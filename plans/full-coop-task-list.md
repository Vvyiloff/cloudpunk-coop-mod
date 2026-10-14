# Full co-op implementation

User authorization: implement and independently verify a playable full co-op mod, including
remote acceleration wake, map presence, shared mission progress, walking and mutual seat swaps.
Preserve saves, existing work and the validated interpolation baseline. Commit completed stages.

## Authority and invariants

- Player IDs are stable per connection (host 1, guest 2). Each car has owner-native physics;
  occupancy, parking grants and campaign execution are serialized by the host.
- Each actor has a location state: on foot, driver or passenger, with a vehicle ID and scene ID.
- Presence and occupancy are different: a connected player is not automatically seated.
- Seat changes require two matching requests, a host-issued revision, and a timeout/cancel path.
  Use F8 as the temporary test key after checking bindings. Disconnect cancels pending swaps.
- Guest driving must use a real native input seam verified against native vehicle movement.
  The inlined OneAxisInputControl.Value getter patch does not satisfy this requirement.
- Story snapshots and subsequent revisions are host-authored. Guest interactions are requests;
  never independently execute rewards, purchases, scene loads or mission completion on both peers.
- Save state must remain reversible during verification. Never treat shared filesystem saves
  in a two-window test as network synchronization.
- Base Cloudpunk mechanics determine scope. Do not invent enemy combat if the game lacks it.

## Stages and acceptance

1. Preserve the verified shared-car presentation baseline (179 protocol tests, live 2.2 km drive).
2. Diagnose actual wake/trails using bounded native visual probes. Verify acceleration, coasting
   and stopping on both windows, with state messages aligned to the rendered pose timeline.
3. Implement actor/scene snapshots, walking animation and map markers. Verify enter/exit,
   walking, reconnect, loading and out-of-scene presence without duplicating the local player.
   Latest additions: authoritative ambient traffic, walking civilians/robots and named story
   NPCs, including spawn/despawn and animation. Model identity must be validated before guest AI
   suppression; native scene and campaign side effects remain separately authoritative.
4. Implement revisioned mutual-consent F8 seat exchange and genuine network driver input.
   Verify request label, timeout, reverse swap, disconnect and no stale-input acceleration.
5. Inspect native story, dialogue, inventory, money, vehicle service and save APIs. Implement
   join snapshot plus ordered authority events with idempotency. Verify at least an actual
   mission transition, choice/reward, scene change and late join against host/guest state.
6. Independent owned cars and boarding: integrate only after the native singleton constraint
   is resolved with verified physical authority per vehicle; preserve shared-car boarding.
7. Run end-to-end gameplay checks, document actual supported mechanics and remaining gaps,
   and commit reviewed changes with a clean tracked tree.

## Research in flight

2026-10-13: all previous research/implementation agents are closed. Main Codex owns the
current animation repair and runtime checks. Computer Use was explicitly reauthorized by
the user for two-window testing. See the current PROGRESS.md section for verified scope.

- Story/save/scene authority: read-only native API research agent.
- Vehicle input, enter/exit and ownership: read-only native API research agent.
- Wake/minimap: native API research plus new VisualProbe.cs only.

2026-10-12 update: initial agents completed or hit quota and were closed. Baseline plus F8
exchange/trails committed as 613ae76. NPC registry collector is a new single-file sidecar;
main owns NPC protocol/simulation, traffic, mobility and all deployment/game control.
Computer Use was physically stopped with Escape; no further Computer Use input this turn.

Only the main executor controls game processes, deployments and overlapping source edits.
