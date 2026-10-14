# Next co-op scope: independent avatars and two cars

User requirements clarified on 2026-10-12 while completing iteration 6b:
players must be able to leave the car, walk independently, see the other character,
and eventually each own a car while also entering the other's car.

This is the next architecture increment, not a claim that these features already work.
Iteration 6b first establishes one visible, interpolated remote car and its native effects.

## Entity model

- A player is a persistent `PlayerId`, an appearance and a locomotion state. It is not a
  vehicle transform and does not disappear when a vehicle is hidden.
- A vehicle is a persistent `VehicleId`, a model, a simulation owner, a driver and a seat roster.
- Player states: `OnFoot`, `Driver(vehicleId)`, `Passenger(vehicleId, seatId)`.
- Seat occupancy is arbitrated by the session host. Requests and accepted transitions have
  monotonically increasing revisions; one player cannot occupy two seats.
- Scene/district identity accompanies entity poses. Crossing a lift or entering a building
  is not reproducible from an exterior transform alone. Story/economy remain local.

## Authority and the game's singleton constraint

For the current one-car MVP the host remains the sole physics owner; a guest driver sends
inputs to that owner. Changing the driver never transfers physics authority.

For two cars, each process can simulate its own normal `PlayerCar` and stream a visual proxy
of the other vehicle. Ownership remains fixed even when someone else occupies its driver
seat: their inputs are forwarded to the original owner. The session host owns the roster,
not automatically both rigidbodies. This is a deliberate extension of the one-car model,
not a silent change to the current implementation.

Research gates before implementation:

1. Find a native, non-inlined vehicle input-consumption method. Getter patches are a proven
   dead end; OS keystrokes remain a constrained fallback and merge local/remote input.
2. Prove that the original owner's vehicle can be driven while that owner's native player
   is walking or riding elsewhere. `Global.playerCar` and `Global.player` are singletons;
   cloning an entire active `PlayerCar` is not a safe shortcut.
3. Validate exterior and interior camera changes without a second Cinemachine brain.
4. Validate scene identity/streaming and reversible cleanup across seat exit/disconnect.

## Rendering changes

`CarProxy` remains visual-only: meshes, particle effects and an inert kinematic body.
It must never become an active native player controller or an object serialized by ES2.

Add a separate `RemoteAvatar` renderer with its own snapshot timeline. On foot it follows
player poses and animation state. In a car it is attached to the assigned seat. Visibility
and animation must follow the player's state independently of the vehicle proxy.

The present `LocalCarSuppress` is only appropriate while riding a remote vehicle. It cannot
become the permanent policy for every guest. With two cars, the local car remains visible
while walking nearby or driving it; only the local player/controller is suppressed during
a remote ride. Restore every changed component, camera target and visibility state on exit.

## Wire extensions after the current visual-effects message

- `PlayerState`: player ID, scene ID, sequence, position, rotation, velocity, locomotion and
  animation state/phase. Prefer a small explicit animation state over arbitrary bone packets.
- `VehicleState`: vehicle ID, fixed simulation owner, model/appearance, pose and sequence.
- `SeatRequest` / `SeatRoster`: vehicle, driver/passenger occupancy and revision.
- `EnterVehicle` / `ExitVehicle`: host-authorized transition and safe exterior spawn position.
- Entity creation/destruction and ownership are explicit; a restart resets session identity.

Keep bounded binary framing, background socket workers, main-thread Unity access and
separate per-process status evidence. Bump the handshake version for incompatible changes.

## Delivery order and concrete checks

1. Visible remote avatar on foot: two screens show the same walk/run/idle trajectory; no
   extra collider, audio listener or save entry; original save files restored after testing.
2. Enter/exit one shared car: passenger attaches only after seat acceptance; exit produces a
   standing remote avatar near the car; five enter/exit cycles leave no duplicates.
3. Guest driving: real guest input moves the host-owned car without manipulating the host's
   own keyboard. If OS actuation is still necessary, clearly state its focus limitation.
4. Two independently owned cars: drive simultaneously, park independently, enter the other
   car, swap drivers, exit, then disconnect. Neither car teleports or changes physics owner.
5. Repeat on two PCs over LAN, then separately verify the selected internet transport.

Unit/protocol tests and two local instances are useful gates, but are not proof of two-PC
latency, internet reachability or the final full co-op experience.
