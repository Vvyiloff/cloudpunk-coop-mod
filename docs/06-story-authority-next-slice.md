# First shared story slice — native research, 2026-10-13

Research only; no campaign synchronization has been implemented or verified.

Named interaction uses Character.GetGuid(), resolved on host with
Global.FindSerializeObject(guid).TryCast<Character>(). Validate a unique named, active,
same-scene, non-crowd target and fresh authoritative guest position. Character.OnInteract()
must execute once on host: it invokes beforeDialogAction and starts native DialogHUD.FadeIn.
Do not separately invoke FadeIn or DialogLine.Complete as a mirror operation on guest.

Requests need connection identity, session/scene epoch, request ID, campaign revision, target
GUID and expected dialog ID. Reserve before entering native code; duplicate requests return
the existing result. Conversation events need interaction ID, ordered sequence, runtime line
index/lineID and revision. Reconnect restores current presentation without replaying rewards.

Direct guest assignments may mirror absolute money/item amounts, Dialog.completed,
DialogLine.played, QuestText fields and SavedBool/SavedInt.value keyed by object GUID plus
the actual SavedValue.Id(). Preserve missing/unloaded saved records. Assigning these fields
does not itself invoke UnityEvents; automatic guest campaign processing still needs suppression.

Native DialogLine.Complete(bool), RVA 0x6D9A50, sets played without rejecting an already-played
line, calls StoryTrigger.Unlock, activates/deactivates quests, mutates item counts and directly
adds positive money. GainMoney interception alone therefore cannot enforce reward authority.
The native teletype coroutine calls Complete; wrapper-only interceptions need native-call tests.
DialogHUD.OnContinue/FadeOut and Character.OnFinishedDialog invoke afterDialogAction, autosave
or NPC behavior changes. UnlockedItem.FadeIn also triggers an achievement. None is a proven
presentation-only guest API. A separate guest presenter is required.

Guard guest Character interaction/finish, line completion, StoryTrigger processing, economy/item
mutation and Global.Autosave/Save. Isolate automatic Global.Update campaign work while retaining
unrelated movement/rendering. In-memory deduplication alone is not crash-safe exactly-once execution.

Candidate for native preflight: ROTRIV -> Side13_1, scene 5, dependency CamusReady. Installed
English table suffix 14 gives +50 BUSINESS, suffix 15 activates Task116, suffix 16 unlocks
Rotriv1. These suffixes are authored IDs, not verified runtime line indexes. Existing probe says
dialog uncompleted and Task116 disabled; it does not capture the NPC GUID, current playability,
reward metadata or runtime event listeners. Preflight those before choosing this live test.

Acceptance: send one guest request twice, execute one native host conversation, show matching
speaker/line/objective on both sides, grant exactly +50 on host, assign equal guest money, and
observe zero guest completion/event/save executions. Reconnect and resend without another
reward or reward popup. Verify under backed-up saves and restore originals afterwards.
