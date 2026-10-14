# Two vehicles and serialized boarding — 2026-10-13

This is a movement/boarding playtest stage, not a completed campaign co-op implementation.
Current wire protocol is 11. Both installations must deploy the same protocol and plugin DLLs.

## Authority

- Car 1 is simulated by the host's native PlayerCar; Car 2 by the guest's native PlayerCar.
- Host VehicleOccupancy serializes both players' locations and the sole driver of each car.
- First boarding an empty car grants driving. Later boarding grants passenger occupancy.
- Leaving the driver promotes the remaining passenger. Stale requests cannot reclaim a seat.
- Non-owner driver input is revisioned, rate-limited, and expires after 0.3 seconds.
- Native owner input is suppressed while that owner is a passenger. Native interaction is
  suppressed during boarding transitions and when the owner is not the current driver.
- Owner physics never runs on the kinematic peer clone. Native solid hulls provide contacts;
  peer body movement runs in FixedUpdate and discontinuities snap without swept movement.
  Peer clones do not suppress the local
  vehicle or move its camera unless the local player explicitly boards the peer's car.

## Controls

- E: own car boarding / native parking, or exit a parked peer car.
- Shift+E: explicitly board the peer car within eight metres. This avoids ambiguous selection
  when both saved games spawn cars at the same parking location.
- F8: request driver/passenger exchange; the other player must confirm F8 within ten seconds.
- F12: diagnostic vehicle/NPC IDs. F11: read-only campaign inventory.

## Confirmed evidence

`work/duo-20261013-owned-visible`: two owner-controlled cars, independently moved 26.44 m and
21.84 m as observed by the other client. About 20 Hz on both peers. Host PNG visibly contains
two separate car meshes with Car 1 / Player 1 and Car 2 / Player 2 labels. Both PNGs were opened.
Remote particles: ten templates, two trails, zero missing templates. Disconnect removed the
peer car and restored local traffic/NPC presentation. All eleven original save files restored
with matching SHA256; both test processes exited.

`work/duo-20261013-boarding-owned`: both native players parked and went on foot. Host boarded
the guest's empty Car 2 first: P1 vehicle=2, P2 vehicle=0, Car 2 driver=1. The guest then boarded
its own Car 2 as passenger: P1 vehicle=2, P2 vehicle=2, Car 2 driver=1. Both role files agreed;
PNG role/camera/actor evidence inspected. Saves restored with matching hashes.

`work/duo-20261013-control-authority` passed passenger acceleration denial, non-owner native
driving and F8 exchange in Car 2. Parking/exit through the remote-owner parking command still
requires a dedicated native check.

## World replication and remaining gaps

Native NPC registry now supports mesh-animated actors and static cleaning robots. Animated
model identity includes normalized actor root name, mesh descriptors, animation/material assets
and hierarchy. Matching catalog duplicates may share a template only when their asset identities
agree. Static copies never instantiate Character/SerializeObject or their scripts.

Live run reached 765 complete NPC records, with roughly 25–37 active, and guest status replicating.
Registry scene traversal is cached by loaded scene handles; runtime saved actors and NPC pools
are refreshed. Dormant records reuse pose data instead of repeatedly reading native transforms.
Bidirectional walking poses were visually checked and native NPC frame evaluation was measured.
Scene/interest streaming, procedural robot child motion, interactive ghost affordances and
vehicle damage/model upgrades remain incomplete. Peer contacts now use native collision hulls;
high-latency contact behavior still needs a separate-network playtest. Asset descriptors do not
establish equality between different game builds.

Campaign/quests/economy are NOT synchronized. Guest native campaign processing and save writes
are not yet comprehensively guarded. The test harness backs up/restores saves; common initial
files do not prove synchronized missions. See docs/06-story-authority-next-slice.md.

## Verification

Build only with tools/deploy.ps1, with the game closed. PowerShell source remains ASCII.
Use tools/duo-test.ps1 -TestIndependentCars -TestDisconnect for two-car movement, and
-TestOwnedBoarding -TestDisconnect for boarding/input/exchange. UI testing requires the user's
current Computer Use authorization. Respect any physical Escape stop and restore test saves.
