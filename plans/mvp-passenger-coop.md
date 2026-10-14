# MVP — двухplayersная езда на одной машине

Objective: хост запускает игру, подключает друга, друг появляется в его машине; кнопкой места
меняются, и ехать можно вдвоём.

Status: план. Ничего из кода ещё не написано.

## Definition of done

Two PCs, one session. Host's HOVA is the only real car. The friend's avatar is visible in the
passenger seat on the host's screen. Pressing one key swaps seats — the friend now drives
(their inputs reach the host's machine and move the real car), the host's avatar becomes the
passenger. Both screens show both characters in one car. They drive around Nivalis together.
No story sync, no economy, no shared minimap, no HUD changes.

## Roles and the one rule that makes it work

| Concept | Meaning |
| --- | --- |
| Session host | opens the session, owns the real `PlayerCar` Rigidbody |
| Car host | **always the session host** — the one machine that simulates physics |
| Guest | runs a kinematic proxy of the car |
| SeatOwner | `Host` or `Guest` — who currently drives |

Physics authority never moves. Only the *input* moves between machines. That is what keeps
this from becoming a distributed-simulation project.

```
 car host (real Rigidbody)                 guest (kinematic proxy)
 ┌──────────────────────────────┐        ┌──────────────────────────────┐
 │ SeatOwner=Host               │        │ SeatOwner=Host               │
 │   local input -> real car    │ CARSTATE│   local input -> sent over   │
 │                              │ ─────> │   proxy follows snapshots   │
 │ SeatOwner=Guest              │ <───── │   local avatar in seat      │
 │   net input -> real car      │  INPUT │                              │
 └──────────────────────────────┘        └──────────────────────────────┘
        both render: avatar-Host + avatar-Guest, one in each seat
```

## The four decisions that carry the design

1. **Remote input is injected through InControl, not by patching vehicle code.**
   `GameInput.Instance.playerActions` is an InControl `PlayerActions` set. When the guest
   drives, the car host rebinds those controls to a virtual InControl device fed from the
   network. `PlayerCar` then reads input exactly as it does for a human — zero hooks in
   physics or vehicle code. The game's own bundled `VirtualDeviceExample` type proves the
   virtual-device infrastructure is present. Fallback if rebinding fights us: Harmony-patch
   the one method that reads the action.
2. **Transform snapshots, not input prediction.** Client-side prediction needs the physics
   law, which is stripped from the binary. Snapshots are boring and always correct.
3. **No second in-process player.** Each machine runs its own game as player 1, so cameras,
   HUD, audio listeners and input are untouched. The guest's own car is simply suppressed.
4. **TCP first, relay second.** `System.Net.Sockets` with a background thread and a
   main-thread queue. Works on LAN immediately and over the internet once a port is open.
   A ~100-line relay on an existing VPS can follow later so friends need no configuration.

## Project layout

```
<your-checkout>\
├── PROGRESS.md                     status, gotchas, do-not-repeat
├── docs\01-architecture.md         game internals from the metadata dump
├── docs\02-feasibility-and-plan.md subsystem verdicts
├── plans\mvp-passenger-coop.md     this file
├── analysis\                       dump + analysis output
├── tools\                          dump query scripts
└── src\
    ├── CloudpunkCoop.sln
    ├── CloudpunkCoop.Protocol\     net6.0, zero Unity deps — unit-testable
    │   ├── OpCode.cs
    │   ├── Message.cs
    │   ├── Serializer.cs
    │   └── ProtocolTests\
    ├── CloudpunkCoop.Plugin\        net6.0, BepInEx IL2CPP plugin
    │   ├── Plugin.cs
    │   ├── Session.cs               role + seat state machine
    │   ├── Transport\ITransport.cs
    │   ├── Transport\TcpTransport.cs
    │   ├── MainThreadQueue.cs
    │   ├── Net\CarSnapshot.cs
    │   ├── Net\InputFrame.cs
    │   ├── Net\Peer.cs
    │   ├── Game\GameRefs.cs         resolves Global/GameInput/Player/PlayerCar
    │   ├── Game\SeatRig.cs          seat anchors + avatar clones
    │   ├── Game\CarProxy.cs         kinematic proxy on the guest
    │   ├── Game\InputBridge.cs      InControl virtual device + rebinding
    │   ├── Game\RemoteCameraRig.cs  Cinemachine virtual camera for the proxy
    │   ├── Game\LocalCarSuppress.cs hide the guest's own car
    │   └── UI\DebugOverlay.cs
    └── CloudpunkCoop.Relay\         later, optional
```

The plugin targets `net6.0` because Il2CppInterop wrappers and the proven VR mod both run on
.NET 6 (the VR mod ships a whole `dotnet/` runtime). The protocol library targets plain
`net6.0` with no Unity references so it can be unit-tested outside the game.

---

## Steps

Ten steps, git commit each. `git init` in the project root before starting; no remote, so
direct commits rather than PRs. Every step is written to be executable from cold.

---

### Step 0 — BepInEx loads on this build
**Why:** everything depends on this. Unity 2018.4 / IL2CPP metadata 24.1 is the oldest
common case; if the loader does not attach, the project stops here and we pivot.

Tasks
1. Download BepInEx 6 IL2CPP x64 and extract into `C:\GOG Games\Cloudpunk` (game root, not
   `Cloudpunk_Data`): `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx\`.
2. Launch the game from the shortcut. Quit after the main menu.
3. Read `BepInEx\LogOutput.log`.

Verification
```powershell
Test-Path "C:\GOG Games\Cloudpunk\BepInEx\LogOutput.log"
Select-String -Path "C:\GOG Games\Cloudpunk\BepInEx\LogOutput.log" -Pattern "Chainloader|Interop|error|Exception"
```
Exit criteria — log contains a Chainloader/Il2CppInterop banner, no fatal exception, and the
game still reaches the menu. Interop assemblies are generated under
`BepInEx\Il2CppInterop\` or `BepInEx\interop\`.

Rollback — delete the four items above; the game is stock again.

---

### Step 1 — Plugin skeleton resolves the game's singletons
**Why:** proves we can call into the game's IL2CPP types at all.

Tasks
1. `git init`, first commit: existing `docs/`, `plans/`, `tools/`, `analysis/*.md`.
2. Create the solution and `CloudpunkCoop.Protocol` (empty).
3. Create `CloudpunkCoop.Plugin` with a `[BepInPlugin]` class whose only job is to log, on a
   scene-loaded coroutine: `UnityEngine.Application.unityVersion`, and resolve
   `Global.Instance`, `GameInput.Instance`, `Player.Instance`, `PlayerCar` — printing each
   type's full name plus a handful of field values.
4. Build against the generated interop assemblies.

Verification — launch the game, confirm the log prints the version and four non-null objects.
Exit criteria — `Global.Instance` resolves non-null and `playerCar` is non-null in a loaded
scene.

---

### Step 2 — Debug overlay and the input probe
**Why:** we must know the exact action list before we can rebind anything.
Runs in parallel with Step 3.

Tasks
1. `UI\DebugOverlay.cs` — an `OnGUI` IMGUI panel: toggle key, FPS, role, seat, peer state,
   snapshot rate. Cheapest possible UI in a shipped build.
2. Dump `GameInput.Instance.playerActions` — every `PlayerAction` with its name and current
   value, refreshed once a second.
3. Log which of those actions `PlayerCar` actually consumes, by Harmony-logging the
   `PlayerCar` methods that take no arguments and run every frame during driving.

Verification — open the overlay while driving; the log must show the car's input reads.
Exit criteria — a written list mapping car functions to actions (throttle, brake, steer,
altitude, boost, look). Store it in `docs/03-input-map.md`.

Rollback — the overlay is toggled off and the probe attribute can be removed.

---

### Step 3 — Seat anchors and a cloned avatar
**Why:** proves we can put a second character in the car, which is the visible half of the MVP.
Runs in parallel with Step 2.

Tasks
1. `Game\GameRefs.cs` — cached resolvers for `Global.Instance.player`, `.playerCar`,
   `.vehicleInterior`, `GameInput.Instance`.
2. Locate the driver's and passenger's seat transforms. Candidates: children of the car
   interior under `VehicleInterior.interiorLocal`; the VR mod already replaces
   `VehicleInterior.interiorCameraTransform`, so the rig is reachable. Log the transform
   hierarchy of the car once and pick the anchors by eye.
3. `Game\SeatRig.cs` — `CloneAvatar()`: duplicate the local player GameObject, strip
   interaction and scripts that would fight for input, disable its colliders, parent it to a
   seat transform. Bind `F1`/`F2` to sit in the driver's or passenger's seat.
4. Note: prevent Easy Save 2's auto-save from serialising the clone.

Verification — press `F2` in game; a copy of your character appears seated in your own car.
Exit criteria — clone is stable for 60 s with no console errors, and the save file does not
grow after a save.

---

### Step 4 — InControl virtual device drives the car
**Why:** the highest-risk unknown in the whole plan. If this fails the fallback is a Harmony
patch, but the fallback costs weeks.

Tasks
1. Create an InControl virtual device and add a `PlayerActionSet` bound to it.
2. Write synthetic values into it from code (hardcode: full throttle + full steer).
3. Rebind `GameInput.Instance.playerActions` so the car reads the virtual device.
4. Bind a key to rebind back to the physical gamepad.

Verification — unplug the gamepad (or ignore it) and drive with the values coming from code.
Exit criteria — the car responds identically whether input comes from hardware or from the
virtual device, with no gameplay hooks installed.

Rollback — rebind back to hardware on unload.

---

### Step 5 — Transport and protocol
**Why:** no network, no co-op. Deliberately free of game knowledge so it can proceed in
parallel and be unit-tested outside the game.
Runs in parallel with Steps 2–4.

Tasks
1. `CloudpunkCoop.Protocol` — opcodes `HELLO`, `WELCOME`, `ROSTER`, `CARSTATE`, `INPUT`,
   `SEAT`, `PING`, `PONG`, `BYE`. Length-prefixed binary framing; structs of primitives only.
2. Unit tests for framing and round-trip of every message.
3. `Transport\TcpTransport.cs` — listener on the host, connector on the guest, background
   thread, `MainThreadQueue` drained in an `Update`.
4. Log connect, disconnect and message counters.

Verification — `dotnet test` green; a standalone console client connects to a standalone
console server and exchanges all opcodes.
Exit criteria — transport proven outside the game before it is ever plugged in.

---

### Step 6 — The car appears on the other machine
**Why:** first visible co-op milestone.

Tasks
1. `Net\CarSnapshot.cs` — position, rotation, velocity, angular velocity, plus visual state
   (damage level, steer angle). Quantise to integers, delta-encode the rotation.
2. Host side: sample the real car at 20 Hz, send.
3. Guest side: `Game\CarProxy.cs` — clone the car visually, kinematic Rigidbody, interpolate
   against a 100 ms delay buffer, snap when the buffer underruns.
4. `Game\LocalCarSuppress.cs` — on the guest, freeze and hide the local car, and block its
   input so it cannot be driven.
5. `Game\RemoteCameraRig.cs` — a Cinemachine virtual camera targeting the proxy with priority
   above the existing one; enable it while the guest is remote. Do not create a second brain.
6. Carry the guest's avatar in the seat alongside.

Verification — guest launches, connects, and sees the host's car move smoothly at 20 Hz while
the host drives.
Exit criteria — both machines show the same car in the same place; measured snapshot rate
19–21 Hz; no rubber-banding in a 3-minute drive.

---

### Step 7 — The guest drives
**Why:** the actual point of the exercise.

Tasks
1. `Net\InputFrame.cs` — throttle, brake, steer, altitude, boost, plus a sequence number.
2. Guest side: sample local input at 30 Hz and send.
3. Host side: receive, write into the InControl virtual device from Step 4, with a small
   input-buffer so the values line up with the render tick.
4. Seat swap: host broadcasts `SEAT`; both sides apply it, rebinding input on the car host.

Verification — guest drives; the host sees the car move under someone else's hands.
Exit criteria — full steering, throttle and altitude control from the guest with no
desynchronisation on seat swap.

---

### Step 8 — Both avatars, both seats, both screens
**Why:** the "мы вдвоём в одной машине" picture the user described.

Tasks
1. Carry avatar state (which seat each peer occupies) in `ROSTER`/`SEAT`.
2. On both machines, place exactly two avatars, one per seat, built from Step 3's rig.
3. On the car host the remote avatar rides along; on the guest both are in the proxy.
4. Handle "get out" — a key that detaches a local avatar and leaves them standing by the car.

Verification — two players, two screens, one car, both characters visible in both seats.
Exit criteria — no phantom characters, no duplicated avatars after five seat swaps.

---

### Step 9 — Ship it
Tasks
1. Config file: port, nick, role, keybinds.
2. Reconnect and clean disconnect handling; never leave the player frozen.
3. Build a release zip: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx\`,
   `CloudpunkCoop.Plugin.dll`.
4. `README.md` — two machines, one LAN or a forwarded port, and an honest statement that
   this is a GOG/Steam build `50cce2edf27f` only.

Verification — a clean machine, following only the README, reaches two players in one car.
Exit criteria — friend does not need any instructions beyond the README.

---

## Dependency graph

```
Step 0 ──► Step 1 ──┬──► Step 2 ──┐
                    ├──► Step 3 ──┼──► Step 6 ──► Step 7 ──► Step 8 ──► Step 9
                    └──► Step 4 ──┘      ▲
                    └──► Step 5 ─────────┘
```

Steps 2, 3, 4 and 5 touch disjoint files and none depend on each other, so they can run as
four parallel lanes. Step 6 is the join point. Critical path: 0 → 1 → (2|3|4|5) → 6 → 7.

## Highest-risk items, in order

| Risk | Step | Why it matters | Fallback |
| --- | --- | --- | --- |
| Il2CppInterop on metadata 24.1 | 0 | project-blocking | the VR mod proves the stack loads, so this is verification, not hope |
| InControl virtual rebinding | 4 | determines the input architecture | Harmony-patch the car read point |
| Seat anchor discovery | 3 | visual MVP depends on it | hardcode offsets tuned by eye |
| Guest's own car suppression | 6 | ghost car could fight the proxy | hard park it at load, or hide the whole object |
| No port forwarding at the friend's end | 5 | connection fails for them | relay on an existing VPS |

## Explicitly out of scope for this MVP

Story sync, Holocash, quests, minimap markers, shared inventory, cutscenes, client-side
prediction, voice chat, more than two players, reconnect mid-session. All of these are in
`docs/02-feasibility-and-plan.md` with verdicts.
