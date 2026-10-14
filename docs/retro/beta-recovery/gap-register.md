# Beta recovery gap register

Current phase: 5 - forward execution and native verification.

Recovered on 2026-10-14 from the complete local rollout for thread
`01a12d05-afa3-74c2-bf1e-db8dfe29e384`, its real user requests, the current
checkout and fresh build/test evidence. The previous thread failed with
`Encoded request body exceeds 67108864 bytes`; work did not reach full beta acceptance.

Canonical requirements remain in `plans/beta-acceptance.md` and
`plans/independent-regions.md`. This register supplements those files rather than
replacing their scope. Requested delivery deadline: before 2027-01-01.

## Perimeter

Active slice: `CoopPersonalEffects`, `CoopInventory`, campaign snapshot/action
integration, `PersonalClockMessage`, consumable reducer/tests and the consumable
branch of `tools/duo-test.ps1`. Callers: plugin bootstrap, session message drain,
native inventory UI and Global.Update hooks. Risks: item loss, replayed sound,
host effect contamination, incorrect timer restoration and solo-save writes.
Separate regional/story/parking slices remain in the canonical acceptance plans.

| ID | Axis | Gap | Evidence | Forward fix | Severity | Effort | Status |
|----|------|-----|----------|-------------|----------|--------|--------|
| G-001 | Thinking | Consumable integration exists but historical progress still says unwired | PROGRESS.md current status; CoopPersonalEffects.cs | Establish fresh native evidence and update current status | P2 | S | done |
| G-002 | Coding practice | Use, duplicate, pause and restart gates have no fresh native acceptance | tools/duo-test.ps1 TestConsumables; docs/08-beta-work-log.md | Run existing native fixtures, inspect images and role logs, prove complete save restoration | P1 | M | in-progress |
| G-003 | Missing features | Cross-region rewarded interactions and shared-car transitions incomplete | plans/independent-regions.md outstanding gates | Implement each regional slice with native acceptance; retain full requested scope | P1 | L | open |
| G-004 | Technology selection | Single loaded native region limits host-authoritative remote story execution | CampaignReplication.cs Handle/StartConversation; plans/independent-regions.md | Audit authored event catalog and native context requirements before selecting regional executor design | P1 | L | open |

## Fresh baseline

- `dotnet test src/CloudpunkCoop.Protocol.Tests/CloudpunkCoop.Protocol.Tests.csproj -c Release`: 573 passed, 0 failed, 0 skipped.
- Supported `tools/deploy.ps1`: Release build, 0 warnings/errors; plugin 332800 bytes, protocol 92672 bytes.
- No Cloudpunk processes existed before the controlled native test.
- Existing pending changes were preserved; no previous worker was dispatched by this continuation.
- Native fixture in progress: `work/duo-20261014-consumables-recovery-1`.

## Review and correction evidence

- Independent review identified accumulated alcohol tick divergence, pre-use
  heartbeat decay, dirty timer loss on campaign reset and a latched campaign
  failure that transport reconnection does not clear.
- Added a pure `AdvanceTicks` operation and seven boundary/atomicity tests.
  Protocol18 conveys eligible tick count and last tick start; the tracker rejects
  tick regression and elapsed progress without ticks. Seven additional clock
  cases cover zero-time eligible updates and invalid metadata. Full suite: 587 passed.
- Use requests now enqueue their current native clock before the action on the
  same reliable stream. Campaign reset flushes dirty personal timer state first.
- The campaign failure latch remains conservative: native mutation/reward failures
  require a full session restart, rather than silently reopening execution.
  User-facing recovery wording and failure injection acceptance remain a gap.
- Fresh supported deploy: 0 warnings/errors, plugin334336/protocol93184 bytes.
- Native use and process restart passed before the protocol18 corrections;
  original12 files/9 saves were SHA256-restored. Fixed a command-file stat race.
- Protocol18 native use/reconnect and process-restart acceptance passed in
  `work/duo-20261014-consumables-clock-reconnect-1`. Replayed accepted request
  did not spend another item or replay audio. Restart quantity1/speed314.59/audio0.
  Both role logs had0 errors/exceptions; separate restore check matched12/12
  original hashes and the exact12-file live inventory. All game processes stopped.
  Native alcohol expiration and all recipe/trade/reward gates remain open.
- Began the next regional slice with actual host definition capture, not only a
  proposal:749 records(12 actors,427 triggers,310 dialogues), including Rory's
  before/after listeners and Achievement74/Rotriv1/CamusReady action descriptors.
  Output is explicitly uncertified. G-003/G-004 remain open until a complete
  remote conversation/reward/object-state fixture passes.

## Execution order

1. Complete and verify personal consumables, duplicate requests, paused native
   timers and persisted restart; address findings before recording acceptance.
2. Close campaign dialogue/reward/trade and reconnect gates.
3. Implement and verify the regional gates in the canonical plan.
4. Repeat parking/collision/animation and representative performance acceptance.
5. Verify network/menu behavior and default diagnostics, package and independently
   inspect the distribution, then commit reviewed stages.

Do not describe same-PC sockets as two-PC/Internet acceptance, protocol tests as
native gameplay acceptance, or this recovery as a completed beta.
