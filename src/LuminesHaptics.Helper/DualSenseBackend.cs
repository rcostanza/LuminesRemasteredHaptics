using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LuminesHaptics.Helper;

/// <summary>
/// DualSense over USB: its audio endpoint has 4 channels; channels 3/4 (index 2/3) drive the left/right
/// haptic actuators. Renders the mixer's 200 Hz band frames as two phase-continuous sines per side.
/// Re-opens the device when it is unplugged/replugged.
/// </summary>
public sealed class DualSenseBackend
{
    private readonly Settings s;
    private readonly Mixer mixer;

    public DualSenseBackend(Settings settings, Mixer m) { s = settings; mixer = m; }

    /// <summary>True while the DualSense haptics output is playing. Reported to the plugin, which falls back to
    /// the game's normal rumble when it is false.</summary>
    public volatile bool Open;

    public void Run(CancellationToken ct)
    {
        bool warned = false;
        while (!ct.IsCancellationRequested)
        {
            MMDevice? dev = Find();
            if (dev == null)
            {
                if (!warned) { Log.Write($"No 4ch output device matching '{s.Device}' (DualSense on USB?). Waiting..."); warned = true; }
                ct.WaitHandle.WaitOne(2000);
                continue;
            }
            warned = false;
            try { PlayOn(dev, ct); }
            catch (Exception e) { Log.Write("Output error: " + e); ct.WaitHandle.WaitOne(4000); }
            ct.WaitHandle.WaitOne(1000);
        }
    }

    private MMDevice? Find()
    {
        using var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                if (d.FriendlyName.IndexOf(s.Device, StringComparison.OrdinalIgnoreCase) >= 0 && d.AudioClient.MixFormat.Channels >= 4)
                    return d;
            }
            catch { /* device vanished mid-enumeration */ }
        }
        return null;
    }

    private void PlayOn(MMDevice dev, CancellationToken ct)
    {
        // >2 channels requires WAVEFORMATEXTENSIBLE, so render straight into the device's own mix format
        // (shared-mode mix formats are 32-bit float).
        var mix = dev.AudioClient.MixFormat;
        if (mix.BitsPerSample != 32) throw new NotSupportedException($"mix format {mix} is not 32-bit float");
        var synth = new Synth(mixer, mix.SampleRate, mix.Channels);
        using var stopped = new ManualResetEventSlim();
        using var output = new WasapiOut(dev, AudioClientShareMode.Shared, true, 20);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception != null) Log.Write("Playback stopped: " + e.Exception.Message);
            stopped.Set();
        };
        output.Init(new FloatBytes(synth, mix));
        output.Play();
        Log.Write($"Output: {dev.FriendlyName} ({mix.SampleRate} Hz, {mix.Channels} ch)");
        Open = true;
        try { WaitHandle.WaitAny([stopped.WaitHandle, ct.WaitHandle]); }
        finally { Open = false; }
        output.Stop();
    }

    /// <summary>Exposes float samples as bytes under the device's (extensible) mix format.</summary>
    private sealed class FloatBytes(ISampleProvider src, WaveFormat format) : IWaveProvider
    {
        private float[] buf = [];
        public WaveFormat WaveFormat => format;

        public int Read(byte[] buffer, int offset, int count)
        {
            int n = count / 4;
            if (buf.Length < n) buf = new float[n];
            int got = src.Read(buf, 0, n);
            Buffer.BlockCopy(buf, 0, buffer, offset, got * 4);
            return got * 4;
        }
    }

    private sealed class Synth : ISampleProvider
    {
        private readonly Mixer mixer;
        private readonly int channels, tickLen;
        private readonly Frame[] prev = [Frame.Silent, Frame.Silent], cur = [Frame.Silent, Frame.Silent];
        private readonly double[] phLow = new double[2], phHigh = new double[2];
        private int tickPos;
        private readonly Frame[] tick = new Frame[2];
        private readonly float invRate;

        public WaveFormat WaveFormat { get; }

        public Synth(Mixer m, int sampleRate, int ch)
        {
            mixer = m; channels = ch;
            tickLen = sampleRate / Mixer.TickRate;
            tickPos = tickLen;
            invRate = 1f / sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, ch);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / channels;

            for (int i = 0; i < frames; i++)
            {
                if (tickPos >= tickLen)
                {
                    prev[0] = cur[0]; prev[1] = cur[1];
                    mixer.Tick(tick);
                    cur[0] = tick[0]; cur[1] = tick[1];
                    tickPos = 0;
                }
                float t = (float)tickPos / tickLen; // interpolate params across the tick -> no clicks
                int o = offset + i * channels;
                for (int c = 0; c < channels; c++) buffer[o + c] = 0f;
                buffer[o + 2] = Sample(0, t);
                buffer[o + 3] = Sample(1, t);
                tickPos++;
            }
            return frames * channels;
        }

        private float Sample(int side, float t)
        {
            ref Frame a = ref prev[side];
            ref Frame b = ref cur[side];
            float aL = a.AmpLow + (b.AmpLow - a.AmpLow) * t;
            float aH = a.AmpHigh + (b.AmpHigh - a.AmpHigh) * t;
            float fL = a.FreqLow + (b.FreqLow - a.FreqLow) * t;
            float fH = a.FreqHigh + (b.FreqHigh - a.FreqHigh) * t;
            phLow[side] += 2 * Math.PI * fL * invRate;
            phHigh[side] += 2 * Math.PI * fH * invRate;
            if (phLow[side] > 2 * Math.PI) phLow[side] -= 2 * Math.PI;
            if (phHigh[side] > 2 * Math.PI) phHigh[side] -= 2 * Math.PI;
            float x = aL * (float)Math.Sin(phLow[side]) + aH * (float)Math.Sin(phHigh[side]);
            return SoftClip(x);
        }

        // Linear up to 0.8, then smoothly saturates towards 1 (gain > 1 from the strength setting).
        private static float SoftClip(float x)
        {
            float ax = MathF.Abs(x);
            if (ax <= 0.8f) return x;
            return MathF.Sign(x) * (0.8f + 0.2f * MathF.Tanh((ax - 0.8f) / 0.2f));
        }
    }
}
