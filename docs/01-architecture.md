# Cloudpunk — architecture map

Read-only analysis of the installed game at `C:\GOG Games\Cloudpunk`. No game file was
modified. Sources: filesystem inspection, IL2CPP metadata dump, live player log.

## Toolchain used to produce this document

| Step | Tool | Result |
| --- | --- | --- |
| Engine fingerprint | `Cloudpunk_Data/app.info`, `boot.config`, `UnityPlayer.dll` | Unity |
| Exact build | player log `output_log.txt` | `2018.4.29f1 (50cce2edf27f)` |
| Backend | `GameAssembly.dll` (28 MB), `il2cpp_data/Metadata/global-metadata.dat` (6.4 MB), **no** `Managed/` folder | **IL2CPP**, not Mono |
| Metadata version | Il2CppDumper 6.7.46 | `24,1` |
| Structure dump | `tools/analyze_dump.py`, `list_game_components.py`, `extract_class.py` | 6466 types / 32590 methods / 34365 fields |

Reproduce with:

```
python tools\analyze_dump.py analysis\dump
python tools\list_game_components.py
python tools\extract_class.py --global-only GameInput Global Player PlayerCar
```

## Codebase size

| Metric | Value |
| --- | --- |
| Types | 6466 |
| Classes / structs / enums / interfaces | 4461 / 785 / 819 / 229 |
| Methods | 32590 |
| Fields | 34365 |
| String literals | 9606 |
| Game-authored components (MonoBehaviour + ScriptableObject) | ~200 after filtering asset-store noise |

Roughly 60% of the type count is third-party: the Mono BCL, Steamworks.NET (1219 types),
InControl (494), Cinemachine (291), plus asset-store packages. The game's own code is
almost entirely in the **global namespace** — 201 components such as `Player`, `PlayerCar`,
`Vehicle`, `GameInput`, `Global`, `Quest`, `StoryTrigger`.

## Libraries compiled into the build

| Library | Types | Role |
| --- | --- | --- |
| Steamworks.NET | 1219 | Steam init, achievements, **SteamNetworkingSockets** available |
| InControl 1.8.3 (build 9361) | 494 | input abstraction; `PlayerActions` action sets |
| Cinemachine | 291 | all cameras are virtual cameras driven by one `CinemachineBrain` |
| Unity Timeline / Playables | 111 | cutscenes are Timeline assets, driven by `PlayableDirector` |
| TMPro / UnityEngine.UI | 289 | HUD and menus |
| Easy Save 2 | ~40 | persistence — `ES2AutoSave`, `ES2Writer`, `ES2Reader` |
| EasyExcel + MoodkieSecurity | ~130 | localisation tables, 6 loaded at runtime |
| GPUInstancer, Funly.SkyStudio, SCPE, SSFS, FXV, LPWAsset, MEC, Battlehub, WhiteCat | ~500 | rendering, water, shaders, perf |

**No netcode library is compiled in** — no Photon, Mirror, FishNet, NGO or LitNet. The
transport has to be built, but Steamworks' P2P socket API is already reachable.

## Runtime layout

- Scenes: `level0` … `level14` plus 15 `sharedassets` bundles; the bulk of the world is in
  `resources.resource` (3.26 GB), i.e. loaded through Unity's **Resources** system, so any
  prefab can be instantiated at runtime by path with `Resources.Load`.
- Platform: Direct3D 11, x64. Also `Galaxy64.dll` and `GDOC.dll` (GOG) present.
- Save location: `%USERPROFILE%\AppData\LocalLow\ION LANDS\Cloudpunk\`.

## Central architecture

Two singletons own almost everything:

```
Global.Instance      Camera mainCamera, uiCamera
                     CinemachineBrain cameraBrain
                     Player player            <- the single player
                     PlayerCar[] playerCars, PlayerCar playerCar
                     VehicleInterior vehicleInterior
                     Dictionary<string,string> sectors
                     List<StoryTrigger> triggers
                     Item[] inventory
                     CharacterDefinition rania/camus, Character huxley/pashta
                     AudioMixer x3, AudioSource x8, Transform audioListener

GameInput.Instance   PlayerActions playerActions   <- the single action set
                     SaveBindings() / LoadBindings(), Vibrate()
```

Other singletons: `Player.Instance`, `SceneLoader.Instance`, `Minimap.Instance`.

### Input

`GameInput` owns one InControl `PlayerActions` set. UI input goes through
`MyInputModule : InControlInputModule`, so the UI event system is already InControl-driven.
Callbacks such as `Player.OnPickupItem/OnPushButton/OnUseTerminal/OnPlayDontKnow` are wired
straight to actions. InControl natively supports several players and control schemes, so a
synthetic second action set is straightforward to construct.

### Vehicle

Two distinct vehicle kinds:

- `PlayerCar` — the player-driven car, physics-based on a `Rigidbody`, and its **entire
  tuning is public fields**: `stabilizerConstant`, `stabilizerDamperConstant`,
  `engineTorqueCurve` (an `AnimationCurve`), `acceleration`, `minMotorTorque`, `motorTorque`,
  `midMotorTorque`, `speedwayMotorTorque`, `minSteerTorque`, `steerTorque`,
  `maxSteerTorque`/`midSteerTorque`/`speedwaySteerTorque`, `maximumSpeed` and per-surface
  variants, `stableHeight`, `maximumAngularVelocity`, `stability`, `reflection`,
  `Transform[] thrusterSpots`, `maxStatus`, `maxFuel`.
- `ThrusterValues` — per-thruster raycast hover spring: `springForce`, `damperForce`,
  `restLength`, `springVelocity`.

Hover is therefore a raycast spring/damper per thruster plus explicit motor/steer torque
driven by a curve, applied to a standard `Rigidbody`. **All inputs to the simulation are
visible as fields** — the force law itself still has to be recovered from the stripped binary.

- `Vehicle` — ambient NPC traffic. Not physics-driven: it follows `VehicleTrack` splines via
  `EnterTrackAt(track, position, direction)` and `UpdateTick(time, deltaTime)`.
- `RaceVehicle` / `RaceTrack` / `RaceCheckpoint` / `RaceManager` — DLC race mode.

### World

`SceneLoader.Instance.LoadNewScene(int sceneID, bool showUI)` is a coroutine behind a fade,
with `IsLoading()` / `IsFading()` guards. The city is assembled from modules and volumes:
`StreetModule`, `HighwayModule`, `TrackModule`, `ChangeSector`, `StreetVolume`,
`StreetAIVolume`, `MinimapVolume`, `ParkVolume`, `ParkingDisplayVolume`, `RespawnVolume`,
`TeleportVolume`, `CameraControlVolume`. Pedestrians: `AIManager`, `CharacterFactory`,
`CharacterSpawnPoint`, `CrowdCluster`, `SimplePathfinder`, `NavPoint`.

`Player.ChangeScene(sceneID)` / `ChangeSceneRoutine` and
`Dictionary<string,string> Global.sectors` drive transitions.

### Story — a data graph, not opaque logic

```
StoryTrigger : SerializeObject
    inputs[] / outputs[]   dependency graph, inputsOR for AND/OR
    SavedBool unlocked, SavedInt progress
    Unlock(), Check(), ActivateQuest(id), ChangeScene(id), TriggerJobComplete(money),
    FreezeControls(bool), InvalidateDialog(id), UnlockSteamAchievement(id), ...

Dialog        lines: List<DialogLine>, isPlaying, completed, repeatable, inCar/inApartment,
              activatedByCharacter, IsPlayable(makeCall, isInCar)
DialogLine    lineID, voiceFileID, character, text, questActivate/Deactivate, itemGive,
              moneyReward, played, overCom, Complete(repeatable)
Quest         QuestText questText, portrait, marker, UpdateProgress()
CutsceneStarter   PlayableDirector timeline, startAction UnityEvent, activateKey
```

### Persistence — Easy Save 2, binary, unencrypted

Live save at `...\LocalLow\ION LANDS\Cloudpunk\Slot0.sav` (57,555 bytes) has an ES2 binary
header followed by length-prefixed plain keys and integer values, e.g. `Job1_0`, `Job1_1`,
`Job2_2` … `Job4_7`. So **story and quest progress is a flat string→int map in an
unencrypted file**. `SerializeObject`/`SavedObject`/`SavedValue` are the game's own layer on
top of ES2, with `Save(ES2Writer)` / `Load(ES2Reader)` pairs.

### HUD

`Minimap.Instance` (own `Camera` for the radar, `markerRootSmall`/`markerRootFull`),
`VehicleInterior` (cockpit camera, steering wheel, speedometer, pedals, `MinimapIcons`),
`DialogHUD`, `HolocashHUD`, `InventoryUI`, `GarageUI`, `RacingUI`, `Quest`.

## Player log evidence

`output_log.txt` (12,888 bytes) from a real session:

```
Initialize engine version: 2018.4.29f1 (50cce2edf27f)
GfxDevice: Direct3D 11.0 / NVIDIA GeForce RTX 3070 Ti
InControl (version 1.8.3 build 9361)
Detected Steam language: russian
[EasyExcel] 6 tables loaded.
Steam initialized with user: [andre] Cloud enabled: [False]
SteamController.Init failed
Loaded Objects now: 37115
Build path: C:\buildslave\unity\build\Runtime\Export\Debug.bindings.h
```

## Existing modding surface

- BepInEx 6 IL2CPP + Doorstop is proven to work on this exact build — the published VR mod
  (`Astienth/Cloudpunk-VR`) ships `winhttp.dll`, `doorstop_config.ini`,
  `BepInEx.Unity.IL2CPP.dll`, `Il2CppInterop.*`, `Cpp2IL.*`, `0Harmony.dll`,
  `MonoMod.RuntimeDetour.dll` and a `.NET 6` runtime under `dotnet/`.
- The mod scene is otherwise tiny: 6 files on Nexus, 5 of them "Miscellaneous" reshades.
  Nobody has attempted gameplay or co-op work.
- No Steam Workshop support, so distribution has to be a GitHub zip.
- `Cloudpunk_Data/globalgamemanagers` can be swapped (the VR mod does it), which allows
  changing player-facing boot settings.