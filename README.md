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

2. [Download this mod](https://github.com/rcostanza/LuminesRemasteredHaptics/releases/latest) (the file `LuminesHaptics-vX.X.zip`, not the source code)

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

### Why an executable is necessary here? Can't you just give me a .dll file like most other Unity mods?

The short answer: a DualSense over USB doesn't take haptics as controller commands. It takes them as audio, and the game's runtime is too old to drive that audio device itself.

<details>
<summary>Click here for the long answer</summary>

1. On a DualSense, haptics are an audio stream

Over USB the DualSense shows up in Windows as a 4-channel sound device. Channels 3 and 4 drive the left and right voice-coil actuators (src/LuminesHaptics.Helper/DualSenseBackend.cs:6-8). So to "play haptics" you have to:

- find that specific output device by name, ignoring the default speakers (Find(), line 43);
- open it through WASAPI with a multichannel format (WAVEFORMATEXTENSIBLE, 32-bit float at the device's mix rate);
- keep generating samples continuously in real time. The helper turns the Switch .bnvib data into two sine waves per side, with 20 ms buffers.

Simple rumble works differently: it's a HID/XInput call, and Steam Input already handles that for the game.

2. The game process can't do this well

- Old runtime. Lumines Remastered runs on Unity 5.6 with old Mono at the CLR 2.0 level, so the plugin targets net35 (LuminesHaptics.Plugin.csproj). NAudio's WASAPI support needs a modern .NET, and the helper targets net48. Doing it in-process would mean hand-writing COM interop for WASAPI against an old Mono with shaky COM support.
- Unity's audio can't help. Unity sends sound to one output device, mixed for speakers. It can't play 4 channels to a second device such as the controller.
- Real-time timing. Audio output has to be fed every few milliseconds. Unity's old Mono garbage collector pauses everything when it runs, and inside the game those pauses would cause audible or felt dropouts in the haptics. A separate process with its own high-priority output thread avoids that.

3. A separate process also keeps the game safe

If the audio device disappears (unplugged, sleep, or DS5 Bridge reconnecting), the helper just retries every few seconds (Run(), line 22). The game never has to deal with audio driver errors, and a crash in the helper can't take the game down.

How the pieces fit

- The plugin stays small. It hooks the game's haptic calls, loads the .bnvib data, and starts the helper hidden (CreateNoWindow) with the game's process ID (Plugin.cs:117-133).
- It then sends play and stop events to the helper over UDP on localhost, port 47812 by default (Program.cs:56).
- The helper reports whether the DualSense output is open. If it isn't, the plugin falls back to the game's normal rumble.
- The helper watches the game's process and exits when the game does (Program.cs:44-53).

A side benefit is that the helper can run without the game. --play-file plays a .bnvib file directly for testing. The plugin's comments also describe the helper as the place where other backends, such as Joy-Con, would go later.

</details>

## Building from source

### Prerequisites

- Windows
- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) or newer (the projects target .NET Framework 3.5 / 4.8, the reference assemblies are pulled from NuGet automatically)
- `BepInEx.dll` and `0Harmony.dll` from [BepInEx 5.4.23.6](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6). Either have BepInEx installed in the game folder, or just extract the BepInEx zip anywhere and point the build at its `BepInEx\core` folder.

No game files are needed to build: the plugin compiles against hand-written stubs in `stubs\`.

### Build the release zip

From the repository root, in PowerShell:

```powershell
# Uses BepInEx from the default Steam install path of the game
.\tools\build-release.ps1

# Or point it at any BepInEx\core folder, and optionally set the version
.\tools\build-release.ps1 -BepInExCoreDir "C:\path\to\BepInEx\core" -Version 0.2
```

This produces `dist\LuminesHaptics-v<version>.zip`, laid out the same as the official release: extract it to the game folder.

### Build the projects individually

```powershell
dotnet build src\LuminesHaptics.Plugin -c Release -p:BepInExCoreDir="C:\path\to\BepInEx\core"
dotnet build src\LuminesHaptics.Helper -c Release
```

If the game is installed somewhere other than the default Steam folder, you can pass `-p:GameDir="D:\Games\LUMINES REMASTERED"` instead of `BepInExCoreDir`.

Outputs:

- `src\LuminesHaptics.Plugin\bin\Release\LuminesHaptics.dll` goes to `BepInEx\plugins\LuminesHaptics\`
- `LuminesHapticsHelper.exe`, `LuminesHapticsHelper.exe.config`, `NAudio.Core.dll` and `NAudio.Wasapi.dll` from `src\LuminesHaptics.Helper\bin\Release\` go to `BepInEx\plugins\LuminesHaptics\Helper\`

### Checking the stubs

If you change the plugin or the stubs, run `.\tools\verify-refs.ps1` (optionally with `-GameDir "<game folder>"`) after a Release build. It checks every game type and member the plugin uses actually exists in your real game install with BepInEx.

## 🤖 AI disclaimer 🤖

Yep, AI made. I'm a programmer but not a game programmer, I have no familiarity with Unity or BepInEx plugins.

Tested by humans though. A human also did type all of this README.

## FAQ

- Joy cons (Switch 1 and 2) have HD rumble, it's haptics too. Do they work here?

Not at the moment. It'd be nice, but I don't have Joy Cons at the moment to even try.

- Does it work on Linux?

Yes. It just doesn't have a Linux build, but neither does the game itself or BepInEx.

## Credits

Uses NAudio (MIT license, see third_party\THIRD-PARTY-NOTICES.txt).
.bnvib format notes: Wexos's Wiki.
