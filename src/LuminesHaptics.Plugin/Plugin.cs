using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LuminesHaptics
{
    /// <summary>
    /// Restores the Switch HD-rumble (.bnvib) haptics that the PC build stubbed out, and streams them to
    /// an out-of-process helper (LuminesHapticsHelper.exe) which renders them on the output device
    /// (DualSense over USB now; other backends such as Joy-Con later).
    ///
    /// The mod only takes over while <see cref="HapticsActive"/> is true: the helper reports an open DualSense
    /// haptics device and player 1 is using a DualSense. At all other times every game method runs unchanged,
    /// so any other controller gets the game's normal rumble.
    /// </summary>
    [BepInPlugin(Guid, "Lumines Haptics", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lumines.haptics";
        public const string Version = BuildInfo.Version;

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // Config
        internal static ConfigEntry<bool> CfgEnabled, CfgDisableStockRumble, CfgUseTrans, CfgFollowGameStrength, CfgVerbose;
        internal static ConfigEntry<float> CfgGain, CfgAmpCurve, CfgLowScale, CfgHighScale;
        internal static ConfigEntry<int> CfgPort;
        internal static ConfigEntry<string> CfgDevice;
        internal static ConfigEntry<Detection> CfgDetection;

        internal enum Detection { Auto, Always }

        /// <summary>True while player 1's DualSense is playing the restored haptics. Read by the patches.</summary>
        internal static bool HapticsActive;

        // playName -> haptic key, for the active skin and for the skin being preloaded.
        // Mirrors SoundManager's bgmList/prepareBgmList swap so haptics change with the music.
        internal static Dictionary<string, string> Current = new Dictionary<string, string>();
        internal static Dictionary<string, string> Prepare = new Dictionary<string, string>();
        // key -> raw .bnvib bytes (kept so they can be re-sent if the helper restarts)
        internal static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();

        private Link link;
        private float lastGain = -1f;
        private float heartbeatTimer;

        // Routing state (see HapticsActive)
        private bool deviceOpen;
        private float statusTime = -100f, nextPadScan;
        private List<object> hapticPads = new List<object>();
        private string lastPadDesc, lastReason = "";
        private bool player1Assigned;
        private const float StatusTimeout = 2.5f; // helper answers each 0.5 s heartbeat

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            CfgEnabled = Config.Bind("General", "Enabled", true, "Play restored HD-rumble haptics.");
            CfgDisableStockRumble = Config.Bind("General", "DisableStockRumble", true,
                "While the DualSense plays haptics, silence the game's own basic rumble on it. Other controllers always keep their normal rumble.");
            CfgDetection = Config.Bind("General", "DualSenseDetection", Detection.Auto,
                "Auto: use haptics only when player 1's controller is detected as a DualSense; otherwise the game's normal rumble is used. " +
                "Steam Input is handled: its virtual controller counts as the DualSense while one is connected by USB. " +
                "Always: use haptics whenever a DualSense is connected by USB, whatever player 1's controller looks like.");
            CfgUseTrans = Config.Bind("General", "UseTransHaptics", false,
                "Use the 'Trance vibration' variant of the music haptics (meant for extra controllers on Switch) instead of the player variant.");
            CfgFollowGameStrength = Config.Bind("Strength", "FollowGameSetting", true,
                "Scale by the in-game vibration strength (1-4). 3 (default) = 1.0x.");
            CfgGain = Config.Bind("Strength", "MasterGain", 1.0f, "Extra overall gain.");
            CfgAmpCurve = Config.Bind("Tuning", "AmplitudeCurve", 0.6f,
                "amplitude^curve. <1 lifts quiet detail (feels stronger). 0.6 tuned on DualSense.");
            CfgLowScale = Config.Bind("Tuning", "LowBandFreqScale", 1.0f, "Multiply low-band frequencies.");
            CfgHighScale = Config.Bind("Tuning", "HighBandFreqScale", 1.0f, "Multiply high-band frequencies.");
            CfgPort = Config.Bind("Helper", "Port", 47812, "Localhost UDP port for the helper.");
            CfgDevice = Config.Bind("Helper", "DeviceName", "DualSense Wireless Controller",
                "Substring of the audio output device to drive (DualSense over USB).");
            CfgVerbose = Config.Bind("Debug", "VerboseLog", false, "Log every haptic load/play.");

            // All-or-nothing: if this game build doesn't look exactly like the one we support, do nothing
            // at all (no patches, no helper, stock rumble untouched), so the game behaves as if unmodded.
            var problems = Compat.Check();
            if (problems.Count > 0)
            {
                Log.LogError($"Lumines Haptics {Version} DISABLED: unsupported game version (game updated?). " +
                             "The game is unaffected. Details:\n  " + string.Join("\n  ", problems.ToArray()));
                return;
            }

            var h = new Harmony(Guid);
            if (!Patches.Apply(h))
            {
                h.UnpatchSelf();
                Log.LogError($"Lumines Haptics {Version} DISABLED: patching failed, all patches removed. The game is unaffected.");
                return;
            }

            link = new Link(CfgPort.Value);
            StartHelper();
            active = true;
            Log.LogInfo($"Lumines Haptics {Version} loaded");
        }

        private bool active;

        private void StartHelper()
        {
            try
            {
                // In a Helper\ subfolder of this plugin's own folder, so uninstalling = deleting one folder.
                // BepInEx scans the helper's NAudio DLLs too, but they are plain .NET 4.x libraries with
                // no plugin types, so it just skips them.
                string dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Helper");
                string exe = Path.Combine(dir, "LuminesHapticsHelper.exe");
                if (!File.Exists(exe)) { Log.LogError("Helper not found: " + exe); return; }
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string args = string.Format(inv,
                    "--port {0} --parent {1} --curve {2} --low-scale {3} --high-scale {4} --device \"{5}\"",
                    CfgPort.Value, Process.GetCurrentProcess().Id, CfgAmpCurve.Value,
                    CfgLowScale.Value, CfgHighScale.Value, CfgDevice.Value);
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
                Process.Start(psi);
                Log.LogInfo("Started helper: " + args);
            }
            catch (Exception e) { Log.LogError("Failed to start helper: " + e); }
        }

        // Game types are only touched inside UpdateGain, which Mono JIT-compiles on first call. When the
        // mod is disabled it is never called, so a changed game API can't throw here every frame.
        private void Update()
        {
            if (!active) return;
            try { UpdateGain(); UpdateRouting(); }
            catch (Exception e)
            {
                // Hand everything back to the game (stock rumble, stock PlayHaptic).
                active = false; HapticsActive = false;
                Log.LogError("Disabled after error in Update (the game's normal rumble is used): " + e);
                link.StopAll();
            }
        }

        private void UpdateGain()
        {
            float g = CfgGain.Value;
            if (CfgFollowGameStrength.Value && PlayerInfo.instance != null)
                g *= Mathf.Clamp(PlayerInfo.instance.vibrationPower, 1, 4) / 3f;
            if (!CfgEnabled.Value) g = 0f;
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (!Mathf.Approximately(g, lastGain) || heartbeatTimer <= 0f)
            {
                lastGain = g;
                heartbeatTimer = 0.5f;
                link.Gain(g); // the helper answers with its device status
            }
        }

        /// <summary>Decides between DualSense haptics and the game's normal rumble (HapticsActive).</summary>
        private void UpdateRouting()
        {
            float now = Time.unscaledTime;
            bool? status = link.PollStatus();
            if (status.HasValue) { deviceOpen = status.Value; statusTime = now; }
            if (now >= nextPadScan) { nextPadScan = now + 0.5f; ScanPads(); }

            string reason =
                !CfgEnabled.Value ? "disabled in config" :
                now - statusTime > StatusTimeout ? "helper not running" :
                !deviceOpen ? "no DualSense connected by USB" :
                !player1Assigned ? "waiting for player 1 to press a button on their controller" :
                hapticPads.Count == 0 ? "player 1 isn't using a DualSense" : null;
            bool want = reason == null;
            if (want != HapticsActive)
            {
                HapticsActive = want;
                if (!want) link.StopAll();
            }
            // While the helper is still starting (no reply yet, up to 5 s), don't log a misleading reason.
            bool starting = statusTime < 0f && now < 5f;
            if (reason != lastReason && !starting)
            {
                lastReason = reason;
                Log.LogInfo(want ? "DualSense haptics ON" : $"DualSense haptics off ({reason}): the game's normal rumble is used");
            }
        }

        private void ScanPads()
        {
            var all = Pads.Scan();
            bool always = CfgDetection.Value == Detection.Always;
            var pads = new List<object>();
            var desc = new StringBuilder();
            bool p1 = false;
            foreach (var p in all)
            {
                if (p.Player == 0)
                {
                    p1 = true;
                    if (p.IsDualSense || always) pads.Add(p.Joystick);
                }
                desc.Append("\n  ").Append(p.Desc);
            }
            hapticPads = pads;
            player1Assigned = p1;
            if (all.Count == 0) desc.Append(" none");
            desc.Append("\n  XInput slots (vendor:product): ").Append(XInputIds.Describe());
            if (always) desc.Append("\n  (DualSenseDetection = Always: any player 1 controller gets haptics)");
            string d = desc.ToString();
            if (d != lastPadDesc)
            {
                lastPadDesc = d;
                Log.LogInfo("Controllers:" + d);
            }
        }

        /// <summary>Called after the game's stock rumble ran: stop it again on the DualSense playing haptics.</summary>
        internal void SilenceHapticPads()
        {
            try { foreach (var j in hapticPads) Pads.StopVibration(j); }
            catch (Exception e) { Log.LogDebug("SilenceHapticPads: " + e.Message); }
        }

        private void OnApplicationQuit() { if (active) link.StopAll(); }

        // ---- called from patches ----

        internal static string MakeKey(string skinID, string filePath, bool isTrans) =>
            skinID + (isTrans ? "/T/" : "/") + filePath;

        /// <summary>
        /// Replacement for the game's LoadHapticData stubs. The game waits on this inside its own sound-loading
        /// coroutine, so it must never throw: any failure just means "no haptic for this sound". (C# forbids
        /// yield inside try/catch, so the real loader is stepped manually.)
        /// </summary>
        internal IEnumerator LoadHaptic(string filePath, string playName, string skinID, bool isTrans, bool async)
        {
            IEnumerator inner = null;
            try { inner = LoadHapticCore(filePath, playName, skinID, isTrans, async); }
            catch (Exception e) { LoadFailed(filePath, e); }
            if (inner == null) yield break;
            while (true)
            {
                object step;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    step = inner.Current;
                }
                catch (Exception e) { LoadFailed(filePath, e); yield break; }
                yield return step;
            }
        }

        private static int loadErrors;

        private static void LoadFailed(string filePath, Exception e)
        {
            if (loadErrors++ < 5) Log.LogError($"Haptic load failed for {filePath} (skipped): {e}");
        }

        private IEnumerator LoadHapticCore(string filePath, string playName, string skinID, bool isTrans, bool async)
        {
            var res = AssetBundleGameResource.Instance;
            if (res == null) yield break;
            string key = MakeKey(skinID, filePath, isTrans);
            byte[] data;
            if (!Cache.TryGetValue(key, out data))
            {
                IEnumerator co = isTrans
                    ? (async ? res.GetSkinHapticTransData(skinID, filePath) : res.GetSkinHapticTransDataNotAsync(skinID, filePath))
                    : (async ? res.GetSkinHapticData(skinID, filePath) : res.GetSkinHapticDataNotAsync(skinID, filePath));
                yield return res.StartCoroutine(co);
                var ta = co.Current as TextAsset;
                if (ta == null)
                {
                    if (CfgVerbose.Value) Log.LogDebug($"no haptic for {key}");
                    yield break;
                }
                data = ta.bytes;
                Cache[key] = data;
            }
            Prepare[playName] = key;
            link.Load(key, data);
            if (CfgVerbose.Value) Log.LogInfo($"loaded {playName} -> {key} ({data.Length} B)");
        }

        internal static void ClearPrepare() => Prepare = new Dictionary<string, string>();

        internal static void SwapSkin()
        {
            Current = Prepare;
            Prepare = new Dictionary<string, string>();
            if (CfgVerbose.Value) Log.LogInfo($"skin swap: {Current.Count} haptics active");
        }

        internal void Play(string playName, VibrationType type, bool loop, bool left, bool right, int blockPos, int playerType)
        {
            if (!HapticsActive || PlayerInfo.instance == null) return;
            if (playerType != 0) return; // single output device = player 1
            var vib = PlayerInfo.instance.controllorVibType;
            if (((int)vib & (int)VibrationSettingType.PLAY) == 0) return;

            byte cls;
            switch (type)
            {
                case VibrationType.BGM:
                    if (CfgUseTrans.Value || ((int)vib & (int)VibrationSettingType.RHYTHM) == 0) return;
                    cls = Link.ClassLoop; break;
                case VibrationType.BGM_TRANS:
                    if (!CfgUseTrans.Value || ((int)vib & (int)VibrationSettingType.RHYTHM) == 0) return;
                    cls = Link.ClassLoop; break;
                case VibrationType.SE_ON:
                    if (((int)vib & (int)VibrationSettingType.ON) == 0) return;
                    cls = Link.ClassSE; break;
                default:
                    cls = Link.ClassSE; break;
            }

            string key;
            if (!Current.TryGetValue(playName, out key) && !Prepare.TryGetValue(playName, out key)
                && !(type == VibrationType.SE_ON && TryBlockOn(out key)))
            {
                if (CfgVerbose.Value) Log.LogDebug($"play {playName}: no haptic");
                return;
            }

            // Pan: block column (0..15) for landings, else L/R flags. Equal-power-ish but full at centre.
            float gl = 1f, gr = 1f;
            bool panOn = ((int)vib & (int)VibrationSettingType.LR) != 0;
            if (panOn)
            {
                if (blockPos >= 0)
                {
                    float p = Mathf.Clamp01(blockPos / (float)(PlayerBlock.GRID_WIDTH - 1));
                    gl = Mathf.Min(1f, 2f * (1f - p));
                    gr = Mathf.Min(1f, 2f * p);
                }
                else if (left && !right) { gl = 1f; gr = 0.3f; }
                else if (right && !left) { gl = 0.3f; gr = 1f; }
            }
            link.Play(key, cls, loop, gl, gr);
            if (CfgVerbose.Value) Log.LogInfo($"play {playName} [{type}] loop={loop} pan=({gl:0.00},{gr:0.00})");
        }

        // Block landings have no per-skin .bnvib: the Switch build played the global
        // HapticManager.BlockOnSE TextAsset (Damped_Long.bnvib in sharedassets0), which still ships on PC.
        private const string BlockOnKey = "global/BlockOnSE";
        private bool blockOnSent;

        private bool TryBlockOn(out string key)
        {
            key = BlockOnKey;
            if (blockOnSent) return true;
            var ta = HapticManager.Instance != null ? HapticManager.Instance.BlockOnSE : null;
            if (ta == null) { Log.LogWarning("HapticManager.BlockOnSE is not set; no block-landing haptic"); blockOnSent = false; return false; }
            Cache[BlockOnKey] = ta.bytes;
            link.Load(BlockOnKey, ta.bytes);
            blockOnSent = true;
            Log.LogInfo($"Using BlockOnSE '{ta.name}' for block landings");
            return true;
        }

        internal void StopLoops() => link.Stop(Link.ClassLoop);
        internal void StopAll() => link.StopAll();
    }

    /// <summary>Localhost UDP link to the helper. Protocol documented at the top of the helper's Core.cs.</summary>
    internal sealed class Link
    {
        public const byte ClassLoop = 0, ClassSE = 1;
        private readonly UdpClient udp;
        private readonly IPEndPoint ep;

        public Link(int port)
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            ep = new IPEndPoint(IPAddress.Loopback, port);
            // Windows reports "port unreachable" (helper not up yet) as an error on the next receive; turn that off.
            const int SIO_UDP_CONNRESET = -1744830452;
            try { udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[4], null); } catch { }
        }

        /// <summary>Latest device status from the helper since the last call ('D' replies), or null if none.</summary>
        public bool? PollStatus()
        {
            bool? status = null;
            try
            {
                while (udp.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var b = udp.Receive(ref from);
                    if (from.Port == ep.Port && b.Length >= 2 && b[0] == (byte)'D') status = b[1] != 0;
                }
            }
            catch (SocketException) { }
            return status;
        }

        private void Send(byte[] b)
        {
            try { udp.Send(b, b.Length, ep); } catch (Exception e) { Plugin.Log.LogDebug("udp: " + e.Message); }
        }

        private static void Key(BinaryWriter w, string key)
        {
            var k = Encoding.UTF8.GetBytes(key);
            w.Write((ushort)k.Length); w.Write(k);
        }

        public void Load(string key, byte[] data)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)'L'); Key(w, key); w.Write(data);
            Send(ms.ToArray());
        }

        public void Play(string key, byte cls, bool loop, float gl, float gr)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)'P'); w.Write(cls); w.Write((byte)(loop ? 1 : 0)); w.Write(gl); w.Write(gr); Key(w, key);
            Send(ms.ToArray());
        }

        public void Stop(byte cls) => Send(new[] { (byte)'S', cls });
        public void StopAll() => Send(new[] { (byte)'A' });

        public void Gain(float g)
        {
            var b = new byte[5]; b[0] = (byte)'G';
            Array.Copy(BitConverter.GetBytes(g), 0, b, 1, 4);
            Send(b);
        }
    }
}
