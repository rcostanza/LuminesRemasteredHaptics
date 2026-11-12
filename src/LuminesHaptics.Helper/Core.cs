using System.Collections.Concurrent;
using System.Text;

namespace LuminesHaptics.Helper;

/*  UDP protocol (plugin -> helper, 127.0.0.1, little endian). One message per datagram.
      'L' u16 keyLen, key(utf8), bnvib bytes...      load/cache a .bnvib under key
      'P' u8 class, u8 loop, f32 gainL, f32 gainR, u16 keyLen, key   start a voice
      'S' u8 class                                    stop all voices of class (0 = music loops, 1 = SE)
      'A'                                             stop everything
      'G' f32 gain                                    master gain (also the plugin's 0.5 s heartbeat)
    Reply (helper -> plugin, to the sender of each 'G'):
      'D' u8 open                                     1 = DualSense haptics output is playing
    Without a recent 'D' 1 the plugin leaves the game's normal rumble alone.
*/

/// <summary>Decoded .bnvib. Output-agnostic (DualSense now, Joy-Con later).</summary>
public sealed class Bnvib
{
    public int Rate;
    public float[] AmpLow = [], FreqLow = [], AmpHigh = [], FreqHigh = [];
    public int Length => AmpLow.Length;

    // u32 metaSize; u16 format(3); u16 rate; [loop fields]; u32 dataSize; frames [aL, fL, aH, fH]
    // amp = B/255, freq = 10 * 2^(B/32) Hz   (Wexos wiki; verified on all 1265 Lumines files)
    public static Bnvib Parse(byte[] d)
    {
        int meta = BitConverter.ToInt32(d, 0);
        int fmt = BitConverter.ToUInt16(d, 4);
        if (fmt != 3) throw new InvalidDataException($"bnvib format {fmt}");
        int rate = BitConverter.ToUInt16(d, 6);
        int off = 4 + meta;
        int size = BitConverter.ToInt32(d, off);
        int n = Math.Min(size, d.Length - off - 4) / 4;
        var b = new Bnvib { Rate = rate, AmpLow = new float[n], FreqLow = new float[n], AmpHigh = new float[n], FreqHigh = new float[n] };
        int f = off + 4;
        for (int i = 0; i < n; i++)
        {
            b.AmpLow[i] = d[f + 4 * i] / 255f;
            b.FreqLow[i] = 10f * MathF.Pow(2f, d[f + 4 * i + 1] / 32f);
            b.AmpHigh[i] = d[f + 4 * i + 2] / 255f;
            b.FreqHigh[i] = 10f * MathF.Pow(2f, d[f + 4 * i + 3] / 32f);
        }
        return b;
    }
}

/// <summary>Band values for one actuator for one 5 ms tick (same shape as the game's VibrationValues).</summary>
public struct Frame
{
    public float AmpLow, FreqLow, AmpHigh, FreqHigh;
    public static Frame Silent => new() { FreqLow = 160, FreqHigh = 320 };
}

public sealed class Settings
{
    public int Port = 47812;
    public int ParentPid;
    public float Curve = 0.6f, LowScale = 1f, HighScale = 1f;
    public string Device = "DualSense Wireless Controller";
    public string? PlayFile;   // test mode
}

/// <summary>
/// Voice mixer, ticked at 200 Hz by whichever backend drives output. Mixes in the parameter domain
/// ("max amplitude per subband", one of the game's own MixType options) so any backend can consume it.
/// </summary>
public sealed class Mixer
{
    private sealed class Voice
    {
        public Bnvib Data = null!;
        public int Pos;
        public bool Loop;
        public float GainL, GainR;
        public byte Class;
    }

    public const int TickRate = 200;
    private readonly Settings s;
    private readonly ConcurrentDictionary<string, Bnvib> cache = new();
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly List<Voice> voices = [];
    private readonly Frame[] last = [Frame.Silent, Frame.Silent];
    public volatile float MasterGain = 1f;

    public Mixer(Settings settings) => s = settings;

    // ---- thread-safe API (network thread) ----
    public void Load(string key, Bnvib b) => cache[key] = b;

    public void Play(string key, byte cls, bool loop, float gl, float gr)
    {
        if (!cache.TryGetValue(key, out var b)) { Log.Write($"play: unknown key {key}"); return; }
        commands.Enqueue(() =>
        {
            if (loop) voices.RemoveAll(v => v.Loop && v.Data == b); // restart the same layer, don't stack
            voices.Add(new Voice { Data = b, Loop = loop, GainL = gl, GainR = gr, Class = cls });
        });
    }

    public void Stop(byte cls) => commands.Enqueue(() => voices.RemoveAll(v => v.Class == cls));
    public void StopAll() => commands.Enqueue(() => voices.Clear());

    // ---- backend thread ----
    /// <summary>Advance 5 ms. Writes left/right frames (already curve-shaped and master-gained).</summary>
    public void Tick(Frame[] outLR)
    {
        while (commands.TryDequeue(out var c)) c();

        for (int side = 0; side < 2; side++)
        {
            float bestL = 0, bestH = 0;
            float fL = last[side].FreqLow, fH = last[side].FreqHigh;
            foreach (var v in voices)
            {
                float g = side == 0 ? v.GainL : v.GainR;
                float aL = v.Data.AmpLow[v.Pos] * g, aH = v.Data.AmpHigh[v.Pos] * g;
                if (aL > bestL) { bestL = aL; fL = v.Data.FreqLow[v.Pos] * s.LowScale; }
                if (aH > bestH) { bestH = aH; fH = v.Data.FreqHigh[v.Pos] * s.HighScale; }
            }
            // amplitude curve (tuned 0.6), keep aL + aH <= 1, then master gain
            float cl = MathF.Pow(bestL, s.Curve), ch = MathF.Pow(bestH, s.Curve);
            float sum = cl + ch;
            if (sum > 1f) { cl /= sum; ch /= sum; }
            float mg = MasterGain;
            last[side] = new Frame { AmpLow = cl * mg, FreqLow = fL, AmpHigh = ch * mg, FreqHigh = fH };
            outLR[side] = last[side];
        }

        for (int i = voices.Count - 1; i >= 0; i--)
        {
            var v = voices[i];
            if (++v.Pos >= v.Data.Length)
            {
                if (v.Loop) v.Pos = 0; else voices.RemoveAt(i);
            }
        }
    }

    public int ActiveVoices => voices.Count;
}

public static class Log
{
    private static readonly object L = new();
    private static string? path;
    public static void Init(string p) { path = p; File.WriteAllText(p, ""); }
    public static void Write(string msg)
    {
        lock (L)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
            if (path != null) File.AppendAllText(path, line + Environment.NewLine);
            Console.WriteLine(line);
        }
    }
}

public static class Net
{
    public static void Handle(byte[] m, Mixer mixer)
    {
        if (m.Length == 0) return;
        var r = new BinaryReader(new MemoryStream(m));
        switch ((char)r.ReadByte())
        {
            case 'L':
            {
                string key = ReadKey(r);
                var data = r.ReadBytes(m.Length - (int)r.BaseStream.Position);
                mixer.Load(key, Bnvib.Parse(data));
                break;
            }
            case 'P':
            {
                byte cls = r.ReadByte(); bool loop = r.ReadByte() != 0;
                float gl = r.ReadSingle(), gr = r.ReadSingle();
                mixer.Play(ReadKey(r), cls, loop, gl, gr);
                break;
            }
            case 'S': mixer.Stop(r.ReadByte()); break;
            case 'A': mixer.StopAll(); break;
            case 'G': mixer.MasterGain = MathF.Clamp(r.ReadSingle(), 0f, 4f); break;
        }
    }

    private static string ReadKey(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
}
