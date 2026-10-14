# Beta work log - 2026-10-13

## Recovery continuation - 2026-10-14

The previous thread exhausted request-body capacity. Its user requirements and
actual source state were recovered using Laya and ECC brownfield-retrospective;
ECC csharp-testing guided the scoped regression checks. Work continues in the
original checkout with inherited edits preserved. Full beta deadline target:
before2027-01-01, tracked in plans/beta-delivery.md.

Fresh baseline573 tests and supported build passed. Native consumable use, two
same-frame requests with duplicate packets, menu pause and process restart passed
before the clock fixes. Fixed a developer command-file stat/read race.

Independent review found alcohol expiration batching, pre-use heartbeat decay,
dirty timer loss on campaign reset and a conservative failure latch. Added exact
last-tick-aware batching, native tick metadata, clock-before-use ordering and reset
flush. Protocol18, full suite587 passed, supported build0 warnings/errors.
Pure batch reducer/tests committed as44aa60f. Socket-drop/rejoin test now replays
the accepted pre-disconnect request and proves no repeated consumption/audio.
The protocol18 native test passed and all12 original files/9 saves were restored;
both role logs had zero Error/Exception. Nine originals were visually inspected
in a contact sheet and the reconnect pair opened individually. Protocol18 full
process restart also passed: quantity1, speed timer314.59, audio0. Both role
logs had0 errors/exceptions; a separate hash/inventory comparison confirmed all
12 original files and no additional live save files. No Cloudpunk process remains.

Regional story research is documented in docs/retro/beta-recovery/regional-story-design.md.
It remains a proposed implementation; complete remote NPC event closure is not
certified. No full beta package or final acceptance is claimed.

The human explicitly requested sustained work until a distributable beta and
confirmed the main scope: world synchronization, collisions, animations,
dialogues, quests and separate inventories. The goal remains active. This is
not a release announcement or evidence that the campaign is complete.

## Verified in this stage

- Laya route fb459867-ee7d-4816-82bc-2302b35da036 selected Codex.
- Protocol campaign transport, bounded atomic chunks, presentation channel,
  session/conversation identity and pure dialogue authority: 373 tests passed.
- Supported deploy succeeded for protocol 13 and the campaign prototype.
- Native `duo-20261013-beta-campaign-live` guest-first parking passed: guest
  parked, host was denied, native landing caption hidden. Both PNGs opened.
  World traffic/NPC and animation remained live. All 11 original save files
  (8 saves) restored with identical SHA256.
- Two initial harness attempts were blocked by OpenWith dialogs caused by a
  research tool launching an extensionless objdump shell script. The specific
  analysis helpers were closed; use the verified physical Windows executable.
- Campaign prototype initially failed before publishing state: native
  inventory contains amount=-1 entries (observed RedTrail, stock=9999).
  Historical probe shows additional unowned upgrades/apartment items with
  this sentinel. Preserve it; never silently normalize the native baseline.
  Latest failed drive run also restored every original save file.

## Implementation in progress, not native accepted

Native acceptance updates:

- `duo-20261013-beta-state-accepted` passed campaign snapshot application:
  guest applied 23 snapshots; 6276 story rows had equal canonical digests;
  all 101 inventory entries including -1 sentinels survived. Host final frame
  window avg 9.00 ms / p95 10.89 / max 45.58. Guest avg 18.44 / max 48.84.
  Both final screenshots opened and all eleven saves hash-restored.
- `duo-20261013-beta-native-story` used natural active RORY ALPHA / RoryAlpha.
  Guest parked, fixture moved its native foot actor near the NPC, then sent the
  real interaction twice over TCP. Host executed 15 native line completions;
  both clients presented matching conversation and completed it. Opened both
  dialogue PNGs. This conversation has ZERO money/quest reward, so it proves
  basic dialogue presentation/completion, not reward or quest acceptance.
  No duplicate guest continue was sent because vanilla audio auto-advanced.
  All original saves restored. Readiness now also uses completed native lines,
  rather than only the trading Done button, and needs a new native check.
- A reconnect worker exceeded model capacity after partial implementation.
  It was closed before parent takeover. Parent reviewed lifecycle/pending-link
  changes, repaired the missing UI namespace and built with zero warnings/errors.
  Automatic reconnect remains a required native gate.

CampaignSnapshot and CampaignReplication add host-only native mutation guards,
typed story/quest/dialog snapshots, separate guest conversation presentation,
reward hooks, guest save isolation, request outcomes and session/line identity.
JSON uses IncludeFields for inventory DTOs and preflights inventory before
campaign field writes. Global.Update masks only automatic campaign collections
with an exception-safe finalizer; normal HUD/music/movement work remains native.

Developer defaults are off, including scripted demo/fallback, diagnostic keys,
debug overlay and status files. Gameplay E/Shift+E/F8 and presence remain live.
Packaging script has a strict mod-only allowlist; no beta archive has been made.

Personal Player2 ledger is separate from host holdings and global trader supply.
Sidecar is keyed by Campaign.Id; native StartNewGame assigns a new identity.
Test mode suppresses inventory persistence. Equipment arithmetic was inspected
against native code; guest-local effects and actual trading acceptance remain.

## Source ownership (updated after region extension)

- Parent Codex: campaign integration, native acceptance, harness/docs, release.
- Bernoulli, Carver, Hubble and Meitner are closed; their earlier ownership is released.
- Confucius researched native region travel and is closed (read-only).
- Galileo completed ambient region isolation and is closed; parent reviewed source.

Do not edit an active worker's file before its explicit scope release. Do not
deploy while either game runs. Build only tools/deploy.ps1. Harness remains
explicitly authorized by the user and restores originals in finally.

## Next gates

1. Deploy integrated sentinel fix/reconnect after source release; native guest
   must actually apply campaign snapshots. Profile the campaign stage, not only
   NPC cache performance. Fail the harness if campaign remains stalled.
2. Native host conversation completion/guest presentation, quest progression
   and reward exactly once, duplicate requests and reconnect.
3. Fix guest interaction context while host is driving; native singleton vehicle
   predicates must evaluate the requester, not accidentally the host seat.
4. Guest personal trade/use/equipment, shared quest-item ownership and pickup
   claims. Guest effects must never execute on the host actor.
5. Scene transitions/interest union, world object/collider presentation, repaired
   legacy boarding/walking fixtures, remote driver exit and long-session checks.
6. Independent review, clean beta package/docs, selected visual proof, commits.

## Independent region extension (2026-10-13)

User requires different simultaneous player regions with full shared state.
Protocol 14 region presence and removal of forced campaign scene following are
implemented. Protocol suite 382/382 passed; supported tools/deploy.ps1 produced
zero errors/warnings and deployed plugin 269312 bytes / protocol 76800 bytes.
Native foundation fixture is running, not yet accepted.

Previous host scene fixtures failed because Player.ChangeSceneRoutine starts its
nested loader on inactive Player even with an active outer runner. Native RVA
66D660-66D691 proves the nested owner. New walking fixture requires active Player;
driving uses the separate native lift routine. Do not repeat the false-success
outer-runner workaround. Full region authority, foreign-region authored events,
parked vehicle regions and handover remain gates in plans/independent-regions.md.

Native region fixtures 1-4 are NOT accepted. Scene IDs changed, but the guest
remained behind the black loading screen / falling from its old position.
All eleven original files (eight saves) were SHA256-restored after each run.
The first fixture also failed traffic reunion; that dependency on peer-car
presentation has been removed. Native readiness now includes gameplay camera
and SceneLoader.IsActive, rather than accepting the scene number alone.

Separate double-opt-in test logs expose repeated guest Global.Update null
references previously hidden by two processes sharing LogOutput.log. Native
analysis proves emptying g.triggers prevents lazy TutorialDriving/Camera lookup
(6312F7/631324); its authored registry is now retained while mutation guards and
scoped automatic timer/dialog masks remain. Pending native verification.

Static campaign research corrected an earlier assumption: ROTRIV / Side13_1 is
in scene13 Level0_Right, not scene5. Task116 points to scene5. Host global dialog
definitions include Side13_1, but CamusReady was false in the inspected baseline.
Do not force prerequisites or claim its reward/quest acceptance. One audited
cross-region conversation is a bounded next slice, not general NPC event support.

## Accepted regional travel foundation

`duo-20261013-independent-regions-lift-1` passed guest-only native car lift 6 -> 5 -> 6;
host remained6. All four region screenshots were opened: new region visibly rendered,
local ambient traffic present. Guest applied20 campaign snapshots while apart and24
by reunion; both native peer-car hulls, host NPC/traffic playback resumed. Role-specific
logs: zero native Unity errors/exceptions and zero Global.Update exceptions. All11
original files/eight saves SHA256-restored. Walking5 is not a valid fixture: LayerSpawn
was false and native ChangeSceneRoutine threw. Use authored car lifts for these regions.

Supported build after integration: plugin282112 bytes, protocol77824, protocol14,
384 tests, zero build warnings/errors. Full campaign/reward/regional authority gates
remain incomplete. No beta ZIP created. Stricter warm-reunion/lift-completion fixture
is prepared and must run after the latest readiness refinement.

## Regional native regression and connection UI work (2026-10-13)

The warm guest 6 -> 5 -> 6 lift test passed with both cameras restored, 20 Hz
traffic and 765 NPC records on reunion. The host-travel counterpart initially
failed on stripped AudioMixerGroup wrappers and invalid unstripped portrait
boxing. Native pointer mixer binding and real boxed draw arguments repaired it.
`duo-20261013-independent-regions-host-travel-fixed` now passed host 6 -> 5 -> 6
while guest stayed6, then native dispatcher dialogue: guest five voice clips,
877 portrait repaint frames, no presentation error, five authoritative host
completions. All four region PNGs opened: visibly different regions and matching
reunion dialogue text/portrait. Audio audibility itself has not been independently
measured. All11 original files/eight saves SHA256-restored. Separate logs contain
no Error/Exception matches. Loading-screen descriptor warmup ran with84 models.
Cold descriptor preparation remains about700ms; this is not a sustained latency
acceptance. 414 protocol tests passed, including30 detached regional authority
reducer cases. The reducer is not runtime-integrated or proof of AI handover.

Ambient capture now runs independently of successful local car snapshots so
walking with a parked car elsewhere cannot stop host NPC/traffic snapshots.
New user scope: a styled in-game Host/Join IP+port interface with status/errors.
Laya route5d6c232d-f5e7-4790-98a1-c61c7c33336c selected Codex. Aristotle owns only
UI/CoopConnectionMenu.cs; parent owns integration/native input masking. New UI
and its gameplay input isolation remain unbuilt/unverified at this checkpoint.
F9 is reserved for connection menu; developer native driving probe moves toF2.
Full beta gates, cross-region interactions/rewards and native AI handover remain
incomplete. No public beta archive or publication has been produced.

Connection UI integrated, supported build298496 bytes passed with zero warnings/errors.
First native menu test found ObjectCollectedException on modal open and correctly
closed the panel without stopping the connection. Local GUI interop inspection
shows TextField reconstructs a stripped TextEditor using RuntimeTypeHandle;
managed editing using native Event delivery is prepared as a repair. It preserves
paste, select-all, Backspace/Delete and caret movement. Repair and LAN/VPN adapter
address display are not yet built/deployed/native-verified. Session start/stop only
runs on Tick, and host-after-guest is refused by the actual save-isolation latch.

`duo-20261013-connection-menu-2`: repaired modal rendered in both windows; Computer
Use clicked guest Disconnect, selected IP, typed the observed host adapter IPv4,
and connected successfully twice through the menu. Manual port0 + Return remained
Off and displayed the 1..65535 validation error; replacing with27050 restored
connection. Save isolation still disabled hosting on the prior guest process.
Initial and final menu PNGs opened. Role logs have no GUI error/exception. All11
original files/eight saves hash-restored. This uses one PC's real adapter IPv4;
it is NOT independent-PC/LAN traversal or public Internet acceptance.

The host-side Stop/Start UI test was not completed: the 240-second harness window
expired during focus transfer; Computer Use reported missing foreground PID and
fresh list_windows returned no Cloudpunk windows after normal harness cleanup.
Do not retry the stale window. Menu transparency is being improved with the
already native-verified boxed texture draw. Native EventSystem input suppression
is added from startup to prevent clicks reaching the underlying native menu;
both edits require a fresh deploy/native test. Guest FX missing counters increased
on reconnect, although host FX remained mapped; bounded missing-path diagnostics
are prepared to establish the cause. Do not claim full reconnect correctness.

`duo-20261013-connection-menu-3` independently verified opaque panel rendering,
host Disconnect -> Off -> Start Host -> listener, automatic guest reconnect,
then guest Disconnect, typing adapter IPv4 and Return -> connected again.
Native EventSystem suppression and boxed texture background were deployed301056
bytes, zero build warnings/errors. Current fixture had fxMissing=0 on BOTH roles
through these reconnects, no missing-path logs and no Error/Exception matches.
Previous fixture's FX mismatch is not yet causally explained; do not erase it or
claim exhaustive reconnect correctness. Final deliberate guest disconnect left
host waiting and guest Off. The next attempted Return coincided with normal
harness teardown; no stale window retry. All11 files/eight saves SHA256-restored.
Test runs used the same PC; public IP/two-PC traversal acceptance remains open.

Faraday now owns read-only research into stable existing-host-save campaign
identity for separate Player2 inventory. No file edit ownership assigned.

## Existing-save inventory identity gate

Read-only Faraday inspection established Global.SaveGame is the filename Slot0.sav,
not a campaign ID; mutable timestamps/elapsed time/hash revisions cannot identify
lineage. Current default Campaign.Id can attach Player2 ledger/shared stock to a
wrong existing campaign. No native save UUID was established. Proposed remedy:
observe exact loaded source revision, map aliases to a persistent random UUID,
record only proven successful native commits, defer inventory configuration until
identity binding, and never auto-migrate ambiguous legacy campaign-1 ledgers.
Unknown externally produced save revisions require distinct identity or explicit
rebind. Native save format extension is not justified by present evidence.
Follow-up read-only Save/load completion contract research remains active; no
registry implementation or native save changes made yet.

Save identity implementation checkpoint (unverified native hooks): HostSaveIdentity
tracks the exact Global load coroutine, captures source bytes at ES2Reader.Create,
marks restoration after normal scoped UpdateStats with reader cleared, and binds
only after normal terminal completion without cancellation/exception. New-game
binding waits for a proven native final temp.sav -> Slot0.sav overwrite copy.
Save return timestamp is not used as success. Registry persistence lives outside
native save files; test mode uses its run output directory. Ambiguous legacy
campaign-1 inventory files are preserved, not attached to a new UUID. Inventory
configuration now waits for that UUID; host Welcome waits for identity readiness.
Switching host campaigns retires the current guest link and resets the ledger.
Plain alias registry12 tests added; 426 total passed. Supported build308224 bytes,
protocol87552, zero warnings/errors. Native observer/disk lineage/reload/new-game
acceptance remains pending. No beta package is certified.

`duo-20261013-save-identity-1` passed native host save identity binding followed
by campaign projection and bidirectional two-car movement (host34.68m, guest21.82m).
All four movement PNGs opened, peer models/world rendered, zero role log errors,
and11 original files/eight saves SHA256-restored. Native load hook ran and wrote
test-local alias registry. Generation guards added for overlapping cancelled loads
must be checked in subsequent native run. Source identity/disk ledger persistence
acceptance is now a dedicated double-opt-in fixture: TEST and TEST_PERSIST_INVENTORY,
all profiles remain inside the run directory, native save uses timestamp-independent
File.Copy commit observer. Fixture requests duplicated real guest Equip NeonTrail,
checks host inventory remains untouched, disk guest equipment, and final saved hash
aliases to the loaded UUID. Resume mode checks restoration after process restart.
Native observer reload/new-game/cancellation and legacy migration gates remain open.

`duo-20261013-inventory-persistence-1`, equip mode, PASSED: duplicated guest
NeonTrail Equip over the real socket changed guest quantity1 ->0/equipped=true,
host retained1/equipped=false, test-local disk ledger matched. Native Global.Save
ran, final File.Copy observer recorded one committed save and associated its
actual Slot0.sav hash with the existing loaded campaign UUID. Both persistent
inventory PNGs opened; all11 original files/eight saves restored. Restart/resume
mode now pending, first-run image/log/native evidence copied into equip-evidence.

The bounded FX diagnostic establishes missing paths are dynamically instantiated
Contact Sparkles(Clone)/Smoke/Sparks; guest templates have7 static authored effects,
whereas host has additional collision effects absent on guest at proxy creation.
This is not packet loss or a general reconnect mapping failure. Current protocol
lacks effect positions and dynamic prefab/incarnation mapping; faithful collision
FX replication remains required. Never hide these misses as a repair.

Persistent inventory resume mode PASSED after both processes restarted: native
guest NeonTrail0/equipped=true restored from test-local host ledger, native host
kept NeonTrail1/equipped=false. UUID was unchanged across native save revisions and
restart, and another successful native save commit alias was verified. Both PNGs
opened, role logs had no Error/Exception; original11 files/eight saves restored.
This proves personal equip persistence for the tested item, not buy/sell/use/reward
coverage or legacy ledger migration.

Contact FX repair now uses allowlisted native PlayerCar.contactSparks prefab,
source-instance logical groups, bounded dynamic replicas stripped of scripts,
car-relative position and rotation per particle, and removal when groups disappear.
Protocol advances15 because spatial fields change wire shape. Noether's detached
protocol patch was reviewed/integrated; supported deploy313344 bytes/plugin88576
protocol passed zero warnings/errors. Protocol suite/new dynamic FX native test
pending. Dewey remains read-only on consumable effect/cooldown native analysis.

2026-10-14 continuation route21728dd8-69b6-455c-9d67-927c7b1c0d66 selected Codex.
Previous goal turn made implementation and native verification progress. Dewey's
lifecycle follow-up terminated on account usage limit; original six-recipe audit
is retained, lifecycle must be completed independently. No active subagents.

`duo-20261013-dynamic-contact-fx-1` ran protocol15 with five dynamic contact groups
on each observer and zero missing templates/errors. Both final PNGs opened.
This run did not exercise parked collisions: TestCarCollision previously silently
required the boarding/walking branch. Harness now explicitly rejects that misuse.

Live save state has changed: baseline now twelve original files/nine saves and
starts on foot. `owned-collision-1` failed the obsolete driving-start prerequisite
and exposed inactive native colliders being filtered from peer templates.
Geometry selection repaired; `owned-collision-2` passed first entrant driver,
passenger control denial0.10m, remote driver20.38m and F8 exchange. Four PNGs
opened. Logs exposed invalid Transform destruction in seated avatar stripping;
now native type casts preserve Transform/Renderer/MeshFilter/Animator/MeshAnimator.

The old parked penetration counter always returned zero. Removing that bypass
made `owned-collision-3` correctly FAIL: same-save parked cars overlap1.633m.
Native save restoration passed after every run. Do not call that fixture accepted.
Protocol16 adds explicit host-authorized parked join placement requests/grants:
only an overlapping on-foot Player2/car2 can receive an authored free bay; owner
applies native parked model/character transforms, boarding waits for placement.
Supported build318464 plugin/88576 protocol, zero warnings/errors. New native
placement regression pending. Native consumable clocks remain unaudited.

`owned-collision-4` verified separate parked bays but failed boarding because the
fixture placed the host at peer car + world-X2, outside the pedestrian platform;
the camera descended below the map. Twelve files/nine saves hash-restored.
Approach preparation now uses the matching authored CharacterSpawnPoint, with
an explicit <=8m boarding-distance check rather than an arbitrary offset.
`owned-collision-5` passed parked separation, first entrant host/car2 driver,
owner as passenger, passenger drift0.07m, remote driving45.10m, mutual F8 swap.
All role PNGs opened, no native Error/Exception, all12 files/9 saves restored.
Supported deployment318976 plugin/protocol88576, zero warnings/errors.
Subsequent fixes cancel the original join placement lease on timeout/region change,
block native UsableCar entry while placement is pending, and refresh role status
after seat exchange. These need a fresh supported build/native regression.
Popper owns a new read-only consumable lifetime audit; parent continues car/UI
verification. No overlapping source writes or native UI actions delegated.

`startup-menu-1` passed actual startup RoleOff main menu. Parent used Computer Use
to click HOST/JOIN, START HOST and CLOSE; native panel changed Off -> waiting.
Read-only socket evidence0.0.0.0:27050/PID33264 matched the game process. Normal
Continue restored world, auto-guest admission succeeded, then car separation,
first entrant driver, passenger drift0.08m, remote driving41.79m and F8 exchange
passed. Correct labels visible in both post-swap screenshots. All13 PNGs opened;
12 original files/9 saves hash-restored. Guest dedicated log had noError/Exception.
Host startupOff had no dedicated logger; do not claim its native errors measured.
Test logging now permitsOff, and main-menu gameplay HUD is suppressed. Supported
deploy319488 plugin/91136 protocol passed zero warnings/errors. Installed config
has RoleOff/diagnosticsfalse/demofalse, prior file backed up in task work directory.

Popper static audit identified unnamed timer helpers and actual Global.Update
call chains: speed/category cooldown scaled-delta subtraction/clamp; alcohol
passed resets on next eligible tick, menu/SceneLoader fade bypass timers. Parent
independently disassembled632977..6329BF and confirmed speed helper/clock/clamp.
Pheromones lifetime is unresolved, no guessed countdown accepted. Popper closed.
Herschel added only PersonalConsumableEffects and its tests (75 focused passes);
parent reviewed immutable recipe/threshold/arithmetic/clock code. Full suite now
running. No native use enabled: remaining integration must bind personal effects,
validated elapsed clock, atomic inventory snapshots/sidecar and one-shot audio.
Herschel closed; no active subagents or game processes remain after cleanup.
