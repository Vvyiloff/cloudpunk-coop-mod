"""Extract the full declaration block (fields + method signatures) of a type from dump.cs.

Usage:
  python extract_class.py Player Vehicle PlayerCar
  python extract_class.py --ns-global --match "^Dialog" --limit 5
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

DUMP = Path(__file__).resolve().parents[1] / "analysis" / "dump" / "dump.cs"
NS_RE = re.compile(r"^// Namespace: ?(\S*)")
DECL_RE = re.compile(r"^(?P<full>(?:public|private|protected|internal)\s+.*?\b(class|struct|interface|enum)\s+[\w.<>`]+.*)$")


def iter_blocks(path: Path):
    namespace = ""
    current_name = None
    current_header = ""
    depth = 0
    buffer: list[str] = []
    for line in path.open("r", encoding="utf-8", errors="replace"):
        ns_match = NS_RE.match(line)
        if ns_match:
            namespace = ns_match.group(1)
            continue
        if current_name is None:
            decl = DECL_RE.match(line)
            if decl:
                current_header = line.rstrip()
                name_match = re.search(r"\b(?:class|struct|interface|enum)\s+([\w.<>`]+)", line)
                current_name = name_match.group(1) if name_match else ""
                depth = 0
                buffer = []
            continue
        buffer.append(line.rstrip())
        depth += line.count("{") - line.count("}")
        if depth <= 0 and line.startswith("}"):
            yield namespace, current_name, current_header, buffer
            current_name = None
            buffer = []
    if current_name is not None:
        yield namespace, current_name, current_header, buffer


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("names", nargs="*", help="exact type names to print")
    parser.add_argument("--match", help="regex over type name")
    parser.add_argument("--global-only", action="store_true", help="only the global namespace (the game's own code)")
    parser.add_argument("--signatures-only", action="store_true", help="hide // RVA / offset comments")
    parser.add_argument("--limit", type=int, default=10)
    args = parser.parse_args()

    if not DUMP.is_file():
        print(f"dump.cs not found: {DUMP}")
        return 2

    wanted = set(args.names)
    rx = re.compile(args.match) if args.match else None
    shown = 0

    for namespace, name, header, body in iter_blocks(DUMP):
        if args.global_only and namespace != "":
            continue
        if wanted and name not in wanted:
            continue
        if rx and not rx.search(name):
            continue
        shown += 1
        if shown > args.limit:
            print(f"... (stopped at --limit {args.limit}, use --match for a narrower query)")
            break
        prefix = f"// Namespace: {namespace}\n" if namespace else "// Namespace: <global>\n"
        print(prefix + header)
        for line in body:
            if args.signatures_only:
                line = re.sub(r"\s*//.*$", "", line)
            if line.strip():
                print(line)
        print()
    if shown == 0:
        print("no match")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
