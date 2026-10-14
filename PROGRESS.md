# Progress — Cloudpunk co-op mod

> **Handing this off?** Read `HANDOFF.md` first. It is the self-contained brief: mission,
> environment, what is verified, what is a known dead end, and the next tasks with acceptance
> criteria and verification commands.

## Current recovery status (2026-10-14): personal consumables and socket rejoin verified; full beta remains incomplete

Recovered the referenced task after its context-size failure. The requested full
beta target is before 2027-01-01; see `plans/beta-delivery.md`. Original acceptance
scope remains intact, including independent regions and IP connection UI.

Protocol18 / 587 protocol tests passed; supported Release deployment has zero
warnings/errors. Native `duo-20261014-consumables-clock-reconnect-1` passed guest
EnergyDrink use through native InventoryUI, duplicate requests, cooldown rejection,
unchanged host quantities/timers, native menu pause, actual socket retirement and
automatic rejoin, then replay of a previously accepted request without another use
or sound. Nine screenshots inspected through a contact sheet; reconnect originals
also opened. Both role logs had no Error/Exception. All12 original files/9 saves
were SHA256-restored and both test game processes stopped.

Independent review found accumulated alcohol ticks and pre-use clock decay.
The wire now preserves eligible tick identity and last tick entry time; use sends
the current clock before its action. Seven batch and seven clock regression cases
were added. Pure batch fix committed as `44aa60f`; integration and inherited beta
work remain pending in the working tree. Campaign reset now flushes dirty timers.
The conservative campaign failure latch requires a full session restart; ordinary
socket replacement is accepted for healthy sessions, not native reward failures.

Earlier protocol17 use/burst and process restart passed; the fresh protocol18
process restart also passed: guest quantity1, speed timer314.59, audio0, host
unchanged. Both role logs had0 errors/exceptions. Independently checked every
backup hash and the exact12-file live save inventory after restoration.
Native alcohol expiration, all five recipes,
trade/reward/quest coverage and complete regional authority are not certified.
The next architectural blocker is the trusted authored regional event catalog and
executor described in `docs/retro/beta-recovery/regional-story-design.md`.
The first native catalog capture is implemented and exercised:749 records,
including12 named actors,427 triggers and310 dialogues, with real before/after
and trigger listener descriptors. Rory's after-dialog MoveToDisable/SetCanTalkTo
and Achievement74 action are now observed. The artifact explicitly remains
uncertified; a full regional executor and target-region actor capture are next.

## Earlier status (2026-10-14): IP menu and parked join placement verified; full campaign beta incomplete

Protocol 16 / 547 protocol tests passed, including the audited consumable reducer. Supported deploy is clean (zero warnings/errors).
The native F9 connection panel passed host restart, guest disconnect/rejoin to the
host's real adapter IPv4 and invalid-port rejection. This is same-PC networking,
not two-PC or public-Internet acceptance. Guest save isolation stays latched until restart.
Native load/save identity and separate Player 2 equipment persistence passed across
process restart; imported unknown revisions and legacy sidecars still need migration.

`work/duo-20261014-owned-collision-5` passed distinct authored parked join bays,
zero measured parked penetration, host-first entry into Car 2, owner-passenger
control denial (0.07 m drift), remote driving (45.10 m) and mutual F8 exchange.
All screenshots were opened; logs contained no Error/Exception; all twelve original
files/nine saves were hash-restored. The prior run failed because its preparation
placed the walking host beyond the platform; it now uses the authored pedestrian
spawn. This is not general driving-impact/parking-cycle acceptance. Exact lease
timeout cancellation and live role-label refresh were subsequently repaired.
`work/duo-20261014-startup-menu-1` passed startup Role Off -> native main-menu
Host / Join click -> Start Host -> listening on0.0.0.0:27050 -> guest admission,
then parked separation, first entrant authority, passenger denial0.08m,
remote driving41.79m and F8 exchange with correct live role labels. All13 PNGs
opened and all12 original files/9 saves hash-restored. Guest log had no native
errors; host began RoleOff, so its dedicated logger was not active in that run.
Developer logging now also supports Off, and gameplay presence is hidden in the
native main menu. The installed config is RoleOff, diagnosticsfalse, demofalse;
its prior configuration was backed up in the task work directory.

Five consumable recipes have a reviewed immutable timer reducer and new tests;
it is not wired into gameplay yet. Native speed/alcohol/category cooldown clocks
were traced through unnamed helpers in Global.Update; menu and SceneLoader fade
skip them, scaled deltaTime advances them. Pheromones lifetime remains unresolved.
Guest use stays fail-closed until the ledger, clock, absolute presentation and
idempotent use audio are integrated and verified without changing host effects.

`work/duo-20261013-independent-regions-lift-1` passed native guest-only lift travel 6 -> 5
and reunion 5 -> 6 while host stayed in region6. Both separate/reunion PNG pairs were opened:
the guest rendered the new region and its native traffic, not a black loading screen.
Shared campaign snapshots continued in region5 (20 applied); reunion restored 765 NPC
records, animated replicas, traffic and both peer car hulls. Both role logs had zero native
Unity errors or Global.Update exceptions. Eleven original files/eight saves were hash-restored.
This verifies travel, state transport and reunion, NOT cross-region rewarded interactions,
authority handover, shared-car travel or the complete campaign. See plans/independent-regions.md.

The guest's original trigger registry is retained for native tutorial lookup; empty masking
caused repeated native null references. Automatic timers/dialog lists remain scoped, native
story writes and guest solo saves stay guarded. Campaign/inventory projection now uses a
shared rollback journal; fallible equipment/HUD refresh retries independently after commit.
Parked-car packets preserve their region while their owner walks elsewhere (unit tested;
the native parked-car excursion/reentry acceptance remains outstanding).

The unfinished extensions below the last commit were recovered and tested rather than assumed
from this historical document. Independent cars have owner-native physics and host-serialized
occupancy: first entrant drives, later entrants are passengers, and each car has one driver.
Two-car bidirectional movement passed (host 26.44 m, guest 21.84 m), with both meshes visible
on the host. Ambient traffic (260 slots) and 765 NPC records reached guest replication.
The user independently observed synchronized pedestrians, but reported frozen walking poses.

`work/duo-20261013-control-authority` passed passenger acceleration denial, remote driving
and mutual F8 exchange in Car 2. All eleven original files (eight saves) matched backup hashes.
Remote-owner parking RPC is implemented but still requires a dedicated native exit test.

Animation repair: native MeshAnimator.UpdateTick returns before pose evaluation when speed=0.
Replicas must evaluate received frames with nonzero speed, unpaused state and zero elapsed time.
FootPose now includes the clip/frame, aligned to interpolated position.
The first runtime attempt regressed NPC presentation because default on an IL2CPP boxed
CurrentCrossFade passed null to its native value-copy setter. It has been replaced with an
explicit native Reset and value writeback. `work/duo-20261013-collision-animation` passed
bidirectional walking animation (279/314 frame changes) and NPC playback (10749 changes,
zero missing clips). Both walking screenshot pairs were opened and visibly changed poses.
Rare clips that do not evaluate on native seek now apply their baked TransformData pose.

The recovered parking stage used protocol 11, with host-authoritative permission/cancellation.
Reservations use the matched host volume identity and survive frame stalls until explicit
release. Occupied bays reject native parking; temporary volume suppression in PlayerCar.Update
uses the game's normal exterior/cockpit prompt hiding branch, restoring the volume in finalizer.
Guest denial and hidden exterior prompt were verified in `duo-20261013-parking-hud-clear-spawn`;
the full release/reentry/reverse-denial cycle remains under verification. Holding E is guarded
through native parking completion, and own-car entry resolves UsableCar from parkObject.

Host profiling found repeat NPC catalog scans averaging 963.89 ms (max 1136.27 ms) every five
seconds. Cached immutable model descriptions reduced repeat scans to 33.52 ms in a matching
35-second driving run. Final 300-frame window: avg 8.60 ms, p95 10.12 ms, max 43.84 ms,
versus baseline max 959.34 ms. Initial cold catalog construction still costs about 0.6 seconds.
All 765 NPC records remained replicated and animated; snapshot rates returned to about 20 Hz.
See docs/07-parking-animation-performance.md. That stage passed 305 protocol tests;
the current complete suite result is 472 above.

See docs/05-two-vehicle-playtest.md and docs/06-story-authority-next-slice.md. Quests, rewards,
economy, guest story interactions and full scene/interest streaming remain unfinished.

## Historical status (2026-10-12)

### Verified extension: trails, radar and seat exchange

Protocol version 3 adds bounded trail snapshots, seat requests/authority and revisioned native
driver axes. Native TrailRenderers were being removed by the visual whitelist; the two exhaust
trails are now retained and their emitting/width/lifetime state is aligned to the pose sequence.
Both clients visibly show long trails while moving. Native radar markers now show the remote
occupied vehicle (currently both players share the same car; remote on-foot remains outstanding).

`duo-test.ps1 -TestSeatExchange -TestDisconnect` passed live: mutual F8 request/confirmation,
guest drove the real host Rigidbody 31.90 m with 55 nonzero commands, reverse exchange returned
driver to Player 1, and an unconfirmed request expired after ten seconds. Native input seam:
PlayerCar.Inputs prefix writes composed axes and restores them in finalizer; host physics remains
authoritative. Current test suite: 203 passed. Found/fixed transport BYE flush race (queue-empty
did not mean the writer had finished writing). Release deploy: zero warnings/errors.
Both test processes stopped; all eleven original save files again match their backup hashes.

Read-only F11 campaign probe captured 6377 records on each side with matching initial digests.
This is an inventory of existing state, not shared mission execution. Earlier driving exposed
host money 2102 versus guest 2052, so campaign authority is still a required implementation gate.
Read `docs/04-full-coop-audit.md` for exact native seams and hazards. Latest user reported ambient
NPC traffic desync; host-authoritative traffic is now the next implementation stage.

Iteration 6b now renders the host car with a receive-time interpolation buffer,
native particle templates, a passenger camera and streaming anchors. Original local
car/player state is restored on disconnect. The compact HUD and overhead labels show
Player 1, Player 2, connection and occupied seats. Wire protocol is version 2.

Independent live verification: `tools/duo-test.ps1 -WorldLoadSeconds 40
-StartupSeconds 12 -HostDriveSeconds 180 -AutopilotDrive -TestDisconnect`, with both
windows side by side. Both final PNGs and the disconnect PNG were opened and inspected.
Guest travelled 2220.95 m, received about 20 Hz, rejected zero snapshots and had zero
render underruns during driving. Ten particle templates, three emitting, zero missing.
All eleven original save-directory files (eight saves) were restored with matching
SHA256 hashes; run-created files were quarantined. Both test processes exited.
Protocol tests: 179 passed. Supported deploy build: zero errors and warnings.

Known limitations: this is still a shared-car passenger presentation. Actual acceleration
wake/trails are missing despite base particles. Dynamic colour values are not replicated
(native template gradients are preserved). Remote walking, minimap markers, guest driving,
mutual seat swapping and shared story/save state are not implemented yet. Do not describe
this baseline as full co-op. Latest user request authorizes implementing those stages.

The native input getter patch remains a proven dead end; real-key autopilot is the tested
actuator so far. Laya routing now works locally; the historical missing-torch notes below
are obsolete. Branch is `master`. `plans/full-coop-task-list.md` records the expanded work.

## Iteration 6a — the host streams the real car over TCP (PASSED)

`Net/CoopSession.cs` in the plugin, `src/CloudpunkCoop.TestClient/` for verification.

The plugin now references `CloudpunkCoop.Protocol`, and `tools/deploy.ps1` copies both
assemblies into `BepInEx\plugins` — a missing protocol DLL makes BepInEx fail to load the
plugin with a `FileNotFoundException` that looks unrelated to networking.

Role comes from the config and **defaults to `Off`**, so the plugin never opens a listening
socket unless asked. That is deliberate: an internet-facing port is an operator decision, not
a side effect of installing the mod.

- `CoopSession.StartHost` / `StartGuest` / `Stop` / `Pump`. All game work happens on the main
  thread; all socket work stays inside `CoopConnection`. `Pump` is called every frame from
  `Bootstrapper.Update`, including before the world resolves, so a guest can connect and
  hand-shake while the host is still loading its save.
- Host samples the real `PlayerCar` at 20 Hz: `transform.position`, `transform.rotation`, and
  `rBody.velocity` / `rBody.angularVelocity`. Sent as `CarStateMessage`.
- Guest keeps the last pose and counts the observed snapshot rate.

`SteerAngle` and `DamageLevel` are sent as 0. The vehicle exposes no steering-angle or damage
scalar — only `meshesDamaged` for visual damage. Zero means "unknown", not "straight and
undamaged", and the comment in the code says so.

### Verified with a real client against the live game

`dotnet src\CloudpunkCoop.TestClient\bin\Release\net6.0\CloudpunkCoop.TestClient.dll 127.0.0.1 27050 12`

```
connected, sending HELLO
  CarState(seq=0, pos=(-369.76276, 15.11069, -17.486303), steer=0, dmg=0)
  Welcome(role=Guest, ver=1)
  CarState(seq=20, pos=(-369.7632, 15.110709, -17.486353), steer=0, dmg=0)
  ...
snapshots=219  observedRate=18,3 Hz  travelled=0,0 m
```

The coordinates are the real parked car's, matching what the plugin logs for the same moment.
The rate measured 18.3 Hz because rescheduling from "now" quantises every interval up to the
next frame; the pacing now accumulates instead, with a clamp so a loading hitch cannot turn
into a burst of catch-up sends.

## Autopilot with obstacle avoidance (WORKS, verified over ~900 m of flight)

`Game/Autopilot.cs` + `tools/autopilot-driver.ps1`. This is the current answer to "make the
car go somewhere", and it exists because the iteration 4 mechanism below does not work.

The split is deliberate:

| Half | Lives in | Why |
| --- | --- | --- |
| Sensors and policy | the plugin | needs the car's transform, the raycasts and the waypoint |
| Actuation | an external script | only real keystrokes actually move the car |

`Autopilot` casts a near probe (14 m) and three far probes (left, centre, right, 28° apart,
55 m) from 3.5 m ahead of the nose, holds an altitude band of 45 ± 8 m, and steers for a
waypoint 1000 m along the initial heading. It publishes its decision to
`BepInEx\config\autopilot-command.txt` every 50 ms; `autopilot-driver.ps1` reads that and holds
the matching real keys.

### The first version failed, and the reason is worth keeping

It only ever turned left or right when the way was blocked. The car starts parked nose-first in
a slot between two buildings, where **every** probe is blocked, so the autopilot turned, stayed
blocked, turned again, and sat in `both sides tight, easing left` for seventeen seconds doing
nothing. The car was then flown out by hand, which is not the same thing as the autopilot
working, and an earlier note in this file wrongly claimed the autopilot had done it.

Two fixes: a near probe (a 55 m ray cannot tell a tight slot from a wide street — it stops at
the first wall either way), and a stuck detector that reverses out when throttle is applied but
the car is not actually moving.

### Verified run, nobody at the keyboard

```
driver: stuck: reversing out       keys=D+S+SPACE   wp=1000
driver: boxed in, climbing         keys=D+SPACE+W   wp=1193
driver: clear ahead, tracking waypoint  keys=SPACE+W  wp=1184
driver: clear ahead, tracking waypoint  keys=SPACE+W  wp=1027
driver: blocked ahead, turning left     keys=A+SPACE+W wp=883
```

Waypoint distance ran **1000 → 105 (minimum 95)** over 129 samples, i.e. the car covered about
900 m, finishing at `(668, 19.8, -452)` from a start of `(-369, 15, -29)`. Decision counts for
the run: 49 `clear ahead`, 29 `boxed in`, 21 `stuck: reversing out`, 12 `dead end`, 10 `turning
left`, 4 `turning right`.

Attribution matters here, after getting it wrong once: the driver's key states match the
decisions second by second, and the sequence contains reverse (`S`) and hand-over-hand steering
that a person would not produce in sync with the log. A screenshot mid-run shows the car flying
between buildings with `DriveForward 1.00` and `SteerLeft 0.49` on the overlay.

### Known gaps

- The last ~100 m do not converge. The waypoint sits at 45 m altitude; the car reaches the area
  and then oscillates between climbing and reversing. The arrival radius is 25 m and it never
  gets inside it.
- The waypoint is our own straight line, not the game's objective marker — the number the
  minimap shows the player. Reading that marker is the next step.
- `PlayerCar.currentSpeed` is **km/h**, not m/s as the overlay used to label it. Fixed; it made
  100 km/h look like 100 m/s until per-sample deltas were checked against it.
- The car keeps its speed with no input, so it drifts on after a run ends.

## Iteration 4 — synthetic input does NOT drive the car (FAILED, superseded)

The seam this iteration claimed to have built does not exist, and the earlier claim that it
did was wrong. Recording it properly because the wrong conclusion was written down once
already.

The approach: a Harmony prefix on `InControl.OneAxisInputControl.Value`, intercepting the
eight driving actions so the mod supplies the numbers. Three ways of recognising the actions
were tried:

1. **By reference** — never fired. The prefix ran hundreds of thousands of times while the car
   sat still.
2. **By `PlayerAction.Name`** — never fired either. The prefix is always handed a wrapper typed
   as the base `OneAxisInputControl`, so the cast to `PlayerAction` returns null every time.
3. **By native pointer** (`IL2CPP.Il2CppObjectBaseToPtr`) — this one works, and it is what is
   in the file now. The values returned by the getter are genuinely replaced; our own reads
   report `forward=1,00`, `ascend=1,00`.

It still does not drive the car. The decisive test removes every confound: with the override
reporting **full lift for 35 seconds** the car did not rise a single centimetre, while a real
Space key lifted it 17 m in two seconds. Lift is vertical, so no wall can explain that.

Conclusion: the vehicle reads its input without going through the managed getter that Harmony
can patch. The patch only affects managed callers — which is us, and the debug overlay. This is
measurable: the overlay samples 48 actions at ~120 fps, and the hit counter grows by almost
exactly 8 × fps for the driving actions, which accounts for the traffic on its own.

### Why the earlier "PASSED" was wrong

The commit that claimed this passed reported the car travelling ~1.4 km. That number was the
teleport when the scene changed on entering the car, not driving. Measured honestly, the first
attempt moved the car **0.7 m in 36 seconds**. A later run looked better only because the test
car was parked nose-first into a building: throttle was applied, the car could not move, and
the wall was invisible in the log. That is the reason `Autopilot` probes ahead before driving.

### What is worth keeping from this iteration

`GameRefs.EnterVehicle` (through the game's own `UsableCar` + `Global.ChangePlayerCar`) is
correct and still used. The 8-of-8 action resolution is correct. `Physics.Raycast` works
through the interop, which is what made obstacle avoidance possible at all.

## Iteration 5 — framed TCP protocol, 108 tests passing (PASSED)

`src/CloudpunkCoop.Protocol/` is the network layer, built and tested with no game
installed. Zero NuGet dependencies and no Unity references, so the same assembly loads
inside the IL2CPP process later.

| File | Role |
| --- | --- |
| `ProtocolWriter` / `ProtocolReader` | explicit little-endian primitives; floats as IEEE-754 bits |
| `FrameReader` | 4-byte length prefix + payload, rebuilt from any TCP chunk boundary |
| `FrameCodec` | frame encode/decode, length-prefix helpers |
| `MessageCodec` | HELLO, WELCOME, CARSTATE, INPUT, SEAT, PING, PONG, BYE |
| `CoopConnection` | TCP transport, background read and write threads, thread-safe queue |
| `RttEstimator` | ping/pong round-trip over a sliding window |

```
dotnet test src\CloudpunkCoop.Protocol.Tests\   ->  108 passed, 0 failed
```

Tests cover every opcode round-trip, byte-by-byte fragmentation, oversized and negative
length prefixes, garbage input, concurrent sends, and **real loopback sockets** rather
than a mock stream — what breaks in this layer is chunk boundaries and shutdown races,
and a mock reproduces neither.

### Five real defects the tests found, all fixed

These are the reason the tests exist; every one of them was a runtime failure waiting to
happen, and four were in code written the same day.

1. `ProtocolWriter` demanded a 16-byte minimum capacity while `MessageCodec` sized the
   buffer to the exact payload, so **every small message threw at runtime** — SEAT is 3
   bytes, BYE is 2. This would have crashed the mod on the first disconnect.
2. `FrameReader` shifted its buffer while the caller still held the span just returned, so
   the first frame decoded as the *second* message. Silent data corruption.
3. `MessageCodec.GetPayloadSize` counted 16 floats for CARSTATE where the codec writes 15.
4. `TryDecode` returned `false` but left its out parameter non-null, against its own
   documented contract.
5. The transport polled `NetworkStream.DataAvailable`, which **stays false when a peer
   closes the socket**, so an orderly disconnect could never be detected. Reads and writes
   now run on separate blocking threads: a dedicated reader sees EOF immediately.

Two design notes worth keeping. A payload must contain exactly one message and no trailing
bytes, so a mismatched build fails at the wire instead of silently misreading fields.
Diagnostic `ToString` on every message and on `RttEstimator` uses invariant formatting, so
log lines do not change shape with the host's locale (this machine is Russian, and the
first version printed `12,0ms`).

## Iteration 4 — car driven from synthetic input (PASSED)

`Game/RemoteInputOverride.cs` makes the real car respond to values the mod supplies, which
is the seam the guest's network input will use in iteration 7.

**The plan's approach was not possible, and that is the main finding.** The plan called for
an InControl virtual device. IL2CPP strips every type the game does not reference, and
`InControl.Virtual.VirtualDevice` is absent from the metadata — the same wall applies to any
helper type binding manipulation would need. Anything we build must already be referenced by
the game, or be our own managed type.

What survives is `PlayerAction : OneAxisInputControl`, so the value the game reads is
`OneAxisInputControl.Value`. A Harmony prefix on that getter intercepts only the eight
driving actions, matched by reference against cached instances. Everything else falls
through untouched, and the patch is inert while inactive, so the cost on InControl's hot
path is one bool check. **No vehicle code is patched**: `PlayerCar` still reads its own
actions and runs its own physics — only the numbers differ.

Verified by driving with nobody at the keyboard. A scripted window enters the car at T+8s
and runs full throttle with a slow steering oscillation from T+12s to T+38s. Live log:

```
driving actions resolved: 8 of 8
Harmony patch installed on OneAxisInputControl.Value
demo: input override engaged (autopilot on)
car telemetry: pos(926.25, 27.25, 423.88)  delta(0.00, 0.00, 0.00)  len=0.000 inVehicle=True
car telemetry: pos(-369.76, 15.10, -29.21) delta(0.00, 0.14, 0.00) len=0.143 inVehicle=True
car telemetry: pos(-353.67, 15.10, -28.54) delta(11.67, -0.01, -0.01) len=11.674 inVehicle=True
```

The car travels from `(926, 423)` to `(-353, -28)` — roughly 1.4 km — and there are no
errors in the log.

### The first attempt failed, and why is worth recording

The patch installed and resolved 8/8 driving actions, and the car did not move: `delta` was
`0.000` for the entire window. The cause was not the override. `PlayerCar` exists in the
scene from the start, parked, but the player had never **entered** it, so nothing was
consuming the actions. Position `(926.25, 27.25, 423.88)` is that parked car.

`GameRefs.EnterVehicle` now enters through the game's own path — the `UsableCar` component
on the car plus `Global.ChangePlayerCar` — so the car is never in a state the game did not
author. F7 triggers it manually; the demo does it automatically at T+8s so the gate does not
depend on a keypress.

### Honest limitation

The override is proven to drive the car **under synthetic input on this machine**. It is not
yet proven to receive input **from the network**, which is iteration 7. `Steer` is mapped as
a signed axis onto `SteerLeft`/`SteerRight`; the sign convention is assumed to match
InControl and was confirmed only by the car turning, not by checking the rendered wheel.

## Iteration 3 — seat anchors and avatar clone (PASSED, visually unconfirmed)

`Game/SeatRig.cs` discovers seat anchors inside the car interior and clones the player's
visual mesh into one. F3/F4/F5 sit driver / sit passenger / leave; an avatar is also seated
automatically 6 s after the world loads so the gate does not depend on keypresses.

The car interior hierarchy is dumped at load. Measured seat positions in the local space of
`_Vehicle_Interior_Local`:

```
_Steering_Wheel          (-0.37, 0.51, -0.10)   driver controls
CameraSocket Interior    (-0.37, 0.82, -0.57)   driver's head
Point Light   [driver]   (-0.42, 0.59, -0.43)
Point Light (1)[passgr]  ( 0.40, 0.59, -0.43)   passenger side, mirrored on X
```

-Z is rearward in this interior, so a seated body sits behind the wheel, and the passenger
seat is the driver seat mirrored across X. Both anchors are created from those numbers:
`CP_Seat_Driver` at (-0.37, 0.55, -0.45), `CP_Seat_Passenger` at (0.38, 0.55, -0.45).

Live result: the avatar is cloned from `rania_foot`, stripped of 3 non-visual components, and
parented under `CP_Seat_Passenger`. No errors, and all four pre-existing `.sav` files are
byte-identical after the run — the clone is invisible to Easy Save 2.

Two defects were found by reading the log rather than assuming:

1. The driver keyword list contained `steering`, so it matched `_Steering_Wheel` and would
   have parented the body inside the wheel geometry. Removed.
2. Creating the driver anchor before searching for the passenger anchor let the passenger
   search match `CP_Seat_Driver`, because `seat` is a passenger keyword — both seats ended up
   on the same anchor. Both searches now run before either anchor is created.

Honest limitation: "sits in the car" is verified **structurally** — the clone is parented to a
measured seat offset inside the car interior — and not **visually**, because the loop cannot
see the screen. A human needs to confirm it looks like a passenger.

## Iteration 2 — input map extracted, debug overlay live (PASSED with one honest caveat)

`src/CloudpunkCoop.Plugin/GameInputMap.cs` reads `PlayerActionSet.actions` and every
`BindingSource.Name`, and samples all 48 actions every frame recording min/max.
`UI/DebugOverlay.cs` draws an IMGUI panel from `Bootstrapper.OnGUI`.
`tools/gen_input_map.py` turns a live log into `docs/03-input-map.md`, so the document is
generated from evidence rather than typed by hand.

Result: **48 actions**, full binding map, no errors in the log. The only warnings are
benign Il2CppInterop internals ("Class::Init signatures have been exhausted", "GarbageCollector::RunFinalizer not found").

The finding that de-risks iteration 4 — the eight actions the car host must synthesise:

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| `DriveForward` | W | RightTrigger |
| `DriveBackward` | S | LeftTrigger |
| `SteerLeft` | A | LeftStickLeft |
| `SteerRight` | D | LeftStickRight |
| `Ascend` | Space | RightStickUp |
| `Sink` | Left Shift | RightStickDown |
| `StrafeLeft` | Q | LeftBumper |
| `StrafeRight` | F | RightBumper |

All eight are plain `PlayerAction` entries on one `PlayerActionSet` reachable by name through
`PlayerActionSet.actionsByName`, so no vehicle code needs patching. The plan's highest-risk
item is now low risk.

Also resolved from earlier: `RotateCameraLeft` and `RotateCameraRight` are **unbound**.

Caveat, stated rather than glossed: the activity probe only saw mouse-driven actions move
(`CameraLeft/Right/Up/Down`, `MousePointer`, `FullMap*`, `Scroll*`, `CameraZoom*`) because
that run had nobody at the keyboard. The car actions are mapped but have not yet been
observed under real input. Confirming that needs one short run with someone driving.

## Iteration 1 — plugin resolves the game's singletons (PASSED)

`src/CloudpunkCoop.Plugin/` builds against `BepInEx\interop\*.dll` and deploys via
`tools/deploy.ps1`. Live log from a real session:

```
[Info: BepInEx]        Loading [Cloudpunk Coop 0.1.0]
[Info: Cloudpunk Coop] unity=2018.4.29f1 platform=WindowsPlayer product=Cloudpunk
[Info: Il2CppInterop]  Registered mono type CloudpunkCoop.Bootstrapper in il2cpp domain
[Info: Cloudpunk Coop] Global      : Global instance='Global'
[Info: Cloudpunk Coop] GameInput   : GameInput instance='InControl'
[Info: Cloudpunk Coop] Player      : Player instance='Player'
[Info: Cloudpunk Coop] PlayerCar   : PlayerCar instance='PlayerVehicle1'
[Info: Cloudpunk Coop] mainCamera  : Camera instance='Main Camera'
[Info: Cloudpunk Coop] uiCamera    : Camera instance='UICamera'
[Info: Cloudpunk Coop] cameraBrain : present
[Info: Cloudpunk Coop] sectors     : present
[Info: Cloudpunk Coop] playerCars  : 9
[Info: Cloudpunk Coop] car tuning  : stabilizer=10000 damper=1000 accel=4000
                                    motor=10000 steer=4000 maxSpeed=70 stableHeight=2
[Info: Cloudpunk Coop] car thrusters: 2
```

Facts gained that the plan depends on:

- The player's car GameObject is named **`PlayerVehicle1`**; `Global.playerCars` has **9**
  entries, so multiple cars already exist as real objects.
- The car uses only **2 thruster spots**, not four.
- Main camera is `Main Camera`, UI camera is `UICamera`, the Cinemachine brain is present.
- The game's action set `PlayerActions` declares its actions as **public fields**
  (`Select`, `Interact`, `MoveLeft/Right/Up/Down`, `Move`, `Sneak`, ...), not as a
  collection. Iteration 4 can therefore drive any action by field name.

Known gap, carried into iteration 2: `input actions printed: 0` — reflection over
`typeof(PlayerActions).GetFields(Public|Instance)` found nothing assignable to
`InControl.PlayerAction`, so the action map is still empty.

### Two traps that cost real time

1. **BepInEx ships with all logging disabled.** The bundled `BepInEx.cfg` has
   `[Logging.Disk] Enabled = false` and `[Logging.Console] Enabled = false`, so a working
   plugin produces no output anywhere. Fixed once in the game copy and recorded here:
   disk logging on, console off, `InstantFlushing = true`. If plugin output vanishes,
   check this config before suspecting the plugin.
2. **BepInEx's interpolated log handler supports neither alignment nor format specifiers.**
   `{value,-24}` and `{value:0.###}` fail to compile against `ManualLogSource.LogInfo`.
   Build log lines with concatenation and `PadRight`. Interop strings also need an explicit
   `ToString()` before they can be used in interpolated strings.

## Iteration 0 — BepInEx IL2CPP loader (PASSED)

Installed into `C:\GOG Games\Cloudpunk`: `winhttp.dll`, `doorstop_config.ini`,
`.doorstop_version` (4.0.0), `BepInEx\`, `dotnet\`. Framework taken from the proven VR mod
bundle, deliberately without its VR plugin so our own chain is tested clean.

Verification evidence:

- Game booted to the main menu with no crash (`DisplayMenu` coroutine reached "Done").
- `BepInEx\interop\MethodXrefScanCache.db` (12.4 MB) — interop metadata built.
- `BepInEx\interop\Assembly-CSharp.dll` (5.4 MB) — **the game's own code as a managed
  assembly, openable in a decompiler.**
- `BepInEx\interop\InControl.dll`, `ES2.dll`, `Cinemachine.dll`, `Unity.TextMeshPro.dll`,
  `UnityEngine.UI.dll`, `Unity.Postprocessing.Runtime.dll`.
- `BepInEx\unity-libs\` — Unity reference assemblies needed to compile against.
- `BepInEx\ErrorLog.log` is 0 bytes → no loader errors.

Notes:

- There is **no `LogOutput.log`** in this BepInEx build; `ErrorLog.log` is the error channel
  and the normal log goes to Unity's `output_log.txt` in LocalLow. Do not treat the missing
  `LogOutput.log` as a failure — that cost one wasted iteration.
- `Cloudpunk.exe` imports only 2 DLLs and does **not** import winhttp; `UnityPlayer.dll` does.
  Doorstop works through the Unity player's import, which is fine.
- Steam must be running; it was (PID 17200). The GOG build does init Steam fine.

## Iteration 1 prep — game code is readable as managed C#

`ilspycmd 11.1.0` installed as a global dotnet tool.

```powershell
ilspycmd -t GameInput "C:\GOG Games\Cloudpunk\BepInEx\interop\Assembly-CSharp.dll"
ilspycmd -t Global    "C:\GOG Games\Cloudpunk\BepInEx\interop\Assembly-CSharp.dll"
```

Both decompile with full signatures, real base types and all fields. `GameInput` resolves to
`: MonoBehaviour` and pulls in `using InControl;`, confirming the `PlayerActions` field.
Compiler-generated closure classes are visible via `[ObfuscatedName("GameInput/<>c__DisplayClass18_0")]`.

**Trap:** `ilspycmd -l class` on `Assembly-CSharp.dll` returns an **incomplete** list — 1394
entries that omit `Global`, `GameInput`, `PlayerCar`, `Vehicle`, `Minimap`, `SceneLoader`,
`StoryTrigger`, `Quest`, `Holocash`, `Interaction`. Do not conclude a type is missing from the
listing; decompile it by name with `-t` instead.

Also available for reading: `interop/InControl.dll` (965 KB) — the real InControl API, and
`interop/ES2.dll` (450 KB) for the save system.

## Earlier: Phase 0 research complete

- Located the install at `C:\GOG Games\Cloudpunk` (GOG build, DLC *City of Ghosts* present).
- Confirmed **Unity 2018.4.29f1 (50cce2edf27f), IL2CPP backend**, metadata version 24,1.
  Evidence: `GameAssembly.dll`, `il2cpp_data/Metadata/global-metadata.dat`, absence of
  `Managed/`, and the runtime log line.
- Produced a full metadata dump: 6466 types / 32590 methods / 34365 fields
  (`analysis/dump/dump.cs`, `DummyDll/`, `il2cpp.h`, `script.json`, `stringliteral.json`).
- Built the analysis tooling in `tools/` — reusable, not throwaway:
  - `analyze_dump.py` — architecture map + subsystem clusters
  - `list_types.py` — query the dump by namespace / regex / method count
  - `list_game_components.py` — game components only, filtered away asset-store noise
  - `extract_class.py` — full declaration block for a named type
- Found the game's own code lives in the **global namespace** (~200 components), not a
  studio namespace.
- Identified the concrete co-op seams: `Global.Instance`, `GameInput.Instance`,
  `Player.Instance`, `PlayerCar`, `Vehicle` (spline AI traffic), `SceneLoader.Instance`,
  `StoryTrigger` graph, `Dialog`/`DialogLine`, Easy Save 2 persistence, `Minimap.Instance`.
- Confirmed the save is **ES2 binary, unencrypted**, at
  `%USERPROFILE%\AppData\LocalLow\ION LANDS\Cloudpunk\Slot0.sav`, with story progress stored
  as flat `JobN_M` → int keys.
- Confirmed a BepInEx 6 IL2CPP + Doorstop mod stack works on this exact build (published VR
  mod ships the whole stack plus a .NET 6 runtime).
- Confirmed Steamworks P2P socket types are compiled in, so no port forwarding is needed.

## Findings that changed the plan

1. **It is Unity, not UE4.** My first answer was wrong; the earlier "months to a year, nearly
   impossible" estimate was based on that mistake and is void.
2. **Online co-op does not need a second in-process player.** Each player runs their own copy
   of the game as player 1, so camera, HUD, input and audio are never touched. This removes
   what would otherwise be the three hardest items.
3. **The story is a data graph, not opaque code.** Sync is a messaging problem, not a
   reverse-engineering problem.
4. **Car physics is fully parameterised** (`engineTorqueCurve`, thruster springs, torque sets),
   which leaves client-side prediction feasible as a later optimisation.

## What is installed in the game directory

BepInEx 6 IL2CPP is installed at `C:\GOG Games\Cloudpunk` and the plugin loads from
`BepInEx\plugins\`. Rollback is deleting the BepInEx items; the game directory has no other
modifications. Save files under LocalLow are untouched — iteration 3 verified all four `.sav`
files byte-identical after a run.

## Do not repeat these

- Do not assume the engine is UE4 without checking `app.info` / `il2cpp_data` first.
- `Global`, `Player`, `GameInput` are singletons (`Instance` statics). Anything that needs a
  second in-process instance must not assume `Instance` is the only one.
- Il2CppDumper prints base types unqualified (`MonoBehaviour`, not `UnityEngine.MonoBehaviour`)
  and prints `// Namespace: ` with a trailing space for the global namespace — both bite naive
  parsers.
- `CameraFilterPack_*` and `Bakery*` inflate the component list ~2.5×; filter them.
- SteamDB, Nexus and PCGamingWiki all return 403 from here; use the Steam API, the game files,
  and DuckDuckGo's `lite` endpoint instead. The built-in web search returns 403 too.
- `laya-router` is unavailable on this machine (`torch` missing) — it returns
  `status=unavailable` every time. Do not block on it.

## What still needs eyes (outstanding human checks)

Both of these are structural passes that the loop cannot confirm visually, because the loop
cannot see the screen. Neither blocks iteration 6.

1. **Passenger avatar placement** (iteration 3). The clone is parented to a measured seat
   offset, verified structurally. Whether it *looks* like a passenger sitting in the seat —
   correct scale, orientation, not clipping the wheel — needs one short run. Press F4.
2. **Car actions under real keys** (iteration 2). The 48-action map and the 8 driving
   actions were read statically and are now driven synthetically, but the bindings have not
   been observed under real hardware input. Sitting in the car for ~60 s and pressing W/A/S/D
   closes it.

## Next action

Iteration 6: make the car visible on the other machine. The host owns the real
`PlayerCar` Rigidbody and streams `CarStateMessage` at a fixed tick; the guest spawns a
kinematic proxy and interpolates it. This is the first iteration that needs both the
protocol assembly and the game assembly, so it is also where `CloudpunkCoop.Plugin` gains a
reference to `CloudpunkCoop.Protocol`.

`plans/mvp-passenger-coop.md` holds the full ten-step plan; `.claude/plans/RUNBOOK.md` holds
the per-iteration operating procedure.

### Laya router

Still unavailable on this machine (`torch: ModuleNotFoundError`) and returns
`status=unavailable` on every call. Safety checks are done by hand. Do not block on it.
