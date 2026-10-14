# Independent player regions

User requirement 2026-10-13: players may travel independently and occupy different
regions while retaining one coherent campaign and world. This extends the beta
acceptance gates; it is not optional passenger-only behavior.

## Implemented foundation and native lift acceptance

- Protocol 14 adds sequenced region presence independent from car/foot poses.
  Loading and stale peers cannot supply ambient simulation for a local region.
- Campaign frames no longer command guest travel. Absolute authored state is
  applied to loaded local records, then projected again after local region entry.
- Foreign-region traffic/NPC frames are rejected before changing receive windows.
  Native local AI/presentation is restored when the guest is elsewhere.
- Presence identifies the partner's region even when their mesh is not local.
- Developer walking transition rejects inactive actors instead of reporting a
  successful outer coroutine whose nested loader never ran.

## Native transition evidence

Player.ChangeSceneRoutine.MoveNext RVA 66D660-66D691 starts LoadNewScene on Player,
even if its outer routine was started on SceneLoader. Driving disables Player.
The two previous 6 -> 5 fixtures therefore did not prove scene loading. Native
walking travel must run on active Player; driving travel uses PlayerCar.EnterLift
and LiftRoutine (684FA0 / 7A6220). Scene 5 is Level1, scene 6 Level2_Midtown.
Do not reuse the inactive-player/outer-loader workaround. A separate exception
was captured in the walking routine after loading scene5: its Layer has no default
CharacterSpawnPoint (native diagnostic LayerSpawn=false). Arbitrary walking travel
into a car region is not a valid fixture. Use authored lifts for city regions;
walking doors require their authored destination entry names/spawn points.

`duo-20261013-independent-regions-lift-1` passed guest-only authored lift 6 -> 5 -> 6,
host retained6. Opened all four PNGs, both regions visibly rendered. Guest continued
absolute state application in5; traffic/NPC/peer car replication resumed in6. Both
role native logs contained zero Unity errors/exceptions or Global.Update errors.
All eleven original files/eight saves were SHA256-restored. A stricter subsequent
fixture also waits for native IsInLift=false, a 15-second warm window and peer hulls.

## Outstanding full synchronization gates

Local ambient restoration alone is NOT complete regional synchronization.

1. Region authority and handover: retain state when players separate/reunite;
   NPCs/traffic must not rewind, duplicate or accept a former authority's frames.
2. Cross-region campaign interactions: current host validation needs a loaded
   native Character/trader. Do not bypass it using an untrusted guest event claim.
   Implement authored regional event execution without corrupting native Layer,
   player singleton context, personal effects or reward deduplication.
3. Preserve story/world object state for unloaded regions, project it on entry,
   and synchronize objects' activation/colliders as well as primitive flags.
4. Vehicle region is independent from a walking owner's region. A parked car
   must stay where left; boarding and parking validation need regional catalogs.
5. Shared-car occupants travel together, with one exclusive driver. Separate-car
   lift travel may not force the other player's region.
6. Dialogue audience/control policy, separate inventory transactions, simultaneous
   mission interactions and reconnect must remain consistent across regions.

## Acceptance fixture

tools/duo-test.ps1 -TestIndependentRegion 5 -TestRegionByLift -HostDriveSeconds 0 -TestCampaignState
backs up saves, leaves host in region6, requests the authored native vehicle lift
only for guest, checks region presence/campaign continuity,
then reunites guest and checks fresh NPC/traffic replication. Open all four region
PNGs. This foundation fixture does not validate cross-region quests or authority handover.

Required subsequent native cases: both directions of region travel; own cars via
lifts; leaving parked cars behind; one player completing a rewarded objective
while the other is elsewhere; independent trading; shared vehicle travel; host
joining a guest-simulated region; disconnect/reconnect in different regions.
All original saves must be restored and SHA256 checked.

Host regional lift6 ->5 ->6 is now also native-verified in
`duo-20261013-independent-regions-host-travel-fixed`. All four PNGs opened;
matching dispatcher text and portrait at reunion, five guest voice clips,
877 portrait frames, no role log errors. Original11 files/eight saves restored.
This does not verify shared-car travel, regional NPC interactions or AI handover.
