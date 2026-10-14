# Cooperative beta acceptance

User scope confirmed 2026-10-13: synchronized world, collisions, animation,
dialogue, quests and separate player inventories. This replaces the original
passenger-only MVP scope; historical HANDOFF exclusions do not limit this work.

## Required behavior

- Native main-menu Host / Join entry and F9 panel create/stop sessions, accept
  validated IPv4/TCP port, show connection failures and live peer presence.
  RoleOff must open no socket until requested. Both PCs on reachable LAN/VPN or
  public TCP must be tested; local adapter on one PC does not certify Internet
  traversal. Native UI input must not also control the car or underlying menu.

- Players may occupy and travel between different regions independently. Shared
  quests/world state persist across unloaded regions and authority handover;
  own-car and shared-car travel, cross-region events and reunion are native gates.
  See independent-regions.md; restoring local ambient AI is only a foundation.

- Two independently owned cars visible on both clients, exclusive driver seats,
  passenger boarding, remote driving, exit/reentry and mutual F8 exchange.
- Native collision hulls and occupied parking denial on both roles, including
  hiding the original landing caption. No simultaneous driver or bay grants.
- Ambient traffic, named NPCs, crowd and walking poses remain synchronized during
  driving, walking, disconnect/reconnect and supported scene transitions.
- One host executes campaign events and rewards. Both players see the same
  conversation and objectives. Guest requests are validated and deduplicated;
  reconnect never replays rewards or writes host campaign into a solo save.
- Separate inventories and wallets, authoritative personal transactions,
  shared mission progression. Quest rewards and quest-item policy are documented
  and verified without silently copying one player's personal spending.
- Default installation has no autopilot, scripted demo, debug overlay, probe
  keys or NPC diagnostic labels. Gameplay presence and controls stay enabled.
- A distributable package contains only mod artifacts, configuration example,
  installation/uninstallation instructions, tested game/loader requirements and
  an honest list of outstanding beta limitations.

## Evidence gates

Run protocol tests, supported deploy, native two-client scenarios and visually
inspect screenshots. Measure warm frame times during a representative drive.
Test dialogue completion and quest/item/money changes, duplicate requests,
late join and reconnect. All test saves must be restored and hash-checked.
Review changes and commit completed stages; do not mark the beta complete based
on packet counters, a build alone, or an executor report.

## Current work

World/car/animation work has prior native evidence in PROGRESS.md. Full parking
roundtrip fixture needs repair. Campaign authority and personal inventory are
the principal implementation blockers. Packaging and native campaign research
are running in disjoint scopes. Codex owns integration and final verification.
