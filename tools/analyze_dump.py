"""Build an architecture map of an IL2CPP game from Il2CppDumper output.

Reads dump.cs plus the DummyDll folder and reports:
  - size of the codebase (types, methods, fields, strings)
  - assembly / namespace breakdown
  - co-op relevant subsystem clusters with the concrete classes behind them

Usage:
  python analyze_dump.py <analysis_dir> [report.md]
"""

from __future__ import annotations

import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

TYPE_RE = re.compile(
    r"^(?P<mods>(?:public|private|protected|internal|static|sealed|abstract|partial|unsafe|readonly|sealed)\s+)*"
    r"(?P<kind>class|struct|interface|enum)\s+(?P<name>[\w\.<>`]+)",
)
METHOD_RE = re.compile(r"^\s+(?:public|private|protected|internal|static|virtual|override|abstract|sealed|extern|unsafe|async|\s)*[\w\.<>\[\],\s\?]+\s+[\w`]+(?P<name>[\w`]*)\s*\(")
FIELD_RE = re.compile(r"^\s+(?:public|private|protected|internal|static|readonly|const|volatile|unsafe|\s)*[\w\.<>\[\],\s\?]+\s+(?P<name>[\w`]+)\s*[;={]")
# NOTE: Il2CppDumper writes "// Namespace: " with nothing after it for the global
# namespace, which is where this game keeps most of its gameplay code. An earlier
# \S+ version silently merged those types into the previous namespace.
NAMESPACE_RE = re.compile(r"^// Namespace: ?(?P<ns>\S*)")

# Subsystems we care about for an online co-op mod.
# (cluster name, regex over class name, why it matters)
CLUSTERS: list[tuple[str, str, str]] = [
    ("Input", r"InControl|InputHandler|InputManager|PlayerInput|ControlScheme|InputAction", "how a second player could be added"),
    ("Vehicle / hover physics", r"Hover|Vehicle|Car\b|Driving|Drive\b|Thruster|Tractor|Wheel|Newton|Hovercraft", "what must be synchronised over the network"),
    ("Player / character", r"PlayerController|PlayerCharacter|\bPlayer\b|Rania|Humanoid|Avatar", "the pawn a second player needs"),
    ("Movement / camera", r"CameraRig|CameraController|ThirdPerson|OrbitCamera|FollowCam|PlayerCamera", "second player needs its own camera"),
    ("Story / dialogue", r"Dialogue|Conversation|\bStory\b|Choice|Quest|Mission|Cutscene|Cinematic|Subtitle|Voicenarrator|Narrative", "why full campaign co-op is hard"),
    ("Save / persistence", r"SaveGame|SaveData|SaveSystem|SaveSlot|Persistence|Serializer|SaveManager", "single-player assumptions to break"),
    ("World / city streaming", r"Nivalis|District|Streaming|WorldPartition|SceneManager|SceneLoader|CitySector|Chunks", "whether clients can load the world independently"),
    ("UI / HUD", r"\bHUD\b|Canvas|Minimap|Waypoint|Objective|Toast|HudRoot|UserInterface|UIDirector", "per-player UI"),
    ("Economy / delivery", r"Holocash|Currency|Delivery|Package|Cargo|Manifest|Vendor|Trade|Inventory|Item\b|Wallet|Price", "shared or per-player progression"),
    ("Networking (any)", r"\bPhoton\b|Mirror|NetworkBehaviour|NetworkManager|\bSocket\b|SteamNet|OnlineSubsystem|\bUNet\b|Netcode|LitNet|Lobby", "existing netcode to reuse"),
    ("Audio / voice", r"AudioSource|AudioManager|MusicController|VoiceOver|VoiceBank|SoundManager", "two players sharing one audio listener"),
    ("Debug / console", r"\bConsole\b|DebugMenu|Cheat|DeveloperMode|ToggleDebug|CommandProcessor", "existing hooks the mod can reuse"),
    ("Localization", r"Localization|Localiz|LanguageManager|LocalizedString", "multi-language safety"),
]

# Libraries we expect to see compiled into the game.
KNOWN_LIBS = {
    "InControl": "input abstraction layer",
    "Steamworks": "Steamworks.NET bindings",
    "TMPro": "TextMeshPro UI",
    "Newtonsoft": "Json.NET",
    "UnityEngine.Networking": "Unity networking",
    "Photon": "Photon netcode",
    "FishNet": "FishNet netcode",
    "Mirror": "Mirror netcode",
    "Unity.Networking": "Unity NGO",
    "Sirenix": "Odin Inspector",
    "Rewired": "Rewired input",
}


def parse_dump(path: Path) -> dict:
    namespace = ""
    types: list[tuple[str, str, str]] = []  # (namespace, kind, name)
    ns_counts: Counter[str] = Counter()
    kind_counts: Counter[str] = Counter()
    methods_per_type: dict[str, int] = defaultdict(int)
    current_type = ""
    in_body = False
    method_count = 0
    field_count = 0

    with path.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            ns_match = NAMESPACE_RE.match(line)
            if ns_match:
                namespace = ns_match.group("ns")
                in_body = False
                continue

            if not in_body:
                t_match = TYPE_RE.match(line)
                if t_match:
                    name = t_match.group("name")
                    kind = t_match.group("kind")
                    full = f"{namespace}.{name}" if namespace else name
                    types.append((namespace, kind, full))
                    ns_counts[namespace] += 1
                    kind_counts[kind] += 1
                    current_type = full
                    in_body = True
                continue

            if line.startswith("}"):
                in_body = False
                continue

            if current_type:
                if METHOD_RE.match(line):
                    methods_per_type[current_type] += 1
                    method_count += 1
                elif FIELD_RE.match(line) and "=>" not in line and "(" not in line:
                    field_count += 1

    return {
        "types": types,
        "ns_counts": ns_counts,
        "kind_counts": kind_counts,
        "methods_per_type": methods_per_type,
        "method_count": method_count,
        "field_count": field_count,
    }


def top_namespace(ns_counts: Counter[str], limit: int = 45) -> list[tuple[str, int]]:
    return ns_counts.most_common(limit)


def assembly_of(namespace: str) -> str:
    """Root namespace usually maps to the compiled assembly name."""
    return namespace.split(".")[0] if namespace else "<none>"


def find_clusters(types: list[tuple[str, str, str]]) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for cluster, pattern, why in CLUSTERS:
        rx = re.compile(pattern)
        hits = [full for _, _, full in types if rx.search(full)]
        # drop pure noise: interfaces and compiler-generated noise
        hits = [h for h in hits if "<" not in h]
        result[cluster] = {"why": why, "count": len(hits), "classes": hits}
    return result


def library_scan(types: list[tuple[str, str, str]]) -> list[str]:
    found = []
    for lib, desc in KNOWN_LIBS.items():
        matches = [f for ns, _, f in types if ns.split(".")[0] == lib or f.startswith(lib + ".")]
        if matches:
            found.append(f"- **{lib}** — {desc}: {len(matches)} types")
    return found


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    analysis_dir = Path(sys.argv[1])
    report_path = Path(sys.argv[2]) if len(sys.argv) > 2 else analysis_dir / "architecture.md"
    dump = analysis_dir / "dump.cs"
    if not dump.is_file():
        print(f"dump.cs not found: {dump}")
        return 2

    data = parse_dump(dump)
    types = data["types"]

    assemblies: Counter[str] = Counter()
    for ns, _, _ in types:
        assemblies[assembly_of(ns)] += 1

    clusters = find_clusters(types)

    strings_file = analysis_dir / "stringliteral.json"
    string_count = 0
    if strings_file.is_file():
        try:
            string_count = len(json.loads(strings_file.read_text(encoding="utf-8", errors="replace")))
        except json.JSONDecodeError:
            string_count = -1

    lines: list[str] = []
    add = lines.append
    add("# Cloudpunk — architecture map from IL2CPP metadata")
    add("")
    add("Generated by `tools/analyze_dump.py` from Il2CppDumper output. Read-only analysis: "
        "no game file was modified.")
    add("")
    add("## Size")
    add("")
    add("| Metric | Value |")
    add("| --- | --- |")
    add(f"| Types | {len(types)} |")
    add(f"| Classes | {data['kind_counts'].get('class', 0)} |")
    add(f"| Structs | {data['kind_counts'].get('struct', 0)} |")
    add(f"| Enums | {data['kind_counts'].get('enum', 0)} |")
    add(f"| Interfaces | {data['kind_counts'].get('interface', 0)} |")
    add(f"| Methods | {data['method_count']} |")
    add(f"| Fields | {data['field_count']} |")
    add(f"| String literals | {string_count} |")
    add("")
    add("## Compiled-in libraries detected")
    add("")
    libs = library_scan(types)
    add("\n".join(libs) if libs else "- none of the expected libraries were found by namespace")
    add("")
    add("## Largest namespaces")
    add("")
    add("| Namespace | Types |")
    add("| --- | --- |")
    for ns, count in top_namespace(data["ns_counts"]):
        add(f"| `{ns}` | {count} |")
    add("")
    add("## Subsystem clusters relevant to co-op")
    add("")
    for cluster, info in clusters.items():
        add(f"### {cluster} — {info['count']} classes")
        add("")
        add(f"*Why it matters:* {info['why']}")
        add("")
        for cls in info["classes"][:40]:
            methods = data["methods_per_type"].get(cls, 0)
            add(f"- `{cls}` ({methods} methods)")
        if info["count"] > 40:
            add(f"- ...and {info['count'] - 40} more")
        add("")

    report_path.write_text("\n".join(lines), encoding="utf-8")
    machine = {
        "type_count": len(types),
        "kind_counts": dict(data["kind_counts"]),
        "method_count": data["method_count"],
        "field_count": data["field_count"],
        "string_count": string_count,
        "assemblies": assemblies.most_common(),
        "clusters": {k: {"count": v["count"], "classes": v["classes"]} for k, v in clusters.items()},
    }
    (analysis_dir / "architecture.json").write_text(json.dumps(machine, indent=2), encoding="utf-8")
    print(f"types={len(types)} methods={data['method_count']} fields={data['field_count']}")
    print(f"report -> {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())