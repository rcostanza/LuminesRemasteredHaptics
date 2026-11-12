using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LuminesHaptics
{
    /// <summary>
    /// The game's controllers, read from Rewired (its input library) by reflection only, so the plugin needs
    /// no Rewired reference at build time (CI has no game files). Resolve() runs inside Compat.Check(); if
    /// anything is missing the whole mod stays disabled.
    /// </summary>
    internal static class Pads
    {
        private const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance;
        private static PropertyInfo controllers, isReady, joysticks, name, hwName, hwId, hwGuid, idProp;
        private static FieldInfo idField;
        private static MethodInfo assigned, setVibration;

        public sealed class Pad
        {
            public object Joystick;
            public string Desc;
            public bool IsDualSense;
            public int Player;
        }

        public static void Resolve(List<string> problems)
        {
            var reInput = Type.GetType("Rewired.ReInput, Rewired_Core");
            var joystick = Type.GetType("Rewired.Joystick, Rewired_Core");
            if (reInput == null || joystick == null) { problems.Add("missing type Rewired.ReInput / Rewired.Joystick"); return; }

            controllers = reInput.GetProperties(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(p => p.Name == "controllers");
            isReady = Prop(reInput.GetProperties(BindingFlags.Public | BindingFlags.Static), "isReady", typeof(bool));
            if (controllers == null || isReady == null) { problems.Add("missing Rewired.ReInput.controllers / isReady"); return; }

            var helper = controllers.PropertyType;
            joysticks = helper.GetProperties(Pub).FirstOrDefault(p => p.Name == "Joysticks" && typeof(IEnumerable).IsAssignableFrom(p.PropertyType));
            assigned = helper.GetMethod("IsJoystickAssignedToPlayer", Pub, null, new[] { typeof(int), typeof(int) }, null);
            if (joysticks == null) problems.Add("missing Rewired ReInput.controllers.Joysticks");
            if (assigned == null || assigned.ReturnType != typeof(bool)) problems.Add("missing Rewired ReInput.controllers.IsJoystickAssignedToPlayer(int, int)");

            var props = joystick.GetProperties(Pub);
            idProp = Prop(props, "id", typeof(int));
            if (idProp == null) idField = joystick.GetFields(Pub).FirstOrDefault(f => f.Name == "id" && f.FieldType == typeof(int));
            if (idProp == null && idField == null) problems.Add("missing Rewired.Joystick.id");
            name = Need(props, "name", typeof(string), problems);
            hwName = Need(props, "hardwareName", typeof(string), problems);
            hwId = Need(props, "hardwareIdentifier", typeof(string), problems);
            hwGuid = Need(props, "hardwareTypeGuid", typeof(Guid), problems);
            setVibration = joystick.GetMethod("SetVibration", Pub, null, new[] { typeof(float), typeof(float) }, null);
            if (setVibration == null) problems.Add("missing Rewired.Joystick.SetVibration(float, float)");
        }

        private static PropertyInfo Prop(PropertyInfo[] props, string n, Type t) =>
            props.FirstOrDefault(p => p.Name == n && p.PropertyType == t && p.GetIndexParameters().Length == 0);

        private static PropertyInfo Need(PropertyInfo[] props, string n, Type t, List<string> problems)
        {
            var p = Prop(props, n, t);
            if (p == null) problems.Add($"missing Rewired.Joystick.{n}");
            return p;
        }

        /// <summary>
        /// Every joystick Rewired sees, with its player (0 = player 1, 1 = player 2, -1 = none) and whether it is
        /// the DualSense. The game assigns a controller to player 1 only once a button on it is pressed.
        /// </summary>
        public static List<Pad> Scan()
        {
            var list = new List<Pad>();
            if (!(bool)isReady.GetValue(null, null)) return list;
            var helper = controllers.GetValue(null, null);
            var js = helper == null ? null : joysticks.GetValue(helper, null) as IEnumerable;
            if (js == null) return list;
            foreach (var j in js)
            {
                if (j == null) continue;
                int id = idProp != null ? (int)idProp.GetValue(j, null) : (int)idField.GetValue(j);
                int player = (bool)assigned.Invoke(helper, new object[] { id, 0 }) ? 0
                           : (bool)assigned.Invoke(helper, new object[] { id, 1 }) ? 1 : -1;
                string n = S(name, j), hn = S(hwName, j), hi = S(hwId, j);
                var g = (Guid)hwGuid.GetValue(j, null);
                string kind;
                bool ds = IsDualSense(n, hn, hi, g);
                if (ds) kind = "DualSense";
                else if (IsSteamVirtualPad(n, hi)) { ds = true; kind = "Steam Input virtual pad = DualSense"; }
                else kind = "not a DualSense";
                string pl = player == 0 ? "P1" : player == 1 ? "P2" : "--";
                list.Add(new Pad
                {
                    Joystick = j, Player = player, IsDualSense = ds,
                    Desc = $"[{pl}] '{n.Trim()}' -> {kind}  (hardware '{hn.Trim()}', id '{hi}', type {g})",
                });
            }
            return list;
        }

        private static string S(PropertyInfo p, object o) => (p.GetValue(o, null) as string) ?? "";

        // With Steam Input on (which this game needs for DualSense input), Steam hides the real DualSense and the
        // game only sees an XInput pad, e.g. 'XInput Gamepad 1' / identifier 'WindowsXInputGamepadGamepad'.
        // Its XInput slot reports Valve's vendor id (28DE:11FF), unlike a real Xbox pad (045E). Such a pad counts
        // as the DualSense; HapticsActive additionally requires a DualSense connected by USB. (Edge case: Steam
        // also virtualises another controller while an idle DualSense is plugged in; the old Steam API in this
        // game can't say which physical pad is behind a virtual one.)
        private static bool IsSteamVirtualPad(string n, string hi)
        {
            if ((n + "|" + hi).IndexOf("xinput", StringComparison.OrdinalIgnoreCase) < 0) return false;
            // Rewired names XInput pads "XInput Gamepad <slot + 1>".
            int slot = -1;
            var parts = n.Trim().Split(' ');
            int num;
            if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out num) && num >= 1 && num <= 4) slot = num - 1;
            for (int i = 0; i < 4; i++)
            {
                if (slot >= 0 && i != slot) continue;
                var id = XInputIds.Get(i);
                if (id != null && id[0] == XInputIds.ValveVid) return true;
            }
            return false;
        }

        // This Rewired build is older than the DualSense, so it can't identify it by type. Match by name or by
        // USB ids (Sony 054C; DualSense 0CE6, DualSense Edge 0DF2) when they appear in the identifier. Rewired
        // may also show it as an unrecognised device (empty type GUID) named after its HID product string,
        // "Wireless Controller". The DualShock 4 uses that name too, but Rewired recognises the DS4, so its
        // type GUID isn't empty.
        private static bool IsDualSense(string n, string hn, string hi, Guid g)
        {
            string all = (n + "|" + hn + "|" + hi).ToLowerInvariant();
            if (all.Contains("dualsense")) return true;
            if (all.Contains("054c") && (all.Contains("0ce6") || all.Contains("0df2"))) return true;
            return g == Guid.Empty && all.Contains("wireless controller");
        }

        public static void StopVibration(object joystick) => setVibration.Invoke(joystick, new object[] { 0f, 0f });
    }
}
