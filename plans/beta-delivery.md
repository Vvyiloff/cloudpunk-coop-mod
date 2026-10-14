# Full beta delivery target

Requested deadline: before 2027-01-01, recorded 2026-10-14.
Acceptance authority: `beta-acceptance.md` and `independent-regions.md`.
These are engineering targets, not proof of completion or a guaranteed schedule.

| Target date | Deliverable | Required evidence |
|-------------|-------------|-------------------|
| 2026-10-31 | Personal inventory/use/trade and shared campaign reward correctness | Native use/equip/buy/sell, duplicate requests, persistence, reconnect, real rewarded dialogue and quest transition |
| 2026-11-15 | Regional authored event catalog and single host campaign execution | Audited event closures, unloaded-region retained state, guest interaction while host is elsewhere, reward exactly once |
| 2026-11-30 | Full regional mobility and world handover | Both travel directions, shared-car lift travel, parked car region retention, reunited NPC/traffic/collider state without rewinds |
| 2026-12-15 | Complete functional and network acceptance | Parking roundtrip, collision/driving/walking/animation regressions, two PCs over reachable LAN/VPN/TCP, menu/input isolation, long session and save preservation |
| 2026-12-20 | Beta feature freeze and independent review | All required gates evidenced, defects triaged, default diagnostics off, fresh installation/uninstallation check |
| 2026-12-30 | Reviewed distributable full beta | Mod-only archive, exact build/loader requirements, installation notes, hashes, reviewed commits and honest limitations |

## Current critical path

The recovered consumable integration is being repaired and independently verified.
After native use/restart/reconnect acceptance, the next feature blocker is a trusted
regional authored event catalog and execution closure. The regional design is in
`../docs/retro/beta-recovery/regional-story-design.md`; it remains proposed and
does not certify any remote rewarded NPC yet.

Remaining native gates are not waived by a green protocol suite or packaging.
Two-PC acceptance requires an actual second game client; same-PC fixtures do not
prove that gate. No periodic/background execution or scheduled continuation has
been configured by this document.
