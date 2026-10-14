# Cloudpunk co-op mod — feasibility verdict and plan

## Verdict

**Feasible. Not cheap, but there is no hard blocker.** The two things I assumed would kill
the project turned out not to apply:

1. It is **Unity/IL2CPP**, not UE4. IL2CPP is the harder backend in one respect — the C#
   bodies are compiled to C++ and stripped, so there is nothing to decompile — but it is the
   *easier* backend in the respect that matters: BepInEx + Il2CppInterop + Harmony gives full
   runtime reflection over every class, and a published VR mod already does exactly that to
   this build.
2. Online co-op does **not** require a second in-process player. Each player runs their own
   copy of the game on their own machine, each as "player 1". The rendering, HUD, camera and
   input stack stay exactly as the developers built them and are never touched.

What remains is a state-sync layer plus a way to show a remote player's avatar. That is a
normal multiplayer project, not a ground-up re-implementation of the game.

## Why this design works

The naive plan — "add a second player inside one process" — is what makes single-player Unity
games look hopeless: second camera, second HUD, second audio listener, second input set, all
against code written assuming one player. None of that applies here.

```
  host machine                              guest machine
  ┌───────────────────────────┐            ┌───────────────────────────┐
  │ unmodified Cloudpunk      │            │ unmodified Cloudpunk      │
  │  player 1 = local Rania   │            │  player 1 = local Rania   │
  │  native physics, native   │            │  native physics, native   │
  │  input, native HUD        │            │  input, native HUD        │
  │            ▲              │            │            ▲              │
  │            │ snapshots    │            │            │ snapshots    │
  │  ghost avatar (remote) ◄──┼── Steam ───┼─► ghost avatar (remote)    │
  │  story/economy state sync │  P2P       │  story/economy state sync │
  └───────────────────────────┘            └───────────────────────────┘
```

The local player is never simulated by the network. Physics, input feel, camera and HUD are
100% native on both sides. The mod only:

- spawns a **ghost avatar** for the remote player (car + character, cloned from the game's
  own prefabs through `Resources.Load`, so no asset extraction is required);
- streams that avatar's transform;
- mirrors story / economy state in one direction.

## Subsystem-by-subsystem judgement

| Subsystem | Verdict | Evidence | Remaining work |
| --- | --- | --- | --- |
| Transport | **Easy** | `Steamworks.SteamNetworkingSockets` is compiled in and `SteamManager` already initialises Steam; connect by SteamID, no port forwarding | wrap the callbacks |
| Mod injection | **Solved** | working VR mod for this build ships the whole BepInEx IL2CPP stack | adopt it, add our plugin |
| Local player | **Free** | untouched | none |
| Remote avatar spawn | **Easy** | world prefabs live in the Resources bundle; `PrefabLoader`, `SpawnPrefab`, `CharacterFactory` exist | find prefab paths, clone, strip input/camera |
| Car transform sync | **Easy** | ghost is kinematic on the client; host owns the real car | ~20 Hz snapshots + interpolation |
| Shared car (ride together) | **Medium** | host car authoritative; guest avatar is parented to it and made kinematic | riding pose, camera offset |
| On-foot remote player | **Medium** | `Player` is a `MonoBehaviour` with `Rigidbody` + `CharacterController`; clone the player prefab, drive its `Movement(float delta)` from received input | identify the input read point |
| Client-side prediction for cars | **Hard, optional** | all tuning exposed as fields (`engineTorqueCurve`, thruster `springForce`/`damperForce`, torque sets) but the force law is stripped | reverse `PlayerCar.FixedUpdate` from `GameAssembly.dll`; only needed if transform sync feels laggy |
| Story sync | **Medium** | story is a graph of `StoryTrigger` nodes; state is a flat key→int map in unencrypted `Slot0.sav` | broadcast dialog id + line index and `unlocked`/`progress` deltas; force the guest's dialog into that state |
| Cutscenes | **Easy** | `CutsceneStarter` wraps a `PlayableDirector`; both clients can play the same Timeline locally | send "play cutscene X" instead of streaming video |
| Shared economy | **Medium** | `Holocash`, `HolocashAccount`, `TradeOffer` are small classes | host-authoritative money, guest sends intents |
| Save / persistence | **Medium** | ES2 binary, unencrypted, easy to read and write | decide whose save wins; keep guest progress separate |
| NPC traffic & pedestrians | **Leave local** | ambient cars follow splines (`VehicleTrack`), so each client simulating its own is harmless | suppress remote-side collisions only if it bothers |
| Per-player HUD | **Not needed** | each client renders only its own player | none |
| Camera / audio listener | **Not needed** | one player per machine | none |

## What is genuinely unknown and must be measured in Phase 0

- Whether the Input System is the legacy one behind InControl, and where `GameInput` reads
  values, so remote input can be injected at the right seam.
- What `PlayerCar.FixedUpdate` actually applies. Optional, but it decides whether driving
  feels crisp or floaty.
- The prefab paths for the player car and character inside the 3.26 GB Resources bundle.
- Whether `Slot0.sav` is rewritten atomically enough to host-authoritative writes.
- Whether a second `MonoBehaviour` carrying a networked ghost can coexist with the original
  save system without `ES2AutoSave` picking it up (it walks components and may serialise it).

## Plan

### Phase 0 — feasibility spike (1 week)
- Install the BepInEx IL2CPP stack into the game, confirm it loads and logs.
- Dump the generated Il2CppInterop wrapper assemblies and confirm `Global`, `GameInput`,
  `PlayerCar`, `Minimap` are reachable with full signatures.
- Log every read of `GameInput.playerActions` to find the input seam.
- Locate the car and character prefabs under Resources and prove they can be spawned.
- Deliverable: a one-button debug overlay proving (a) hooks work, (b) a second avatar can be
  spawned and driven. **Everything after this is scheduled work, not research.**

### Phase 1 — playable two-player sandbox (2–3 weeks)
- Steam P2P connect/disconnect by SteamID, host authority.
- Ghost avatar: spawn, transform snapshots, interpolation, name tag.
- Two separate HOVAs, free-roam, no story.
- **Done when two people can drive around Nivalis together over the internet.**

### Phase 2 — shared world state (3–6 weeks)
- Story state sync via the ES2 key map and `Dialog`/`DialogLine` replication.
- Cutscene triggering on both machines.
- Shared Holocash, host-authoritative trades.
- Reconnect and host migration basics.

### Phase 3 — polish (1–2 months)
- Ride in the same car, passenger mode.
- Chat / voice.
- Guest-side story-independent activity so the second player is never idle during cutscenes.
- Configuration UI, keybinds, latency compensation.

Total: roughly **3–4 months** to a genuinely good two-player co-op, for someone already
comfortable with IL2CPP tooling. A hobbyist doing it alone in evenings should expect 6–12
months.

## Hard truth about scope

A **branching co-op campaign** — both players making dialogue choices that fork the story
independently — is not achievable. The dialogue graph mutates a single global state machine
(`Dialog.isPlaying`, `StoryTrigger.unlocked`) with one `Global` singleton behind it. Two
independent storylines would mean cloning and forking that whole system.

What *is* achievable, and is what players have actually asked for on Steam since 2023: one
player drives the story, the other is free in the same city — their own car, their own
errands, their own conversations with vendors — and they experience the campaign together
for the cutscenes and the city.

## Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Il2CppInterop support for metadata v24.1 (Unity 2018.4 is the oldest common case) | project-blocking | validate first in Phase 0; the VR mod already proves the stack loads on this build |
| Mod breaks on game updates | high | GOG build hash `50cce2edf27f`; pin and refuse to run on unknown builds |
| `ES2AutoSave` serialises our ghost components into the save | medium | keep ghosts out of the auto-save registry, or mark them unsaveable |
| Transform-sync driving feels laggy | medium | keep input local on each machine; only the *other* player is interpolated |
| Anti-cheat / VAC concerns | low | GOG build has no anti-cheat; this is private co-op, not competitive |