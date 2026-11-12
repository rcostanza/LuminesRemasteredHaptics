using System;
using System.Runtime.InteropServices;

namespace LuminesHaptics
{
    /// <summary>
    /// USB vendor/product id of the device in each XInput slot, via xinput1_4's undocumented
    /// XInputGetCapabilitiesEx (ordinal 108). Steam Input's virtual pad reports Valve's vendor id (28DE).
    /// Resolved with GetProcAddress at runtime; any failure just means "unknown".
    /// </summary>
    internal static class XInputIds
    {
        public const ushort ValveVid = 0x28DE;

        [StructLayout(LayoutKind.Sequential)]
        private struct CapsEx
        {
            public byte Type, SubType; public ushort Flags;
            public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY;
            public ushort LeftMotor, RightMotor;
            public ushort VendorId, ProductId, VersionNumber, Unk1; public uint Unk2;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint GetCapsEx(uint one, uint userIndex, uint flags, out CapsEx caps);

        [DllImport("kernel32", SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32")] private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        private static GetCapsEx fn;
        private static bool tried;

        private static GetCapsEx Fn()
        {
            if (tried) return fn;
            tried = true;
            try
            {
                var h = LoadLibrary("xinput1_4.dll");
                var p = h == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(h, (IntPtr)108);
                if (p != IntPtr.Zero) fn = (GetCapsEx)Marshal.GetDelegateForFunctionPointer(p, typeof(GetCapsEx));
            }
            catch (Exception e) { Plugin.Log.LogDebug("XInputGetCapabilitiesEx unavailable: " + e.Message); }
            return fn;
        }

        /// <summary>(vid, pid) of the pad in XInput slot 0-3, or null if empty/unknown.</summary>
        public static ushort[] Get(int slot)
        {
            var f = Fn();
            if (f == null) return null;
            try
            {
                CapsEx c;
                return f(1, (uint)slot, 0, out c) == 0 ? new[] { c.VendorId, c.ProductId } : null;
            }
            catch { return null; }
        }

        public static string Describe()
        {
            if (Fn() == null) return "unavailable";
            var s = "";
            for (int i = 0; i < 4; i++)
            {
                var id = Get(i);
                s += $"[{i}] " + (id == null ? "-" : $"{id[0]:X4}:{id[1]:X4}") + " ";
            }
            return s.TrimEnd();
        }
    }
}
