// Reference stub of the Unity 5.6 UnityEngine API surface used by LuminesHaptics (+ BepInEx's base types).
#pragma warning disable 1591
using System.Collections;

namespace UnityEngine
{
    public class Object
    {
        public string name { get { throw null; } set { } }
        // Unity's overloaded null checks (destroyed objects compare equal to null). Must exist here or the
        // plugin would compile plain reference comparisons.
        public static bool operator ==(Object x, Object y) { throw null; }
        public static bool operator !=(Object x, Object y) { throw null; }
        public static implicit operator bool(Object exists) { throw null; }
    }

    public class Component : Object { }
    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) { throw null; }
    }

    public class YieldInstruction { }
    public sealed class Coroutine : YieldInstruction { }

    public class TextAsset : Object
    {
        public byte[] bytes { get { throw null; } }
    }

    public struct Mathf
    {
        public static int Clamp(int value, int min, int max) { throw null; }
        public static float Clamp01(float value) { throw null; }
        public static float Min(float a, float b) { throw null; }
        public static bool Approximately(float a, float b) { throw null; }
    }

    public sealed class Time
    {
        public static float unscaledDeltaTime { get { throw null; } }
        public static float unscaledTime { get { throw null; } }
    }
}
