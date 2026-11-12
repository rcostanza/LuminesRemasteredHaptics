# Lumines Remastered (PC) with Dualsense haptics

A mod to provide haptic feedback when used with a Dualsense controller (over USB).

## Tl;dr

You like how Lumines has HD rumble on Switch? Now the PC version has it too, if you have a Dualsense.

## What's this?

Lumines Remastered came out on PC, PS4, Xbox One, and Switch. The latter is the best version for a single reason: HD rumble. All versions have trance vibration, which uses the controller rumble to the music's beat, but it's far more immersive on Switch due to controller haptics.

If you never used a Switch's Joy Con or PS5's Dualsense (that has haptic feedback, a more advanced version of HD rumble), the best way I can explain haptics in this game is that you can _feel_ the music playing through your hands.

Unfortunately the game was never patched for current platforms, so Dualsense support is not a thing and haptics remain exclusive to the Switch version. It's where this mod comes in.

Even on PC, the game comes with the necessary files to provide Switch-like haptics, they're just never used. The mod makes use them when a Dualsense is detected and outputs haptics through a transparent helper application.

Again: no game files are included, the mod reads the haptic data from your own game install.

## Requirements

- Steam version of Lumines Remastered. Untested and unsupported with... _other sources_.

- Steam Input has to be **enabled**. It is by default anyway, the game doesn't natively support the Dualsense.

- A Dualsense controller, connected through USB or using something like the awesome [DS5 Bridge](https://github.com/SundayMoments/DS5_Bridge), that makes it mimic an USB connection while working wirelessly.

- [BepInEx 5.4.23.6](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6), a mod framework for Unity games

## Install

1. [Download BepInEx 5.4.23.6](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip)

2. [Download this mod](https://github.com/rcostanza/LuminesRemasteredHaptics/releases/latest)

3. Extract both to the game folder

## Uninstall

Simply delete the `BepInEx\plugins\LuminesHaptics` folder.

## Settings

The mod should work fine as it is, but there are some customization options if you wish.

The in-game vibration options apply: vibration on/off, rhythm, block landing, L/R panning,
and strength (3 = default).

Other settings are in `BepInEx\config\lumines.haptics.cfg`, created on first launch:

| Setting              | Default | Description                                                                                                                                                                                             |
| -------------------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MasterGain`         | `1.0`   | Overall strength multiplier                                                                                                                                                                             |
| `AmplitudeCurve`     | `0.6`   | Below 1 makes quiet detail stronger                                                                                                                                                                     |
| `DisableStockRumble` | `true`  | Mute the game's own basic rumble on the DualSense while it plays haptics (other controllers always keep their rumble)                                                                                   |
| `DualSenseDetection` | `Auto`  | `Auto`: haptics only when player 1's controller is a DualSense (directly or through Steam Input). `Always`: haptics whenever a DualSense is connected by USB, whatever player 1's controller looks like |
| `UseTransHaptics`    | `false` | Use the alternate "trance vibration" music haptics                                                                                                                                                      |

## What's in the files

- A mod DLL (`LuminesHaptics.dll`)

- A small helper program (`LuminesHapticsHelper.exe`), transparently fired by the plugin, responsible for sending the haptics to the controller

You shouldn't download random executable files, but the source code for both is right here, so you can inspect, modify, and build yourself.

## 🤖 AI disclaimer 🤖

Yep, AI made. I'm a programmer but not a game programmer, I have no familiarity with Unity or BepInEx plugins.

Tested by humans though. A human also did type all of this README.

## FAQ

- Does it work with Joy Cons (Switch 1 or 2) connected to PC through BT?

Not at the moment. It'd be nice, but I don't have Joy Cons at the moment to even try.

- Does it work on Linux?

Yes. It just doesn't have a Linux build, but neither does the game itself or BepInEx.

## Credits

Uses NAudio (MIT license, see third_party\THIRD-PARTY-NOTICES.txt).
.bnvib format notes: Wexos's Wiki.
