# Full co-op audit (2026-10-12)

## Confirmed baseline and new seams

- Baseline a3e2dd9: host-owned car, guest render timeline, camera, particle templates,
  reversible local suppression and honest handshake/presentation presence.
- The missing acceleration wake is two native TrailRenderers below Exhaust Neon/Trail.
  The original visual whitelist removed them. Retain their native templates and replicate
  emitting/visibility/lifetime/width with the car's sequence, not a decorative throttle guess.
- PlayerCar.Update calls Inputs(float), which reads composed Drive, Steer, Altitude and Strafe.
  Prefix writes whole InputControlState structs into thisState; finalizer restores originals.
  Native Drive/Steer directly read Value at axis+0x68. Altitude also has a getter call;
  the old patch did not map the composed axis. Do not generalize the getter dead end to all input.
- A live zero/lift/zero probe changed native isRising/currentFloorLevel and real Rigidbody height
  from 55.59 m to roughly 59 m before the test stopped. The earlier probe at 34.55 m did not
  rise; inspect layer limits before attributing every no-motion result to failed interception.
- F8 requests are host-revisioned, expire after ten seconds, cancel on repeated same-player
  presses and require the other player's confirmation. Physical authority stays on the host.

## Campaign authority is still unfinished

Live evidence: after driving, host HUD money was 2102, guest 2052. Shared initial filesystem
saves are not evidence of synchronized campaign progression. Never describe this as full co-op.

Native data surfaces:

| State | Installed native API |
| --- | --- |
| Registry | Global.savedObjectDictionary, SavedObject.guid/linkedObject/values |
| Story | StoryTrigger.unlocked: SavedBool, progress: SavedInt; discover Id(), do not assume IDs |
| Dialogs | Global.Dialogs; Dialog.id/completed/isPlaying/lines; line index+lineID+played |
| Objectives | Global.questTexts; QuestText.enabled/progressCurrent/progressMax/markerPos/markerInScene |
| Economy | Global.money; inventory Item.name/amount/totalAmountAvailable/isEquipped |
| Scene | SceneLoader.LoadNewScene(int,bool), Player.ChangeScene(int), SetNextSpawnPoint(string) |
| Persistence | Global.Autosave/Save(int), ES2Writer.Save overloads and ES2AutoSaveManager |

Typed saved values include bool, float, int, Vector3, transform, string and object. The old
description as only a flat JobN_M integer map is incomplete. Linked objects may be absent for
unloaded scenes; never delete or overwrite their records blindly.

Do not replay StoryTrigger.Unlock or DialogLine.Complete on guests to mirror final flags:
they can invoke UnityEvents, rewards, quest operations, NPC actions and scene changes twice.
Use one host-authored campaign, durable snapshots plus ordered presentation events. Guest
actions need target identity, scene epoch, distance/dependencies and revision validation.

First verification slice: one live non-repeatable, same-scene dialog that advances a quest
and changes money/item count. Capture registry and canonical digests; prove guest progression
and write suppression; perform native completion once on host; display the same objective and
line on both clients; confirm reward exactly once and reconnect without replaying it.

Chunk full snapshots (frame limit 65536 bytes), verify digests and atomic application, and
reject stale scene-epoch actions. Mid-dialog reconnect, runtime UnityEvent listeners and
target-aware guest NPC interaction require separate real gameplay checks.

## Movement and supported native game mechanics

Real boarding: UsableCar.OnInteract -> ChangePlayerCar -> Player.Deactivate and car activation.
Parking coroutine restores Player at ParkVolume.characterSpawnPoint and autosaves. Calling
ChangePlayerCar alone does not implement the full transition. Global.IsInVehicle reports
the inverse of Player.IsActive, so menu/loading state still needs separate guards.

Remote walking can preserve Player.anim (MeshAnimator), call SetStateAndDirection and exactly
one UpdateTick source. Current seat clone is not a verified walking actor. Required states:
Idle, Turning, Walking, Running, Falling and BlockingAnim; preserve native direction.

Native evidence supports conversations, trading, inventory use/sale, Holocash, pickups,
vehicle fuel/repair/collision damage, scripted lockdown/explosions and DLC race abilities.
No verified player shooting/melee system exists in the 48-action map. FXV shader-demo shooters
are not campaign combat. Synchronize actual game mechanics before proposing extra combat.

Separate owned cars remain a native-singleton ownership problem. Actor/vehicle IDs and
on-foot/driver/passenger states must precede independent vehicle physics and boarding.
