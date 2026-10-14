# Handoff brief — Cloudpunk online co-op mod

Everything needed to continue this work from cold, including what is verified, what is
already known to be a dead end, and exactly how to prove each next step.

Written 2026-10-12. Branch `master`, HEAD `8874d44`, working tree clean.

---

## 1. Mission

**Deliverable.** An online co-op mod for Cloudpunk: a friend joins over the network, appears
in the host's HOVA (the hover car), and can drive — seat swapping so either player steers.

**Definition of done (MVP).** Two PCs, one session. The host's car is the only real car. The
guest's avatar is visible in the passenger seat on the host's screen. One key swaps seats: the
guest now drives (their input reaches the host's machine and moves the real car) and the host
becomes the passenger. Both screens show both characters in one car. They drive around Nivalis.

**Explicitly out of scope.** Story sync, Holocash/economy, quests, shared minimap, HUD changes,
cutscenes, client-side prediction, voice chat, more than two players, reconnect mid-session.

---

## 2. Repo and environment

| Item | Value |
| --- | --- |
| Project root | `C:\Users\andre\Documents\cloudpunk-coop` |
| Game install | `C:\GOG Games\Cloudpunk` (GOG build, DLC *City of Ghosts*) |
| Engine | Unity 2018.4.29f1, **IL2CPP** backend, metadata 24.1 |
| Mod loader | BepInEx 6 IL2CPP + Doorstop, already installed in the game root |
| Interop assemblies | `C:\GOG Games\Cloudpunk\BepInEx\interop\*.dll` |
| Plugin output | `C:\GOG Games\Cloudpunk\BepInEx\plugins\` |
| Plugin config | `C:\GOG Games\Cloudpunk\BepInEx\config\dev.andre.cloudpunk-coop.cfg` |
| Plugin log | `C:\GOG Games\Cloudpunk\BepInEx\LogOutput.log` |
| Saves | `%USERPROFILE%\AppData\LocalLow\ION LANDS\Cloudpunk` (Easy Save 2, plain binary) |
| Branch | `master` (not `main`) |

**Build and deploy** (the only supported way to get a build into the game):

```powershell
cd C:\Users\andre\Documents\cloudpunk-coop
.\tools\deploy.ps1
```

It builds Release and copies **both** `CloudpunkCoop.Plugin.dll` and
`CloudpunkCoop.Protocol.dll` into `BepInEx\plugins`. A missing protocol DLL makes BepInEx fail
to load the plugin with a `FileNotFoundException` that looks unrelated to networking.

**Tests** (protocol only, no game needed):

```powershell
dotnet test src\CloudpunkCoop.Protocol.Tests\
# 108 passing
```

**Critical operating note:** the game does **not** reach the world by itself. It waits for a
key on a "press any key" splash screen and then needs the **Continue** entry in the main menu.
A verification run that skips those two steps sits on the splash screen and measures nothing.
`tools/live-test.ps1` and `tools/duo-test.ps1` do both.

---

## 3. Verified working (with the evidence)

| # | What | Evidence |
| --- | --- | --- |
| 0 | BepInEx loads on this build | interop assemblies generated, `ErrorLog.log` empty, game reaches menu |
| 1 | Plugin resolves the game's singletons | `Global`, `GameInput`, `Player`, `PlayerCar` all non-null; car is `PlayerVehicle1`; `playerCars` has 9 entries |
| 2 | Full input map | 48 `PlayerAction`s with bindings, dumped to `docs/03-input-map.md` |
| 3 | Seat anchors + avatar clone | clone parented under `CP_Seat_Passenger` at a measured offset; 4 `.sav` files byte-identical after a run. **Structural only — never confirmed visually** |
| 5 | Network protocol | `dotnet test` → 108 passing, including real loopback sockets |
| 6a | **Host streams the real car over TCP** | headless client received 219 snapshots at 18.3 Hz with coordinates matching the plugin's own log |
| 6a | **Two instances connect on one machine** | `host: sent=1477` / `guest: recv=1476, remoteRate=20.3, remotePos=-356.5,14.0,-46.0` |

Side quest that works (not part of the co-op MVP): an obstacle-avoiding autopilot that flew the
car ~900 m to a waypoint (waypoint distance 1000 → 105) with reverse-escape from dead ends.

---

## 4. What failed, and the constraint it creates — READ THIS FIRST

The single most important thing to understand before designing input handling.

### 4.1 InControl virtual device — impossible

The original plan was an InControl virtual device fed from the network. It does not exist in
this build: IL2CPP strips every type the game never references, and
`InControl.Virtual.VirtualDevice` is absent from the metadata. The same wall applies to any
helper type that binding manipulation would need.

### 4.2 Harmony on `OneAxisInputControl.Value` — the patch works, the car ignores it

Approach: a Harmony prefix on the `Value` getter of `InControl.OneAxisInputControl`
(`PlayerAction` derives from it), matching the eight driving actions and returning our numbers.

Three ways of identifying the actions were tried:

1. **By reference** — never fired.
2. **By `PlayerAction.Name`** — never fired: the prefix is always handed a wrapper typed as the
   base `OneAxisInputControl`, so the cast to `PlayerAction` is null every time.
3. **By native pointer** (`IL2CPP.Il2CppObjectBaseToPtr`) — **works**. Values are genuinely
   replaced; our own reads report `forward=1.00`, `ascend=1.00`.

It still does not drive the car. The decisive test, with no confounds:

```
override reports ascend=1.00 for 35 seconds   ->  car y stayed 15.11, did not rise at all
real Space key held for 2 seconds             ->  car rose 17 m  (y 15.11 -> 31.97)
```

Lift is vertical, so no wall or obstacle can explain the difference.

**Why:** IL2CPP inlines trivial property getters at compile time, so the game's native code
reads the field directly and never calls `get_Value`. A Harmony patch on that getter can only
affect *managed* callers — which is us and our debug overlay. This is measurable: the overlay
samples 48 actions at ~120 fps, and the hit counter grows by almost exactly `8 × fps` for the
driving actions, which accounts for all the traffic on its own.

**Consequence.** Input cannot be injected by patching InControl's accessors. Two viable routes
remain:

- **(A) Patch a non-trivial game method that consumes input.** IL2CPP does not inline large
  methods, so a patch on the real consumption point should work. Needs research: find where
  `PlayerCar` (or whatever drives it) reads throttle/steer/altitude each frame, then patch that.
  This is the preferred route: no window focus requirement, no mixing with the host's own input.
- **(B) Actuate real keystrokes on the host machine.** **Proven to work** — synthetic
  `keybd_event` *with the scan code* drives the car (100 km/h, a 90 m hop). Without the scan
  code the game ignores the key entirely. Drawbacks: the host's game window must be focused,
  and the host's own input merges with the guest's.

Route (B) is already implemented and reusable: `Game/Autopilot.cs` publishes a decision to a
file and `tools/autopilot-driver.ps1` turns it into held keys. For co-op the same driver would
be fed from network input instead of the autopilot.

---

## 5. Architecture that must be kept

1. **Physics authority never moves.** The session host always owns the real `PlayerCar`
   Rigidbody. Only *input* moves between machines. This is what keeps the project from becoming
   a distributed-simulation problem.
2. **Transform snapshots, not prediction.** Client-side prediction needs the physics law, which
   is stripped from the binary. Snapshots are boring and always correct.
3. **No second in-process player.** Each machine runs its own game as player 1, so camera, HUD,
   audio and input are untouched. The guest's own car gets suppressed instead.
4. **TCP first, relay later.** Works on LAN immediately and over the internet once a port is
   forwarded. A small relay on an existing VPS can follow so friends need no configuration.
5. **Sockets on background threads, game objects only on the main thread.** IL2CPP will not
   tolerate a background thread touching UnityEngine objects. `CoopConnection` does all socket
   work and hands decoded messages to a queue; `CoopSession.Pump` drains it on the main thread.
6. **Role defaults to `Off`.** The plugin must never open a listening socket unless asked. An
   internet-facing port is an operator decision, not a side effect of installing the mod.

---

## 6. Next task: iteration 6b — the guest draws the car

**This is what is missing right now.** The guest receives the host's car pose and does nothing
with it, so two windows side by side look completely unsynchronised. Nothing remote is rendered
yet.

### Tasks

1. `src/CloudpunkCoop.Plugin/Game/CarProxy.cs`
   - Clone the car visually (a second `PlayerCar` GameObject, or reuse the seat-rig clone
     approach already in `Game/SeatRig.cs`).
   - Give it a **kinematic** `Rigidbody` (`rBody.isKinematic = true`) so it cannot fight physics.
   - Buffer incoming snapshots (~100 ms) and interpolate position and rotation between them.
   - Snap instead of interpolating when the buffer underruns.
   - Discard snapshots older than the newest (the sequence number is already on the wire).
2. `src/CloudpunkCoop.Plugin/Game/LocalCarSuppress.cs`
   - On the guest, park and hide the guest's own car and block its input, so there is exactly
     one car visible.
3. Camera: make the guest's camera look at the proxy. The plan suggests a Cinemachine virtual
   camera with higher priority than the existing one. Do **not** create a second brain.
4. Carry the guest's avatar in the seat alongside (reuse `SeatRig`).

### Acceptance criteria

- Guest launches, connects, and sees the host's car move smoothly while the host drives.
- Both machines show the same car in the same place.
- Measured snapshot rate 19–21 Hz.
- No rubber-banding in a 3-minute drive.
- Only one car visible on the guest.

### How to verify

```powershell
cd C:\Users\andre\Documents\cloudpunk-coop
.\tools\deploy.ps1
.\tools\duo-test.ps1
```

`duo-test.ps1` launches both copies, drives them through the splash screen and the main menu,
lets the host drive for 8 s, captures **each window separately** with `PrintWindow`, and prints
both per-process status files. Then **look at the two PNGs it writes** to
`C:\Users\andre\AppData\Local\Temp\opencode\duo-01-host.png` and `duo-02-guest.png`. The guest
window must show the host's car moving. Reading the images is not optional — this project has
already burned time on claims that were not visually checked.

### Pitfalls specific to 6b

- Two instances share one save folder and the game auto-saves. `duo-test.ps1` backs the save
  folder up and restores it; keep that behaviour.
- Two instances share one BepInEx log file, so the log cannot be used to tell the two apart.
  Per-role status files (`BepInEx\config\coop-status-host.txt` / `-guest.txt`) exist for this.
- Only one window can be focused. The two-instance harness handles focus per window; a desktop
  screenshot would only ever show the front one.
- The guest will load the same save, so its own car starts at the same place as the host's.
  Without suppression you will see two cars in the same spot and think the proxy works.

---

## 7. Then: iteration 7 — the guest drives

Depends on the input decision in section 4. Recommended order:

1. Spend one focused session on route (A): find the method that consumes driving input and try
   patching it. Acceptance: with the patch installed and a synthetic value written, the car
   moves with nobody touching the keyboard. If that passes, use it — it is much better quality.
2. If (A) fails, use route (B): `InputMessage` from the guest drives
   `tools/autopilot-driver.ps1` on the host. Acceptance: the guest holds a key, the host's car
   moves, and the host's own keyboard is untouched.

Then `SeatMessage` swap: host broadcasts the seat change, both sides apply it, input rebinds on
the car host.

**Acceptance:** full steering, throttle and altitude control from the guest with no
desynchronisation on seat swap.

---

## 8. Tooling reference

All scripts live in `tools\` and are PowerShell 5.1 compatible. **Write them ASCII-only** —
PowerShell 5.1 reads files as CP1251 on this machine, and Cyrillic in a script breaks parsing.

| Script | Purpose | Notes |
| --- | --- | --- |
| `deploy.ps1` | build + copy both DLLs into the game | run after every code change |
| `live-test.ps1` | one instance: splash → main menu → world → scripted keys → screenshots | `-Actions "tap:F6,hold:W:6000,shot:a.png,wait:2000"`, `-KeepRunning` to leave it up |
| `duo-test.ps1` | two instances (host + guest) over loopback | backs up and restores saves; captures each window separately |
| `autopilot-driver.ps1` | reads `BepInEx\config\autopilot-command.txt` and holds real keys | the working actuator for route (B) |
| `extract_class.py` | prints a class declaration from the metadata dump | `python tools\extract_class.py --signatures-only PlayerCar` |

Key injection rules learned the hard way:

- `keybd_event` **must include the scan code** or the game ignores the key.
- The game window must be in the foreground. A background process cannot take the foreground
  with `SetForegroundWindow` alone; the working recipe is `ShowWindow` + `SetWindowPos(TOPMOST)`
  + `AttachThreadInput` + `SetForegroundWindow` + `SetFocus`, and it is already implemented in
  the scripts.
- **The Discord overlay steals keyboard input.** If the operator reports "not a single key
  works", check that first.

Headless client for protocol work without a second game:

```powershell
dotnet src\CloudpunkCoop.TestClient\bin\Release\net6.0\CloudpunkCoop.TestClient.dll 127.0.0.1 27050 12
```

It connects, sends HELLO, and reports the snapshot count, observed rate and how far the remote
car travelled.

---

## 9. Do not repeat these

- **Do not assume the engine is UE4.** It is Unity. An earlier estimate was based on that error.
- **Do not patch trivial property getters and expect the game to notice.** IL2CPP inlines them.
  See section 4.2.
- **Do not conclude "the car does not move" from a test where the car is parked nose-first into
  a wall.** That mistake produced a wrong "PASSED" commit that claimed the car travelled 1.4 km;
  the real figure was 0.7 m in 36 seconds. Raycasts are cheap: look before you drive.
- **Do not attribute movement to the mod when a human might have been at the keyboard.** One
  "success" was actually the operator flying the car out by hand.
- `Global.IsInVehicle` is true whenever the game has a player car assigned, including before
  the player is actually inside it. Do not use it as "the player is driving".
- `PlayerCar.currentSpeed` is **km/h**, not m/s.
- `ilspycmd -l class` returns an incomplete list (1394 entries, missing `Global`, `GameInput`,
  `PlayerCar`, `Vehicle`, `Minimap`, ...). Decompile by name with `-t` instead.
- BepInEx's interpolated log handler supports neither alignment nor format specifiers. Build log
  lines with concatenation and `PadRight`.
- Il2CppInterop strings need an explicit `ToString()` before use in interpolated strings.
- `laya-router` is unavailable on this machine (`torch: ModuleNotFoundError`). Do not block on it.
- The game must be closed before deploying, or the DLL copy fails with a sharing violation.

---

## 10. One verification cycle, start to finish

```powershell
cd C:\Users\andre\Documents\cloudpunk-coop

# 1. game must not be running
Get-Process Cloudpunk -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. build and deploy
.\tools\deploy.ps1

# 3. protocol tests, if the network layer changed
dotnet test src\CloudpunkCoop.Protocol.Tests\

# 4a. single instance with scripted keys
.\tools\live-test.ps1 -Actions "wait:20000,shot:a.png"

# 4b. or both roles at once
.\tools\duo-test.ps1

# 5. LOOK at the screenshots in C:\Users\andre\AppData\Local\Temp\opencode
#    and read BepInEx\config\coop-status-*.txt for the per-process truth

# 6. commit; never leave the tree dirty
git status --short
git add -A
git commit -F <message-file>
```

**Reporting rule that this project has earned the hard way:** state separately what was
changed, what was verified, what was deployed, and what a human still has to confirm by eye.
A green build is not a working feature, and a log line is not a moving car.
