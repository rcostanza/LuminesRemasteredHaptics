// Reference stub of the LUMINES REMASTERED (PC) types/members used by LuminesHaptics. Names and signatures
// only (written by hand from the game's public API); no game code. See HAPTICS_MOD_PLAN.md section 0.
#pragma warning disable 1591
using System.Collections;
using UnityEngine;

public class SoundManager : MonoBehaviour { }   // patched by method name only

public class HapticManager : MonoBehaviour
{
    public static HapticManager Instance;
    public TextAsset BlockOnSE;
}

public class PlayerInfo : MonoBehaviour
{
    public static PlayerInfo instance;
    public VibrationSettingType controllorVibType;
    public int vibrationPower;
}

public class AssetBundleGameResource : MonoBehaviour
{
    public static AssetBundleGameResource Instance;
    public IEnumerator GetSkinHapticData(string skinName, string hapticName) { throw null; }
    public IEnumerator GetSkinHapticDataNotAsync(string skinName, string hapticName) { throw null; }
    public IEnumerator GetSkinHapticTransData(string skinName, string hapticName) { throw null; }
    public IEnumerator GetSkinHapticTransDataNotAsync(string skinName, string hapticName) { throw null; }
}

public class PlayerBlock : MonoBehaviour
{
    public const int GRID_WIDTH = 16;
}

public enum VibrationType { BGM, BGM_TRANS, SE_ON, SE_BONUS }

public enum VibrationSettingType { PLAY = 2, RHYTHM = 4, ON = 8, LR = 0x10 }
