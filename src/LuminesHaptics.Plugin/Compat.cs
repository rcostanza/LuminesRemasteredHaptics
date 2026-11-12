using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LuminesHaptics
{
    /// <summary>
    /// Startup self-check against the running game build. Uses reflection with string names only, so it can
    /// run (and fail cleanly) even when the game's API changed. Everything the plugin touches must be listed
    /// here, in sync with stubs\Assembly-CSharp and tools\verify-refs.ps1.
    /// </summary>
    internal static class Compat
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>Resolved hook targets, keyed "Type.Method". Filled by Check(); used by Patches.</summary>
        public static readonly Dictionary<string, MethodInfo> Hooks = new Dictionary<string, MethodInfo>();

        private static List<string> problems;

        public static List<string> Check()
        {
            problems = new List<string>();
            Hooks.Clear();

            Type sm = T("SoundManager"), hm = T("HapticManager"), pi = T("PlayerInfo"),
                 ab = T("AssetBundleGameResource"), vt = T("VibrationType"), vs = T("VibrationSettingType"),
                 pb = T("PlayerBlock"), textAsset = Type.GetType("UnityEngine.TextAsset, UnityEngine");
            if (problems.Count > 0) return problems; // nothing else is checkable

            var s = typeof(string); var b = typeof(bool); var i = typeof(int); var ie = typeof(IEnumerator);

            // Hooks. Parameter NAMES matter: Harmony binds prefix arguments by name.
            Hook(sm, "LoadHapticData", ie, P(s, "filePath"), P(s, "playName"), P(s, "skinID"), P(b, "isTrans"));
            Hook(sm, "LoadHapticDataNotAsync", ie, P(s, "filePath"), P(s, "playName"), P(s, "skinID"), P(b, "isTrans"));
            Hook(sm, "LoadSkinAudioClipAsync");
            Hook(sm, "LoadSkinAudioClip");
            Hook(sm, "SkinChanged");
            Hook(sm, "PlayHaptic", typeof(void), P(s, "playName"), P(vt, "vibType"), P(b, "isLoop"), P(b, "leftPan"),
                 P(b, "rightPan"), P(i, "blockPos"), P(i, "playerType"));
            Hook(sm, "StopLoopHaptic");
            Hook(sm, "AllStopBGM");
            Hook(sm, "Pause");
            Hook(hm, "StandaloneVibration");
            Hook(hm, "StandaloneSEVibration");

            // Directly referenced members (must match the stubs exactly).
            Field(hm, "Instance", hm, true);
            Field(hm, "BlockOnSE", textAsset, false);
            Field(pi, "instance", pi, true);
            Field(pi, "controllorVibType", vs, false);
            Field(pi, "vibrationPower", i, false);
            Field(ab, "Instance", ab, true);
            foreach (var m in new[] { "GetSkinHapticData", "GetSkinHapticDataNotAsync", "GetSkinHapticTransData", "GetSkinHapticTransDataNotAsync" })
                Method(ab, m, ie, s, s);

            // Enum values are compiled into the plugin as constants.
            EnumValues(vt, "BGM", 0, "BGM_TRANS", 1, "SE_ON", 2, "SE_BONUS", 3);
            EnumValues(vs, "PLAY", 2, "RHYTHM", 4, "ON", 8, "LR", 0x10);
            var gw = pb.GetField("GRID_WIDTH", All);
            if (gw == null || !gw.IsLiteral || (int)gw.GetRawConstantValue() != 16)
                problems.Add("PlayerBlock.GRID_WIDTH is not the constant 16");

            // Rewired (controller detection, used to decide between DualSense haptics and stock rumble).
            Pads.Resolve(problems);

            return problems;
        }

        private struct Param { public Type Type; public string Name; }
        private static Param P(Type t, string name) => new Param { Type = t, Name = name };

        private static Type T(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t == null) problems.Add("missing type " + name);
            return t;
        }

        private static MethodInfo Single(Type t, string name)
        {
            var ms = t.GetMethods(All).Where(m => m.Name == name && m.DeclaringType == t).ToArray();
            if (ms.Length == 1) return ms[0];
            problems.Add(ms.Length == 0 ? $"missing method {t.Name}.{name}" : $"{t.Name}.{name} is overloaded ({ms.Length})");
            return null;
        }

        private static void Hook(Type t, string name, Type ret = null, params Param[] ps)
        {
            var m = Single(t, name);
            if (m == null) return;
            if (ret != null && m.ReturnType != ret) { problems.Add($"{t.Name}.{name} returns {m.ReturnType.Name}"); return; }
            if (ps.Length > 0)
            {
                var actual = m.GetParameters();
                bool ok = actual.Length == ps.Length;
                for (int k = 0; ok && k < ps.Length; k++)
                    ok = actual[k].ParameterType == ps[k].Type && actual[k].Name == ps[k].Name;
                if (!ok)
                {
                    problems.Add($"{t.Name}.{name} signature changed: ({string.Join(", ", actual.Select(a => a.ParameterType.Name + " " + a.Name).ToArray())})");
                    return;
                }
            }
            Hooks[t.Name + "." + name] = m;
        }

        private static void Method(Type t, string name, Type ret, params Type[] args)
        {
            var m = t.GetMethod(name, All, null, args, null);
            if (m == null || m.ReturnType != ret) problems.Add($"missing method {ret.Name} {t.Name}.{name}({string.Join(", ", args.Select(a => a.Name).ToArray())})");
        }

        private static void Field(Type t, string name, Type type, bool isStatic)
        {
            var f = t.GetField(name, All);
            if (f == null || f.FieldType != type || f.IsStatic != isStatic || !f.IsPublic)
                problems.Add($"missing field {(isStatic ? "static " : "")}{type?.Name} {t.Name}.{name}");
        }

        /// <summary>Pairs of (name, value). net35 has no ValueTuple.</summary>
        private static void EnumValues(Type t, params object[] pairs)
        {
            for (int k = 0; k < pairs.Length; k += 2)
            {
                string name = (string)pairs[k]; int value = (int)pairs[k + 1];
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f == null || Convert.ToInt32(f.GetRawConstantValue()) != value)
                    problems.Add($"enum {t.Name}.{name} != {value}");
            }
        }
    }
}
