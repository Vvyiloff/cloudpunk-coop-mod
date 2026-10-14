# Parking, animation and host hitching — 2026-10-13

## Verified evidence

- `work/duo-20261013-control-authority`: passenger acceleration denied, non-owner driver
  movement and mutual F8 driver exchange passed in Car 2.
- `work/duo-20261013-collision-animation`: 765 NPC records, 10749 guest frame changes,
  279/314 peer-avatar frame changes, no missing clips. Walking screenshot pairs inspected.
- `work/duo-20261013-parking-hud-clear-spawn`: host parked first, guest parking denied with
  `Parking space occupied`; `parkingPromptHidden=True`. Guest PNG inspected: no native E
  landing caption. Reentry exposed missing parked-object usable lookup; corrected subsequently.
- `work/duo-20261013-perf-baseline`: repeat host NPC scan avg 963.89 ms, max 1136.27 ms;
  final 300-frame window max 959.34 ms. Snapshot delivery dipped during these main-thread stalls.
- `work/duo-20261013-perf-cached`: repeat host scan 33.52 ms; final frame avg/p95/max
  8.60/10.12/43.84 ms. Both roles retained 765 NPC records and roughly 20 Hz car snapshots.
- Each finished run closed its own processes and restored all 11 original files (8 saves),
  checked by SHA256. Interrupted/failing tests are failures, not acceptance.

## Implementation

Wire protocol 11 requires matching plugin/protocol deployment on both installations.
The host matches a request to its native ParkVolume and keys reservations from that matched
volume. A granted lease is held until explicit owner cancellation/departure or disconnect;
it cannot expire while a client stalls. Replies older than three seconds cannot start parking.
Native ParkRoutine places the car at volume.position + (0,0.75,0), verified in GameAssembly
RVA 0x7A7DB0. Already parked cars and the native volume.parkingCar also block a bay.

The native PlayerCar.Update interact input is suppressed during online independent-car play.
Only host parking permission starts ParkRoutine. For occupied bays, the Update prefix temporarily
clears parkingVolume; native code hides the exterior/cockpit parking UI. A finalizer restores
the reference. E holding cannot immediately reboard during parking/grace. Own-car reentry reads
UsableCar from parkObject, which can be outside the driving controller hierarchy.

MeshAnimator skips evaluation when paused or speed zero. Network presentation seeks the source
frame with zero elapsed time and nonzero speed. Some clips require direct baked TransformData
application; no completion callbacks, audio or root-motion authority are replayed.

NPC model descriptors formerly traversed and hashed every pooled model twice on each scan.
The cache retains read-only descriptions by animator identity, animSet identity and actor name;
destroyed references are pruned and new/replaced animation sets are described anew. Streaming
city scenes do not discard surviving NPC asset descriptions. Position and animation sampling
rates were not reduced. Cold construction remains a one-time load cost.

## Remaining validation and scope

The full parking denial/release/reentry/reverse-denial/concurrent-request HUD cycle is currently
being verified with tools/duo-test.ps1 -TestParkingAuthority -TestDisconnect. Remote-driver
parking/exit still requires a dedicated shared-car native test. Legacy TestOwnedBoarding and
simultaneous-foot walking fixtures assumed both cars could park in one bay; update those
fixtures to use different bays or sequential departure before treating them as current checks.

The mod is not finished campaign co-op: quests, choices, rewards, economy, guest story
interaction and scene/interest union remain separate work. Static robot child motion and
vehicle model/damage upgrades are also unfinished. No performance claim for remote hardware
or a long play session is implied by this local two-instance sample.
