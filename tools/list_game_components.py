"""List the game's own types (global namespace) with their base types.

The BCL and Mono runtime are compiled into this build too, so the global
namespace mixes game code with hundreds of framework types. Game code is
identified by deriving from a Unity / plugin base class, or by being an enum
with a game-ish name.

Usage:
  python list_game_components.py                 # all MonoBehaviour-derived, grouped
  python list_game_components.py --enums         # global enums
  python list_game_components.py --match dialog  # filter by regex
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

DUMP = Path(__file__).resolve().parents[1] / "analysis" / "dump" / "dump.cs"
NS_RE = re.compile(r"^// Namespace: ?(\S*)")
DECL_RE = re.compile(r"^(?:public|private|protected|internal)\s+.*?\b(class|struct|interface|enum)\s+([\w.<>`]+)(?:\s*:\s*([\w.<>`,\s]+?))?\s*(?://.*)?$")

# Base types that mean "this is game or asset code, not the BCL".
# Il2CppDumper prints base types unqualified ("MonoBehaviour", not
# "UnityEngine.MonoBehaviour"), so both forms must be accepted.
GAME_BASE_PREFIXES = (
    "Cinemachine", "TMPro", "UnityEngine.UI", "MEC", "WhiteCat",
    "Minigame", "SSFS", "FXV", "LPWAsset", "Battlehub", "RedAlliance", "EasyExcel",
    "InControl", "SCPE", "GPUInstancer", "Funly",
)
GAME_BASE_SHORT = {
    "MonoBehaviour", "ScriptableObject", "EditorWindow", "Editor", "Behaviour",
    "StateBehaviour", "CinemachineVirtualCameraBase", "CinemachineExtension",
    "TextMeshProUGUI", "VisualTreeAsset", "ScriptableComponent", "GlobalComponent",
}


def is_game_base(base: str) -> bool:
    if not base:
        return False
    first = base.split(",")[0].strip().split(".")[-1]
    if first in GAME_BASE_SHORT:
        return True
    return base.startswith(GAME_BASE_PREFIXES)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--enums", action="store_true", help="list global enums instead")
    parser.add_argument("--match", help="regex filter on type name")
    parser.add_argument("--limit", type=int, default=100000)
    args = parser.parse_args()

    namespace = ""
    entries: list[tuple[str, str, str]] = []  # (kind, name, base)
    with DUMP.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            ns_match = NS_RE.match(line)
            if ns_match:
                namespace = ns_match.group(1)
                continue
            decl = DECL_RE.match(line)
            if not decl:
                continue
            kind, name, base = decl.group(1), decl.group(2), (decl.group(3) or "").strip()
            if namespace != "" or "<" in name:
                continue
            if args.enums:
                if kind == "enum":
                    entries.append((kind, name, base))
            else:
                if kind in ("class", "struct") and is_game_base(base):
                    entries.append((kind, name, base))

    if args.match:
        rx = re.compile(args.match, re.IGNORECASE)
        entries = [e for e in entries if rx.search(e[1])]

    if args.enums:
        for _, name, _ in entries[: args.limit]:
            print(f"[enum] {name}")
        print(f"\nglobal enums: {len(entries)}")
        return 0

    grouped: dict[str, list[str]] = defaultdict(list)
    for _, name, base in entries:
        grouped[base].append(name)

    print(f"game components (global namespace, derived from game/plugin bases): {len(entries)}\n")
    for base in sorted(grouped, key=lambda b: -len(grouped[b])):
        print(f"--- derives from {base}  ({len(grouped[base])})")
        for name in sorted(grouped[base]):
            print(f"    {name}")
        print()

    print("base-class histogram:")
    for base, count in Counter(e[2] for e in entries).most_common(25):
        print(f"  {count:4d}  {base}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
