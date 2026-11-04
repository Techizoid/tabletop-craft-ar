# HoloLens 2

This fork adds a **Microsoft HoloLens 2** build of the tabletop app, in the spirit of the 2015 Minecraft HoloLens demo: the live world sits on your real table, and you play it with your hands and your voice. It uses the same Paper plugin, protocol and server starter as the Quest 3 app; nothing on the server changes. The Quest 3 build keeps working from the same project.

> **Status: written, not yet run on a device.** The hand and voice mapping is engine-free and unit-tested (`quest-app/Tests~/PadTests.cs`). The Unity side has not been compiled or built for UWP yet, and nothing has been tried on a HoloLens 2. Expect a first round of fixes on the first build. Gesture thresholds, the face budget and the light floor are starting points to tune on the device.

> **Platform support:** Unity deprecated HoloLens 2 support in July 2025. It is still in Unity 6 up to 6.3 (this project pins 6000.0.84f1), but gets no new fixes. Microsoft's HoloLens 2 software support ends on 31 December 2027.

## What you need

- A **Windows 10 or 11 PC**. The UWP build and deployment only work on Windows.
- **Unity 6000.0.84f1** with **Universal Windows Platform Build Support** (Unity Hub, Installs, Add modules).
- **Visual Studio 2022** with the *Universal Windows Platform development* and *Desktop development with C++* workloads, the **C++ ARM64 build tools** and a Windows 10/11 SDK.
- The **[Mixed Reality Feature Tool](https://aka.ms/MRFeatureTool)**, for Microsoft's Mixed Reality OpenXR Plugin.
- A **HoloLens 2** in Developer Mode (Settings > Update & Security > For developers), on the same Wi-Fi as the server PC.
- The server set up as for Quest: [QUEST_SETUP.md](QUEST_SETUP.md) (server starter, Minecraft: Java Edition 26.3 started once).

## Build and deploy

1. Open `quest-app` in Unity. The Android target is fine to start with.
2. Run the **Mixed Reality Feature Tool**: pick the `quest-app` folder, then *Discover Features* > *Platform Support* > **Mixed Reality OpenXR Plugin** (latest), *Get Features*, *Import*, *Approve*. Go back to Unity and let it import.
3. **TabletopCraft > HoloLens 2 > Configure Project.** This:
   - adds Unity's **XR Hands** package if it's missing, then carries on by itself after Unity reloads;
   - sets up the Universal Windows Platform: IL2CPP, ARM64, D3D, the Holographic device family, the capabilities (network, LAN, microphone, spatial perception) and the `TABLETOP_HL2` define;
   - turns on OpenXR for UWP with single-pass instanced rendering, 16-bit depth submission, the Microsoft HoloLens feature group, the hand tracking subsystem and the hand, eye-gaze and motion-controller interaction profiles, then runs OpenXR's automatic validation fixes;
   - builds the scene `Assets/Scenes/TabletopHoloLens2.unity` and switches to the Universal Windows Platform.
4. Open **Project Settings > XR Plug-in Management > OpenXR** on the Universal Windows Platform tab. Check that *Microsoft HoloLens* and *Hand Tracking Subsystem* are ticked, then fix any yellow warnings (or use **Mixed Reality > Project Validation Settings > HoloLens 2 Application (UWP) > Fix all**).
5. **TabletopCraft > HoloLens 2 > Build Visual Studio Solution.** The solution lands in `quest-app/Builds/HoloLens2`.
6. Open the `.sln` in Visual Studio. Choose **Release**, **ARM64** and either **Device** (USB) or **Remote Machine** (the HoloLens IP address), then **Debug > Start Without Debugging**. The first build takes a while.

From the command line, on Windows:

```
Unity -batchmode -nographics -projectPath quest-app -buildTarget WSAPlayer -executeMethod TabletopCraft.EditorTools.HoloLens2Setup.ConfigureBatch -logFile hl2-setup.log
Unity -batchmode -nographics -quit -projectPath quest-app -buildTarget WSAPlayer -executeMethod TabletopCraft.EditorTools.HoloLens2Setup.BuildSolution -logFile hl2-build.log
```

Run `ConfigureBatch` without `-quit`. If it adds XR Hands it exits with code 2: run it again until it logs `HoloLens 2 setup done`.

To go back to the Quest build, switch the platform to Android and run **TabletopCraft > Create Scene** (or `TabletopBatch.Setup`). `TABLETOP_HL2` belongs to the UWP platform only, and `BuildApk` never builds the HoloLens 2 scene.

## First run

The first run is the same as on Quest. Start the app at a table and it finds the server on the LAN and shows a 6-digit code. Type `tabletop pair <code>` in the server window. The game files (about 75 MB) then download once. On Windows, the server PC's network must be **Private** so the firewall rules apply.

The world first appears about half a metre in front of you, a little below eye height. To put it on your table, rest your **right hand on the table** and say **"place world"**.

## Controls

The right hand's ray comes from your shoulder through your hand, like other HoloLens apps. **With your hand down, the ray follows your gaze**: look at a block, raise a hand and air-tap.

| What | Quest 3 | HoloLens 2 |
|---|---|---|
| Break a block, click the menu, panels and cards | right trigger | **air tap** with the right hand (index finger and thumb) |
| Use or place | left trigger | air tap with the left hand |
| Grip (grab a mob, modifier) | grip | **make a fist** |
| Move and turn the table | both grips | both fists, then move the right hand |
| Glide around the world | left stick | pinch the left **middle finger** and thumb, then move the hand; or say "glide forward", "glide back", "glide left", "glide right" |
| Up and down | left grip + left stick | say "go up" or "go down" |
| Zoom (scale the miniature) | right stick up/down | pinch the right middle finger, then move forward or back; or say "zoom in", "zoom out" |
| Turn the miniature | right stick left/right | right middle-finger pinch, then move sideways; or say "turn left", "turn right" |
| Stop spoken movement | | say "stop moving" |
| Stick click (area width, auto-fit) | stick click | pinch the **ring finger** and thumb |
| Put the table at your hand | hold A | say **"place world"** |
| Pick the block under the ray | B | say "pick block" |
| Undo | hold B | say "undo that" |
| Block menu | menu button | say "open menu" (toggles; "close menu" too) |
| Operator panel (chat, console, players) | X | say "operator panel" |
| Area setup | Y | say "area setup", then "confirm area" (A) or "cancel setup" (B) |

The phrases are in English. Each has two words or more so the recognizer is steadier, and none clashes with HoloLens system commands. A pinch only counts while the other fingers stay open, so a fist is never an air tap. HoloLens 2 has no haptics: the app's sounds and visuals confirm actions.

## Differences from the Quest build

- **No automatic table detection yet.** The Quest app snaps to the table from the headset's Space Setup. On HoloLens 2 you place the world with your hand and "place world". The placement is not remembered between sessions.
- **Additive display.** Black shows as clear, so dark caves and night scenes fade out. The app raises the darkest light level to 0.4 (Quest: 0.2). Override it with `"min_light"` in `tabletop.json`.
- **Smaller budget.** HoloLens 2 has far less GPU than a Quest 3, so area setup treats areas over about **60k faces** as over budget (Quest: 250k). The Quest's hold-B override has no voice command yet; to raise the cap, set `"quad_budget": 100000` in `tabletop.json`. Meshing uses 2 worker threads (`"mesh_workers"`).
- **No clock or foveation settings.** The `perf` and `foveation` keys in `tabletop.json` don't apply.
- **Files on the device.** `tabletop.json` and the game files live in the app's `LocalState` folder. You can reach it with the Windows Device Portal (*System > File explorer > LocalAppData > TabletopCraftAR_… > LocalState*). With pairing and the automatic game files you shouldn't need it.

## Troubleshooting

- **The UWP build fails inside a `com.meta.xr.*` package.** The Meta SDK is only needed for the Quest build. In this fork, remove `com.meta.xr.sdk.core` and `com.meta.xr.mrutilitykit` from `quest-app/Packages/manifest.json` while you build for HoloLens 2. The project's own code doesn't need them with `TABLETOP_HL2` set.
- **"Microsoft's Mixed Reality OpenXR Plugin is missing"** in the Console: run step 2 again, then Configure Project.
- **No voice:** the app needs the microphone. Check *Settings > Privacy > Microphone* on the HoloLens. The log shows `VOICE listening for 20 phrases` when it works, and `VOICE <phrase>` for each command heard.
- **No hands:** check that *Hand Tracking Subsystem* is ticked under OpenXR (UWP tab).
- **Logs:** Visual Studio's Output window, or the Device Portal. Look for lines starting with `[Tabletop]`.

## How it's built

- `quest-app/Assets/Scripts/World/PadInput.cs`: engine-free. Hand joints become trigger, grip, stick and stick-click (pinch and fist detection with hysteresis, and a pinch-and-move stick). Spoken phrases become timed button presses and stick pushes, with the same down/up edges OVRInput gives. It also holds the per-device profile. Tested in `Tests~/PadTests.cs`, which also checks the phrases against the app's own `TapHoldButton` and `HoldButton` timing.
- `XR/TInput.cs`: the single input facade. The Quest build forwards to `OVRInput`; with `TABLETOP_HL2` it reads the pad. Every `OVRInput` call in the app now goes through it, so the call sites look the same.
- `XR/HoloLensInput.cs`: XR Hands joints and Windows keyword recognition feed the pad. `XR/HoloLensRig.cs`: the head camera and the two hand transforms (shoulder-to-hand rays, gaze when a hand is down).
- `Editor/HoloLens2Setup.cs`: Configure Project, Create Scene and Build Visual Studio Solution. Meta-only code (`OVRManager`, MRUK, the Quest scene builder and Setup) is left out with `TABLETOP_HL2`.

## Next steps

- Table detection from HoloLens spatial mapping (AR Foundation plane detection through the Mixed Reality OpenXR Plugin), and remembering the placement with spatial anchors.
- More of the E3 2015 demo by voice: lightning, follow a player, jump to a Mark.
- Tune the gesture thresholds, the face budget and the light floor on a device, then record the results in [OVERVIEW.md](OVERVIEW.md#status) and [TESTING.md](TESTING.md).
