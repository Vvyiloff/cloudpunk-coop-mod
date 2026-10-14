"""Query the Il2CppDumper dump of Cloudpunk by namespace / regex / size.

Examples:
  python list_types.py --global-only
  python list_types.py --ns UntitledGames
  python list_types.py --match "hover|vehicle"
  python list_types.py --match "dialogue|conversation" --fields
  python list_types.py --top 25
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

DUMP = Path(__file__).resolve().parents[1] / "analysis" / "dump" / "dump.cs"
NS_RE = re.compile(r"^// Namespace: ?(\S*)")
TYPE_RE = re.compile(r"^(?:public|private|protected|internal)\s+.*?\b(class|struct|interface|enum)\s+([\w.<>`]+)")


def load() -> list[tuple[str, str, str]]:
    """Return (namespace, kind, name) triples, skipping compiler-generated noise."""
    namespace = ""
    types: list[tuple[str, str, str]] = []
    with DUMP.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            ns_match = NS_RE.match(line)
            if ns_match:
                namespace = ns_match.group(1)
                continue
            type_match = TYPE_RE.match(line)
            if type_match:
                name = type_match.group(2)
                if "<" in name or "+" in name:
                    continue
                types.append((namespace, type_match.group(1), name))
    return types


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ns", help="filter by namespace (prefix match, case-insensitive)")
    parser.add_argument("--global-only", action="store_true", help="only types in the global namespace")
    parser.add_argument("--match", help="regex over the full type name")
    parser.add_argument("--exclude-lib", action="store_true", help="hide Unity/System/Steamworks/InControl noise")
    parser.add_argument("--top", type=int, help="sort by method count and show top N")
    parser.add_argument("--limit", type=int, default=200)
    args = parser.parse_args()

    if not DUMP.is_file():
        print(f"dump.cs not found: {DUMP}")
        return 2

    types = load()
    lib_roots = (
        "UnityEngine", "System", "Unity.", "Steamworks", "InControl", "TMPro", "Cinemachine",
        "SCPE", "Battlehub", "GPUInstancer", "Funly", "Mono", "Microsoft", "Ibuprogames",
        "EasyExcel", "Sirenix", "MEC", "uCurve", "Plawius", "MoodkieSecurity", "AOT",
    )

    selected = types
    if args.global_only:
        selected = [t for t in selected if t[0] == ""]
    if args.ns:
        needle = args.ns.lower()
        selected = [t for t in selected if t[0].lower().startswith(needle)]
    if args.match:
        rx = re.compile(args.match, re.IGNORECASE)
        selected = [t for t in selected if rx.search(f"{t[0]}.{t[2]}" if t[0] else t[2])]
    if args.exclude_lib:
        selected = [t for t in selected if not t[0].startswith(lib_roots)]

    if args.top:
        # method counts require a second pass over the file
        counts: Counter[str] = Counter()
        current = ""
        wanted = {f"{ns}.{name}" if ns else name for ns, _, name in selected}
        with DUMP.open("r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                ns_match = NS_RE.match(line)
                if ns_match:
                    namespace = ns_match.group(1)
                    current = ""
                    continue
                type_match = TYPE_RE.match(line)
                if type_match:
                    current = f"{namespace}.{type_match.group(2)}" if namespace else type_match.group(2)
                    continue
                if line.startswith("}"):
                    current = ""
                    continue
                if current in wanted and re.match(r"^\s+[\w].*\(", line) and "=>" not in line:
                    counts[current] += 1
        ranked = counts.most_common(args.top)
        print(f"{'methods':>8}  type")
        for name, count in ranked:
            print(f"{count:8d}  {name}")
        return 0

    shown = 0
    for namespace, kind, name in selected:
        full = f"{namespace}.{name}" if namespace else name
        print(f"[{kind[0]}] {full}")
        shown += 1
        if shown >= args.limit:
            remaining = len(selected) - shown
            if remaining > 0:
                print(f"... and {remaining} more (use --limit)")
            break

    if shown == 0:
        counts = Counter(t[0] or "<global>" for t in types)
        print("no match. Available namespaces (top 40):")
        for namespace, count in counts.most_common(40):
            print(f"  {count:5d}  {namespace}")
    else:
        print(f"\nmatched {len(selected)} types")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
