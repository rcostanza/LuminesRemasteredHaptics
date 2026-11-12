global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Threading;

namespace LuminesHaptics.Helper;

/// <summary>float math helpers missing from .NET Framework 4.8.</summary>
internal static class MathF
{
    public static float Pow(float x, float y) => (float)Math.Pow(x, y);
    public static float Abs(float x) => Math.Abs(x);
    public static float Sign(float x) => Math.Sign(x);
    public static float Tanh(float x) => (float)Math.Tanh(x);
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
}
