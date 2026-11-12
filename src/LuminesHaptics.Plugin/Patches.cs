using System;
using System.Collections;
using HarmonyLib;

namespace LuminesHaptics
{
    /// <summary>
    /// Hook points (see HAPTICS_MOD_PLAN.md §0). Targets come from Compat.Check(), which has already verified
    /// them against the running game. Returns false if any patch fails; the caller then unpatches everything.
    /// </summary>
    internal static class Patches
    {
        public static bool Apply(Harmony h)
        {
            return P(h, "SoundManager.LoadHapticData", prefix: nameof(LoadHapticData_Prefix))
                 & P(h, "SoundManager.LoadHapticDataNotAsync", prefix: nameof(LoadHapticDataNotAsync_Prefix))
                 & P(h, "SoundManager.LoadSkinAudioClipAsync", prefix: nameof(ClearPrepare_Prefix))
                 & P(h, "SoundManager.LoadSkinAudioClip", prefix: nameof(ClearPrepare_Prefix))
                 & P(h, "SoundManager.SkinChanged", prefix: nameof(SkinChanged_Prefix))
                 & P(h, "SoundManager.PlayHaptic", prefix: nameof(PlayHaptic_Prefix))
                 & P(h, "SoundManager.StopLoopHaptic", postfix: nameof(StopLoops_Postfix))
                 & P(h, "SoundManager.AllStopBGM", postfix: nameof(StopLoops_Postfix))
                 & P(h, "SoundManager.Pause", postfix: nameof(StopAll_Postfix))
                 & P(h, "HapticManager.StandaloneVibration", postfix: nameof(StockRumble_Postfix))
                 & P(h, "HapticManager.StandaloneSEVibration", postfix: nameof(StockRumble_Postfix));
        }

        private static bool P(Harmony h, string hook, string prefix = null, string postfix = null)
        {
            try
            {
                h.Patch(Compat.Hooks[hook],
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
                return true;
            }
            catch (Exception e) { Plugin.Log.LogError($"Hook failed: {hook}: {e}"); return false; }
        }

        // The PC build's LoadHapticData* are `yield break` stubs; replace with a real loader.
        private static bool LoadHapticData_Prefix(string filePath, string playName, string skinID, bool isTrans, ref IEnumerator __result)
        {
            __result = Plugin.Instance.LoadHaptic(filePath, playName, skinID, isTrans, async: true);
            return false;
        }

        private static bool LoadHapticDataNotAsync_Prefix(string filePath, string playName, string skinID, bool isTrans, ref IEnumerator __result)
        {
            __result = Plugin.Instance.LoadHaptic(filePath, playName, skinID, isTrans, async: false);
            return false;
        }

        private static void ClearPrepare_Prefix() => Plugin.ClearPrepare();

        private static void SkinChanged_Prefix()
        {
            Plugin.Instance.StopLoops();
            Plugin.SwapSkin();
        }

        // Original forwards to HapticManager.PlayHapticSE (stock 0.7/0.2s buzz). Replaced only while player 1's
        // DualSense is receiving haptics; otherwise (other controller, no DualSense on USB, player 2) the game's
        // own code runs unchanged.
        private static bool PlayHaptic_Prefix(string playName, VibrationType vibType, bool isLoop, bool leftPan, bool rightPan, int blockPos, int playerType)
        {
            if (!Plugin.HapticsActive || playerType != 0) return true;
            try { Plugin.Instance.Play(playName, vibType, isLoop, leftPan, rightPan, blockPos, playerType); }
            catch (Exception e) { Plugin.Log.LogError("PlayHaptic: " + e); }
            return false;
        }

        private static void StopLoops_Postfix() => Plugin.Instance.StopLoops();
        private static void StopAll_Postfix() => Plugin.Instance.StopAll();

        // The stock rumble code always runs (it rumbles every controller and keeps its own state). Afterwards,
        // only the DualSense that is playing haptics is silenced again; all other controllers keep their rumble.
        // (Not done by patching Rewired's Joystick.SetVibration: it is tiny enough for Mono to inline.)
        private static void StockRumble_Postfix()
        {
            if (Plugin.HapticsActive && Plugin.CfgDisableStockRumble.Value) Plugin.Instance.SilenceHapticPads();
        }
    }
}
