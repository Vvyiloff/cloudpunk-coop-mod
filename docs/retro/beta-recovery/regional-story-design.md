# Regional story execution design

Date: 2026-10-14. Status: proposed; static investigation, not native acceptance.

## Parent continuation: fresh native catalog evidence

The parent implemented developer-only `NativeCampaignProbe.CaptureDefinitions`
and exercised it in `work/duo-20261014-authored-catalog-1`. It reads direct fields,
persistent calls and runtime listener counts; it never invokes IsPlayable,
FindDialog, Check, Complete or event callbacks. Native artifact contains749 records:
12 actors,427 triggers and310 global dialogues. All data is locally host-observed;
the header deliberately says Certified=false. It is an audit input, not a complete
trusted region certificate or shipped detached executor.

Fresh RORY ALPHA evidence: stationary active scene6 actor at(6.47,8.25,-1.05),
GUID0a5df13c-cdbd-41f2-a561-a231e1f1a506. Before-event has no persistent/runtime
listeners. After-event has ordered MoveToDisable(NavPoint MoveTargetRory), then
SetCanTalkTo(false), both runtime-only persistent calls. Achievement74 invokes
UnlockSteamAchievement(74). Its global trigger has no input/item/quest dependencies.
Rotriv1 and CamusReady observed persistent/runtime action lists are empty.
Side13_1 still contains+50 at runtime index13, Task116 at14, Rotriv1 at15;
ROTRIV's actor placement is still absent in the scene6 actor catalog.

Remaining certification work includes dynamic listener lifecycle, transitive output
graph, stable object argument identity and NavPoint movement/disable behavior.
Do not omit Rory's after-event or achievement to claim complete remote dialogue.
The native consumable/reconnect regression also passed during catalog capture;
all12 original save files/9 saves were hash-restored. Both role logs and catalog
integrity checks are independently inspected by the parent.

## Decision and scope

Use a **host-owned detached regional campaign executor** for certified authored
interactions. Resolve NPC/dialogue/trade metadata from the host's installation or
host-observed loaded objects, copy it into immutable records, and execute explicit
typed operations against one host campaign state. Project committed state and
presentation into whichever region each player actually loads. Do not instantiate
foreign Characters, load a second gameplay Layer, swap singleton/player context,
execute guest-provided reward deltas, or let a regional ambient authority write the
campaign.

This is implementable incrementally, but the available evidence does **not** certify
a complete rewarded NPC conversation for immediate remote execution. Unknown event
closures must reject admission before the first line or reward. General scene-bound
UnityEvents require explicit adapters and remain outside the first slice. A queue
for those events alone would not satisfy independent-region story acceptance.

The parent is verifying consumables. This sidecar changes only this document:
no source, tests, builds, game runs, deployment, installations or other-task messages.
ECC `architecture-decision-records` was applied only for context, decision,
alternatives and consequences; no ADR directory/index or framework rewrite.

## Evidence and current limitations

Evidence pointers below are checkout-relative unless an absolute path is given.
Declarations prove fields/signatures, disassembly proves the cited instruction/call,
and historical probes prove only their dated observations. Neither proves current
gameplay behavior. The checkout contains extensive pre-existing tracked/untracked
work; this investigation does not treat PROGRESS.md as current acceptance evidence.

| Evidence | Finding and consequence |
|---|---|
| `plans/independent-regions.md:43`–outstanding gates; `docs/retro/beta-recovery/gap-register.md`, G-003/G-004 | Cross-region rewarded interactions, unloaded object state and context-safe execution remain required. |
| `src/CloudpunkCoop.Plugin/Game/CampaignReplication.cs:318`, `Handle`; `:355`, `StartConversation`; `:371`, `ContinueConversation` | Kinds 1/2/5/6 require the host's scene and fresh foot pose. Start resolves a **loaded** Character through `FindSerializeObject`, calls native `FindDialog/IsPlayable/OnInteract`; continuation uses host DialogHUD. Removing the scene check leaves these dependencies intact. |
| `CampaignReplication.cs:273`, presentation receive; `Tick`, foreign-region guest conversation clearing | Cross-region presentation is rejected, then any active conversation is cleared. A new executor also needs explicit conversation-region/audience routing. |
| `src/CloudpunkCoop.Plugin/Game/CampaignSnapshot.cs:21`, `:26`–`:58`, `:167`–`:215` | Capture discovers triggers/characters from live registries. A saved record with no linked object and no currently registered trigger GUID can be excluded. Apply skips unavailable saved schemas/Characters and uses direct rollback-capable field setters. This is not an authoritative archive for never-loaded or disappeared region records. |
| `src/CloudpunkCoop.Plugin/Game/RemoteFootAvatar.cs:48`–`:66`; `RegionReplication.cs:15`–`:21`; `src/CloudpunkCoop.Protocol/RegionPresenceMessage.cs` | Foot receive retains foreign-scene poses; rendering is same-region only. Presence is sequenced and expires after 3 seconds; foot freshness is 2 seconds. Neither message binds a pose to a region-entry epoch. These are client observations, not proof against a modified client. |
| `src/CloudpunkCoop.Plugin/Net/CoopSession.cs:210`–`:229`, `:448` | Request handling drains into the existing pump, then campaign tick runs. Reuse this main-thread integration point; do not add a worker performing native campaign writes. |
| `src/CloudpunkCoop.Protocol/DialogueAuthority.cs`, `Begin/SetLine/TryContinue`; `CampaignMessages.cs`, `CampaignActionMessage` | Session, request, generation and runtime index seams already exist. Continue is latched per line, but the authority permits both players to continue; a new audience/control policy must be explicit. Request deduplication is in-memory, not crash-safe. |
| `src/CloudpunkCoop.Plugin/Game/CoopInventory.cs:217`, `:332`, `:371`, `:444` | Existing shared rewards reconcile positive host-native deltas. Existing trade arithmetic already avoids native callbacks/context swaps, but trader admission and price lookup still require a loaded Character. Reuse audited arithmetic after integration with the parent's finalized inventory API. |

### Exact native metadata and event seams

`analysis/dump/dump.cs` and `script.json` map the supported IL2CPP declarations and
addresses. Independently read-only disassembled the installed
`C:\GOG Games\Cloudpunk\GameAssembly.dll` with existing `llvm-objdump` and resolved
direct call targets with `script.json`. SHA256:
`f9982812867496598d3bd6e1d5e58cfb9901dfa5cd36d446728827da17925650`.
Addresses below are RVAs, not portable APIs. No disassembly files were written.

| Seam | Exact evidence | Design implication |
|---|---|---|
| Global definitions vs scene actors | `dump.cs:223570`–`:223583`, `:223671`–`:223672`, `:223761`; `CharacterDefinition`, `:230268`–`:230285` | `Global.characters`, `Dialogs`, inventory and quest definitions can supply authored data independently of a Character. A CharacterDefinition is a definition component, not a substitute scene actor. Definition dialogue list order must be retained. |
| NPC identity/context | `Character`, `dump.cs:220401`–`:220480`; `SerializeObject:229170`–`:229186` | Persist GUID, defined/override definition, scene, authored position, talkability, disable/hide conditions, saved field IDs, itemOffers, priceFactor, buysItems and actor events. Distinguish static placement from moving actor state. |
| NPC lifecycle callbacks | `beforeDialogAction` 0x108, `afterDialogAction` 0x110; `OnInteract` RVA 8CC920, `OnFinishedDialog` 8CC4C0 | OnInteract calls UnityEvent.Invoke at 8CCD72 and DialogHUD.FadeIn at 8CCDCB. OnFinishedDialog invokes its event at 8CC5FC and changes actor/UI behavior. Skipping these silently can lose story effects. |
| Dialog admission | `Dialog`, `dump.cs:223382`–`:223428`; `IsPlayable` RVA 6D2E10 | Flags include completed, inCar/notInCar, inApartment, dependencyName/checkNotUnlocked. Native admission lazily caches dependency at 6D3084 and calls `StoryTrigger.Check` at 6D315E. Check calls Unlock at 72BFDA: this is **not a pure predicate**. |
| Line operations | `DialogLine`, `dump.cs:223337`–`:223378`; Complete RVA 6D9A50 | Authored fields: lineID/voice/text/speaker, triggerName, invalidateDialog, quest on/off, itemGive/amount, moneyReward/reason, callDelay, showItemOffers, overCom. Complete writes played = !repeatable at 6D9AD5; calls Unlock 6D9D5F, quest off/on 6D9E67/6D9EC0 and ChangeUnits 6DA046. Positive money directly adds to Global.money at 6DA146, bypassing GainMoney. Negative money is negated and written **back into line.moneyReward** at 6DA05C–6DA05E, then subtracted at 6DA09F. Copy authored signed amounts before mutable native execution; initially exclude negative rewards and repeatable economic lines. |
| Native timing/finish | `DialogHUD`, `dump.cs:230541`–`:230645`; coroutine `:230649`–`:230696` | Teletype MoveNext RVA 6D7E70 calls Complete at 6D87FD and 6D90F8 on different paths and auto-OnContinue at 6D9607. OnContinue calls Character.OnFinishedDialog at 6D59C7 and FadeOut at 6D59DB. FadeOut calls Autosave at 6D533D, SetCanTalkTo 6D53CC, MoveToDisable 6D540A and SetCallTimer 6D545D. A detached scheduler must define completion readiness/skip/finish semantics; do not reuse this coroutine as a headless executor. |
| Trigger graph and action | `StoryTrigger`, `dump.cs:229643`–`:229811`; Check/Unlock RVA 72BDE0/72C160 | Copy input graph/OR, item prerequisites, progress/timer fields, quest changes, give/take item, disableCharacters and ordered action closure. Unlock calls downstream Check at 72C371 and UnityEvent.Invoke at 72C512. Audit transitive effects, not just the named line trigger. |
| Serialized event description | `UnityEngine.Events.PersistentCall`, `dump.cs:124345`–`:124416`; `ArgumentCache:123857`; `UnityEventBase:124449` | Persistent target, method, mode, arguments and callState are available in declarations; ArgumentCache contains object/type, int, float, string, bool. InvokableCallList also has runtime listeners at `:124420`–`:124422`. Public GetPersistent* convenience methods were not found in this dump; a build-specific read-only accessor or asset parser is needed, not assumed. Persistent-list emptiness alone does not prove no runtime effects. |
| Singleton/scene loading | `Layer`, `dump.cs:225351`–`:225421`; Awake RVA 7A13E0 | Awake starts MEC coroutine at 7A17BB, adjusts PlayerCar layer limits 7A1872, skybox 7A18F7 and shadow distance 7A1944. Additive gameplay loading or temporarily replacing Layer.Instance cannot isolate lifecycle effects. |
| Unloaded saved state | `SavedObject`, `dump.cs:223304`–`:223334`; Global.AddSerializeObject RVA 642DD0; `SerializeObject.Start/OnDisable` RVA 721520/721710 | SavedObject.values is separate from linkedObject. Missing linkage does not prove missing saved primitives and does not imply a usable Character. Exact lifecycle restoration/overwrite order still needs verification. |

### Dated local probe: usable candidate data, not an accepted interaction

`C:\GOG Games\Cloudpunk\BepInEx\config\coop-campaign-host.jsonl` has header
UTC `2026-10-13T23:09:04.6357082Z`, scene 6, 6389 data records and 12 linked named
Characters. Reading it does not establish any currently running game's state.
`NativeCampaignProbe.cs:20`–`:75` explains what it captures and what it omits.

* `Side13_1`: actor ROTRIV, dependency CamusReady, nonrepeatable, uncompleted.
  Runtime index/lineID 13 gives +50; index 14 activates Task116; index 15 unlocks
  Rotriv1. voiceFileIDs are 14/15/16 respectively. This establishes the index
  distinction missing from the old design note.
* Task116 marker is scene 5. It is **not the NPC's region**.
  `docs/08-beta-work-log.md:120` corrects ROTRIV's location to scene 13 Level0_Right,
  superseding the scene-5 candidate in `docs/06-story-authority-next-slice.md:33`.
  This investigation did not independently extract ROTRIV's level13 placement.
* The probe contains global Side13_1 while host is in scene 6, but no ROTRIV
  Character row. It does not enumerate event listeners or trigger names alongside
  trigger GUIDs, and omits line invalidateDialog/callDelay/showItemOffers and some
  admission flags. It cannot certify the complete conversation or its dependency.
* A scene-6 candidate with known placement is RORY ALPHA,
  GUID `0a5df13c-cdbd-41f2-a561-a231e1f1a506`, position (6.47, 8.25, -1.05),
  historically active/talkable. RoryAlpha has 15 lines; last index/lineID 14
  triggers Achievement74. It is **not certified event-free**, and its achievement
  closure/before/after events need audit before admission.

## Proposed execution contract

### 1. Trusted authored catalog, retained without native pointers

Introduce a bounded `RegionalStoryCatalog` keyed by installation fingerprint,
scene and GUID. There are two admissible catalog sources:

1. Copy actual host-native definitions/actors/events on successful host region
   readiness after native load. Retain only detached primitives/IDs across unload.
   This supports previously visited host regions without a new asset dependency.
2. Later, extract immutable scene metadata from the **host's local installed**
   `Cloudpunk_Data/levelN`, referenced sharedassets and global definitions, preserving
   transforms/active ancestry, GUIDs, references and persistent call order. Validate
   against live host captures before enabling unvisited regions.

No Unity asset parser was available in the inspected Python runtimes; no parser was
installed and scene event payloads were not extracted. Asset extraction is a future
implementation option, not work completed here. Guest-discovered placements,
listener lists, prices and reward amounts never populate the trusted catalog.

Admission requires a certificate for the **whole** before-event, dialogue and
after-event closure, including every reachable trigger/input/output and referenced
item/quest/object. Unknown methods, missing references, duplicate GUIDs, dynamic
listeners, ambiguous actor definitions and graph cycles without audited behavior
fail closed. Name-to-trigger mapping must come from trusted metadata; the current
probe's GUID-only trigger rows are insufficient. Bounded closure expansion prevents
unlimited work. The certificate records adapter version and catalog digest.

Separate catalog from mutable `RegionalCampaignState`: retained trigger primitive
values keyed by GUID and actual SavedValue.Id(), dialogue played/completed state,
quests, regional object enabled/talkable state and revisions. Do not replace a
regional archive with only the next loaded-region capture. Represent deletion and
unknown state explicitly. Mobile actors need host-validated archived simulation
state; the first slice admits stationary placement only.

### 2. Intent, host admission, ordered commit

Guest sends intent only: Interact(NPC GUID), Continue(generation, runtime index,
lineID), Buy/Sell(item ID, merchant GUID, quantity). The host chooses the dialogue,
price, effects and next line. Bind identity to the connection. Add a host-issued
region-entry epoch (separate from ambient AI lease), expected campaign/object
revision and bounded pose sequence reference. Current session/request IDs remain.

Host validates fresh matching presence/foot observations, epoch, region, on-foot
state and absence of vehicle occupancy, finite position, NPC enabled/talkable
state and distance (existing 4m interaction, 5m trade). Use catalog coordinates for
stationary targets, not a missing host Character. Reject unknown/mobile targets,
loading/reconnect stale epochs and unsupported certificates. Pose freshness/range
checks cannot establish honest physical movement by a hostile client; complete
anti-cheat would require authoritative movement/transition validation, outside
this design. They never authorize client-supplied campaign effects.

Use one `CampaignMutationCoordinator` on Unity main thread: reserve request and
conversation/line identity, perform pure admission, prepare a complete typed plan,
then commit and publish revision/results. Serialize loaded native host story,
remote story and both players' trade writes through the same coordinator. Block
new native host dialogue admission while the first remote conversation owns the
story lease; pause automatic native self-triggering/call/timer mutation for that
short lease while retaining lookup registries. Do not blanket-stop Global.Update
or change personal-effect clocks. Existing prefixes are seams to integrate, not
proof every direct native campaign write is already intercepted.

Allow one campaign conversation globally in the first slice. The initiating
player controls it; same-region listeners may receive presentation, foreign host
can receive a read-only observer view. Reject competing host/guest interactions
with a busy reply. This is a deliberate serialization policy for simultaneous
requests, not independent concurrent conversations. Later concurrency requires
conflict sets over the same single commit stream.

### 3. Explicit adapters instead of native event invocation

Compile authored effects into a closed typed operation set:

* dialogue line played/completed/invalidation;
* activate/deactivate/progress quest using host definitions;
* trigger unlock/progress and input propagation using a pure, bounded audited
  graph evaluator;
* audited story rewards into the host and guest inventory ledgers;
* region-scoped object enabled/talkable/behavior state projections;
* separately deduplicated presentation commands (voice, portrait, music, achievement).

Perform native parity research for graph evaluation before admitting triggerful
dialogue; declarations are not sufficient to reproduce Check/Unlock ordering,
required-item semantics or timeout behavior. Do not call native IsPlayable as a
pure preflight or native Complete/Unlock to execute a detached operation. For v1
restrict to nonrepeatable, non-apartment, non-self-triggering, stationary NPCs,
positive rewards, audited quest/item effects and fully certified event closures.

Preserve native ordering in a prepared plan; unknown effects reject the whole
interaction before it starts. In particular, do not grant Side13_1 +50 and only
later discover Rotriv1 cannot be executed. Negative reward mutation, repeatable
reward semantics, repair, companions, scene changes, cutscenes and player freeze
need separate adapters/certification.

`ChangeScene`, `SetNextSpawnPoint`, `FadeToBlack`, `FreezeControls`, `FullRepair`,
companion/parking actions and arbitrary GameObject methods may target the local
player/Layer. Never invoke them on host for a foreign guest. An eventual adapter
must commit a host-decided command for an explicit player/region and apply only
local physical/presentation effects there; completion acknowledgment is not a
guest reward assertion. If a campaign step depends on unverified physical outcome,
leave it pending or unsupported rather than advancing by client assertion.

### 4. Trade without a loaded trader

Resolve merchant GUID/scene/offer set/priceFactor/buysItems from a certified host
catalog and mutable regional enabled state. Calculate from the host Item catalog:
`forceBasePrice ? basePrice : trunc(float32(basePrice) * priceFactor)`, the existing
`CoopInventory.TryTradePrice` rule; validate bounds and quantity before mutations.
Reuse existing buy/sell stock arithmetic (native buy requires positive stock;
stock -1 is not unlimited), personal ownership/equipment rules and story-item
rejection. Check the parent's final personal cooldown rule; do not invoke host
CheckCooldown UI/audio on the guest's behalf.

Commit guest wallet/items plus shared supply under the same host coordinator,
using the parent's finalized ledger interface; never call native TradeOffer.OnClick,
InventoryUI.SellItem or swap Global money/items. Price and stock are not fields
accepted from a guest. Host native trades must use the same admission/commit stream
to prevent interleaved stock loss. A buy/sell is a personal transaction and must not
pass through `OnSharedReward`. Separate offer presentation needs authoritative
quote/offer messages because guest BuyPrefix currently reads a live TradeOffer.

### 5. Rewards, durability and projection

For loaded native host dialogue, retain the existing baseline/delta reward seam
only within an exclusive coordinator interval. For detached dialogue calculate
the certified authored reward directly and atomically credit both personal
ledgers according to current shared-positive-reward policy. Do not also invoke
LinePostfix/OnSharedReward for that operation. Unknown net effects or negative
payments must not be inferred as rewards.

Conversation readiness must be host-scheduled from certified line/text/voice
metadata, independent of host HUD/Player.IsActive. Guest audio completion is
presentation feedback, not reward authority. Define when line effect commit occurs
relative to display/skip and parity-check both native teletype paths. Do not assume
completion only happens when Continue is clicked.

Extend conversation data with interaction scene, owner/control, audience and
monotonic event/line identities. Route by audience and interaction scene instead
of snapshot source scene or SameRegion. Reconnect restores current line/voice
position without replaying line effects. A revision must become visible only after
the prepared mutation is committed; reply identifies that commit revision.

Current `Replies` survives socket replacement but not host process restart, and
the inventory sidecar is not an atomic campaign transaction log. Initially either
limit execution to a live host session and state that restriction, or add a durable
host command journal keyed by persistent campaign identity, player, command ID and
conversation/line operation. For restart-safe acceptance persist deterministic
after-state and outcome before acknowledging; restore/project after-state
idempotently, reconcile with native save identity and inventory persistence, and
never replay native UnityEvents. A file append plus independent native autosave
does not itself prove atomicity. Failure after effects but before acknowledgment
must produce a reconciliation state, not a fresh executable retry.

Snapshot capture merges detached regional authority with loaded native state under
ownership rules; it cannot overwrite newer detached changes with stale load-time
defaults. On load, link authored schemas, project absolute authority before
allowing interactions/automatic trigger processing, then project object activity
and colliders. Continue using `CampaignFieldTransaction` for preflight/rollback of
direct assignments, but its field rollback does not make external events or file
persistence transactional. Missing object/schema remains pending/unsupported.

## Minimal first implementation slice

Implement **catalog certification plus one serialized detached conversation**, with
no general scene loader or campaign rewrite:

1. Add a host-only read-only catalog capture at region readiness. Copy scene/GUID,
   stationary placement, definitions, full dialogue fields and before/after/trigger
   event descriptors; release native references on unload. Bound extraction and
   fail on runtime listeners or unresolved event payloads. Include a completeness
   flag: today's NativeCampaignProbe is not a certificate.
2. Add a pure reducer for admission, generation/line reservation, played/completed,
   certified positive reward/quest effects and serialized busy handling; integrate
   at CampaignReplication.Handle/pump. Keep the native same-region path for
   unsupported content. Maintain exclusive writer ownership while detached work
   runs, including guards for automatic native campaign mutations.
3. Add interaction-region/epoch and audience presentation routing. First fixture:
   host captures the target region, leaves via an authored lift, guest remains and
   requests the same certified stationary NPC while host is elsewhere. No host
   Character lookup after departure. Duplicate continue, competing host request
   and reconnect must not replay effects.
4. Audit one **entire** candidate closure before enabling it. RoryAlpha has a known
   GUID/scene and an achievement tail; Side13_1 has a known +50/quest/trigger tail
   but still lacks trusted NPC placement and Rotriv1/event closure evidence.
   Neither is authorized as a completed reward fixture by this investigation.
   Do not force CamusReady, strip an achievement/trigger, or stop a conversation
   before its unsupported tail and claim full acceptance.
5. After that vertical slice, adapt the existing personal trade reducer to certified
   merchant records; then add trigger/object adapters and restart-safe journal.
   Consumable use/equip/effect-clock implementation remains with the parent.

The first code deliverable can safely be catalog capture and a detached reducer
that rejects uncertified content. A successful cross-region rewarded story gate
requires the next certified full closure, reward/quest parity and native fixture;
catalog capture or a dry-run alone does not close G-003/G-004.

Future validation, **not run here**: pure tests for stale epochs, forged reward/price
fields, unknown closure, duplicate line/reconnect, revision conflicts, bounds,
missing unloaded objects and stock races; backed-up native fixture for both travel
directions with host stationary in the foreign region, actual guest dialogue and
voice, one +50 per eligible player, correct quest/trigger/object changes on entry,
zero foreign-context native mutations and unchanged original saves after verified
restoration. Restart/crash-window tests are required before durable exactly-once
claims. Compare same-region native reference behavior without forcing prerequisites.

## Alternatives and consequences

| Alternative | Reason not selected |
|---|---|
| Execute on guest and report rewards | Grants campaign authority to untrusted assertions and creates competing writers. |
| Temporarily load/swap host Layer/player context | Native Awake, coroutine, camera, skybox and actor lifecycle effects are not isolated by restoring a pointer. |
| Invoke global native Complete with a null/foreign Character | Complete reaches triggers, UI, mutable rewards and host context; omits NPC before/after events and dialogue finish semantics. |
| Host consumes raw guest UnityEvent descriptions | Guest chooses effects and references; cannot establish host-authored authorization. |
| Queue every interaction until host arrives | Safe deferral can be a fallback, but does not execute story while regions differ. |
| Dedicated second gameplay process | Could isolate Layer but adds save/context orchestration and another potential writer; no existing isolation/transaction proof and outside the minimal slice. |

The chosen design makes host-derived reward authority, unloaded-state retention
and explicit regional effects reviewable. Its cost is native-parity work and a
certified-content rollout; unsupported NPCs must produce an honest rejection.

## Explicit unresolved behavior

* Exact live/serialized persistent and runtime listeners for ROTRIV, RoryAlpha,
  CamusReady, Rotriv1 and Achievement74; trigger GUID/name/reference mapping and
  transitive graph closure. Existing probes cannot answer these.
* Native Check/Unlock order, required-item ownership policy, timer/progress/repeatable
  semantics and all admission flags; IsPlayable can mutate state.
* Teletype effect timing and skip/autoadvance parity, voice duration/path selection
  for a foreign on-foot player, finish/talkability/callDelay effects.
* Lifecycle ordering for saved-object defaults, unlink/relink, activation/colliders,
  and authoritative projection before Start/OnAfterLoad or automatic processing.
* Whether definitions are complete before a region loads, effects attached by
  runtime code, and never-visited region catalog extraction. A global dialogue
  observed in scene 6 does not imply every scene object exists there.
* Coverage of all native campaign writers: existing Harmony prefixes/direct-field
  transactions do not prove a global mutation lock across asynchronous coroutines.
* Campaign journal/native save/guest ledger crash reconciliation and achievement
  side-effect deduplication. Socket deduplication is insufficient.

These are bounded implementation admission gates, not reasons to relax authority
or reinterpret independent-region acceptance as ambient rendering only.
