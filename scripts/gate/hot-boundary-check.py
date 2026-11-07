#!/usr/bin/env python3
"""H2 guard: player AOT assemblies must not reference hot-update assemblies."""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

TOOL = "quality/hot-boundary"
SETTINGS = "ProjectSettings/HybridCLRSettings.asset"
EXCLUDED_ROOTS = (
    "Assets/ThirdParty/",
    "Packages/com.tuyoogame.yooasset/",
    "Packages/com.code-philosophy.luban/",
    "Packages/com.code-philosophy.hybridclr/",
    "Packages/com.unity.pipeline/",
    "Packages/MCPForUnity/",
)


def hot_assemblies(project: Path) -> list:
    path = project / SETTINGS
    if not path.is_file():
        return []
    names, collecting = [], False
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if line.startswith("hotUpdateAssemblies:"):
            collecting = True
            continue
        if collecting:
            if line.startswith("- "):
                names.append(line[2:].strip())
            elif line and not line.startswith("-"):
                break
    return names


def load_asmdefs(project: Path) -> list:
    out = []
    for path in project.rglob("*.asmdef"):
        rel = path.relative_to(project).as_posix()
        if any(rel.startswith(prefix) for prefix in EXCLUDED_ROOTS):
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError) as exc:
            out.append({"path": rel, "name": "", "references": [], "error": str(exc)})
            continue
        out.append({
            "path": rel,
            "name": data.get("name", ""),
            "references": data.get("references", []),
            "includePlatforms": data.get("includePlatforms", []),
        })
    return out


def is_editor_only(entry) -> bool:
    platforms = entry.get("includePlatforms") or []
    return platforms == ["Editor"]


def is_test(entry) -> bool:
    path = entry.get("path", "")
    return "Tests" in path or entry.get("name", "").endswith("Tests")


def scan(project: Path):
    hot = set(hot_assemblies(project))
    if not hot:
        return [], hot, 0
    violations, aot_count = [], 0
    for entry in load_asmdefs(project):
        if entry.get("error"):
            violations.append(f"{entry['path']}: asmdef 解析失败：{entry['error']}")
            continue
        if entry["name"] in hot or is_editor_only(entry) or is_test(entry):
            continue
        aot_count += 1
        for reference in entry["references"]:
            if reference in hot:
                violations.append(f"{entry['path']} ({entry['name']}) -> {reference}")
    return violations, hot, aot_count


def main(argv=None) -> int:
    configure_stdio()
    parser = argparse.ArgumentParser(description="AOT must not reference hot-update assemblies (H2)")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()

    violations, hot, aot_count = scan(project)
    if not hot:
        info(TOOL, f"{SETTINGS} 无 hotUpdateAssemblies —— 跳过（P2 前）")
        return EXIT_OK
    if args.json:
        print(json.dumps({"hot": sorted(hot), "aot": aot_count, "violations": violations}, ensure_ascii=False))
    if violations:
        for item in violations:
            print(f"  {item}")
        return fail(TOOL, f"{len(violations)} 条 AOT→hot 引用（H2 违规）")
    info(TOOL, f"OK: hot={len(hot)} aot={aot_count}，无 AOT→hot 引用")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
