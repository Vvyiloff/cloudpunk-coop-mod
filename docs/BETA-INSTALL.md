# Cloudpunk Coop experimental beta

This is an unfinished beta, not acceptance of a complete shared campaign. Use backed-up
saves. Two-player movement, vehicles, seat exchange and world presentation have prior
playtest evidence in the project docs; this package has no independent runtime certification.
Campaign implementation is being developed separately by the parent task. Shared NPC
conversation, choices, objectives, quest progression, money/items/rewards, guest save
suppression and reconnect without duplicated effects must pass live acceptance before
being described as working campaign co-op. Matching initial campaign digests are insufficient.
Scene transitions, parking release/reentry and long sessions also need current acceptance.

## Prerequisites and installation

Both players need their own Cloudpunk install and a compatible BepInEx Unity IL2CPP loader.
The local reference installation inspected on2026-10-14 uses Unity executable/player
file version2018.4.29.5295330 and BepInEx6.0.0-be.682+910844a982dbb126d0c6544847424bc42fb7849c
(Core and Unity.IL2CPP assemblies). This identifies the tested loader/engine;
it is not the Cloudpunk content/build number or certification of other versions.
The development install uses .NET 6 plugin assemblies and generated BepInEx interop wrappers;
this archive does not provide the loader, Unity/game assemblies or a .NET runtime. Use the
same tested game/loader versions on both PCs. This beta has not been verified across versions.

1. Close Cloudpunk. Back up your save directory and any existing mod/config files.
2. Install the compatible loader separately and launch once to generate interop assemblies;
   close the game again. Existing working loader installs do not need replacement.
3. Extract the two DLLs under `BepInEx/plugins` into that folder in the game root.
   Replace both together; remove duplicate older copies of these mod DLLs elsewhere in plugins.
4. Review `BepInEx/config/dev.andre.cloudpunk-coop.cfg.example`. For a fresh install copy it
   to `dev.andre.cloudpunk-coop.cfg`; for an existing install merge settings manually.
   The example suffix prevents silently overwriting your configuration.
5. Open the in-game co-op panel with `F9` (or the main-menu Host / Join entry). Choose
   Host and Start Host on one PC; choose Join, enter the host IPv4 and port and Connect
   on the other. Configuration-based startup is still available: `[Network] Role = Host`
   on the host, `Role = Guest` on the other PC and
   `Address` to the host's LAN/VPN address. Use the same `Port` (default TCP 27050),
   `IndependentCars` setting and exact beta build. `127.0.0.1` only connects on the same PC.
   Allow the chosen TCP port through the host firewall for the intended network.
6. Start both games and check the co-op presence HUD/connection. Begin with a short movement,
   entry/exit and seat exchange test using disposable or backed-up saves. Do not assume
   full campaign acceptance. Host native saves remain authoritative; guest native writes
   are suppressed for that process, including disconnect/shutdown. Restart with Role=Off
   for ordinary solo saving. Player 2 inventory uses a separate host sidecar keyed by
   a persistent campaign identity. The mod records native loaded-save hashes and
   verified successful save revisions in `BepInEx/config/CloudpunkCoop`; retain that
   directory when updating or backing up the mod. Saves imported with an unknown
   revision receive a separate identity. Revisions produced while the mod was absent
   and legacy inventory sidecars cannot yet be rebound automatically; preserve them.
   One guest equipment item has passed persistence across process restart; native
   use/buy/sell/reward acceptance remains incomplete.

Default `Role = Off` opens no co-op socket. `IndependentCars = true` selects individual vehicles.
Use `E` for co-op entry/exit and `Shift+E` for the other player's vehicle in independent-car mode.
`F8` requests/confirms seat exchange. These gameplay controls and the presence HUD remain
available with diagnostics disabled. Native game controls continue to apply.

## Beta defaults and developer testing

Leave `[Developer] EnableDiagnostics = false` and `[Automation] RunScriptedDemo = false`.
Both default to false; a configuration read failure leaves automation disabled.
Existing configs may retain earlier true values: explicitly review both sections.
`F9` opens the connection panel in ordinary play. `F1` debug panel, `F3/F4/F5/F7`
forced seat/demo actions, `F6` autopilot, `F2` driving
probe, `F10` visual probe, `F11` campaign inventory and `F12` world labels require diagnostics.
Probe output is local developer evidence, not proof that campaign replication works.

For a test process, `CLOUDPUNK_COOP_DIAGNOSTICS=true` enables diagnostics without editing
the shared config (`false` disables them). It alone does not enable scripted automation:
also opt in through `RunScriptedDemo = true` or `CLOUDPUNK_COOP_DEMO=true`.
The demo override still requires diagnostics and cannot enable automation after an automation
config read failure. Clear test environment variables before ordinary play. Existing role,
port, address and independent-car environment overrides also take precedence over config.

## Building a package (maintainers)

Run from the repository in PowerShell:

```powershell
.\tools\package-beta.ps1
```

This invokes the supported `tools/deploy.ps1` workflow, which builds and copies DLLs into
the development game installation. Close the game first. Packaging does not start the game.
The default install is `C:\GOG Games\Cloudpunk`; custom installs must already be supported
by the project's deploy/build configuration (`GameRoot` in the project references).
Passing `-GameRoot` forwards the same path to MSBuild references, interop preflight and
deployment. The deploy script refuses to build/copy while Cloudpunk is running.

To avoid any build/deploy, package an existing supported build:

```powershell
.\tools\package-beta.ps1 -UseExistingBuild -OutputPath C:\Temp\CloudpunkCoop-beta.zip
```

Both build DLLs must be present, newer than source inputs and SHA256-identical to the
deployed DLLs. This freshness/hash check does not certify runtime behavior or reproducibility.
Existing output archives are not overwritten. The script uses an explicit four-file allowlist:
the two mod DLLs, the config example and these notes. No game binaries, loader binaries,
saves, logs or diagnostic captures are distributed. Installation acceptance remains manual.

## Removal and recovery

Close both games, remove only `CloudpunkCoop.Plugin.dll` and `CloudpunkCoop.Protocol.dll`
from the installed mod locations, then restore your previous mod/config backup if needed.
Keep the separately installed loader and unrelated plugins. Restore save backups separately
if a test changed campaign state; removing the mod does not undo native save writes.

