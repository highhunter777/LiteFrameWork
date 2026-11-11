#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Next 子树的功能内聚/短链路预算（新架构 G2/G3/G4）。

只读，不依赖 Unity：
  - 每个 feature 目录必须有唯一 `<Name>Feature.cs`；
  - 功能目录内禁止 Abstractions/SubModules/Services/Impl；
  - Next 文本不得出现旧机制 token；
  - 统计 feature/page/prefab/code 文件，供触碰面证据使用。

用法：
  python scripts/gate/client-next-change-budget.py            # 文本报告
  python scripts/gate/client-next-change-budget.py --check    # 违规 exit 1
  python scripts/gate/client-next-change-budget.py --json out.json
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, repo_root  # noqa: E402

TOOL = "quality/client-next-budget"
BANNED_DIRS = {"Abstractions", "SubModules", "Services", "Impl", "Common", "Utils"}
LEGACY_TOKENS = (
    "PageIds", "UIPageCatalog", "UIBindIndex", "BindGenerator",
    "IClientSegment", "ServiceContainer", "PageServices", "ProcedureId",
)
PAGE_CLASS = re.compile(r"\bclass\s+([A-Za-z_]\w*)\s*:\s*PageBase\b")
TEXT_SUFFIXES = {".cs", ".asmdef", ".json", ".prefab", ".unity", ".asset", ".md"}


def load_roots(root: Path):
    path = root / "arch" / "client-roots.json"
    if not path.is_file():
        raise SystemExit(f"{TOOL}: arch/client-roots.json 未落地")
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    next_root = str(data["nextRoot"]).strip("/")
    next_runtime = str(data["nextRuntime"]).strip("/")
    return next_root, next_runtime


def iter_text(next_root: Path):
    for path in next_root.rglob("*"):
        if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES:
            try:
                yield path, path.read_text(encoding="utf-8-sig", errors="replace")
            except OSError:
                continue


def scan(root: Path):
    next_root_rel, next_runtime_rel = load_roots(root)
    next_root = root / next_root_rel
    next_runtime = root / next_runtime_rel
    problems = []
    features = []
    pages = []
    prefabs = []
    code_files = 0

    if not next_root.is_dir():
        problems.append(f"Next 子树不存在：{next_root_rel}")
    if not next_runtime.is_dir():
        problems.append(f"Next 运行时根不存在：{next_runtime_rel}")

    features_root = next_runtime / "Features"
    if features_root.is_dir():
        for feature in sorted(p for p in features_root.iterdir() if p.is_dir()):
            entry = feature / f"{feature.name}Feature.cs"
            if not entry.is_file():
                problems.append(f"功能入口缺失：{entry.relative_to(root).as_posix()}")
            for child in feature.rglob("*"):
                if child.is_dir() and child.name in BANNED_DIRS:
                    problems.append(
                        f"功能目录出现技术层子目录：{child.relative_to(root).as_posix()}")
            features.append(feature.name)

    if next_runtime.is_dir():
        for path in sorted(next_runtime.rglob("*.cs")):
            code_files += 1
            try:
                text = path.read_text(encoding="utf-8-sig", errors="replace")
            except OSError:
                continue
            for match in PAGE_CLASS.finditer(text):
                pages.append({"class": match.group(1),
                              "file": path.relative_to(root).as_posix()})
        for path in sorted(next_runtime.rglob("*.prefab")):
            prefabs.append(path.relative_to(root).as_posix())

    token_hits = []
    for path, text in iter_text(next_root):
        for token in LEGACY_TOKENS:
            if token in text:
                token_hits.append({"token": token,
                                   "file": path.relative_to(root).as_posix()})
    problems.extend(f"旧机制 token：{hit['token']} @ {hit['file']}" for hit in token_hits)

    return {
        "nextRoot": next_root_rel,
        "nextRuntime": next_runtime_rel,
        "features": features,
        "pages": pages,
        "prefabs": prefabs,
        "codeFiles": code_files,
        "tokenHits": token_hits,
        "problems": problems,
    }


def main(argv=None) -> int:
    configure_stdio()
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="有违规 exit 1")
    ap.add_argument("--json", default=None, help="报告落盘路径")
    args = ap.parse_args(argv)

    report = scan(repo_root())
    if args.json:
        Path(args.json).write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8", newline="\n")
    print(f"[{TOOL}] root={report['nextRoot']} features={len(report['features'])} "
          f"pages={len(report['pages'])} prefabs={len(report['prefabs'])} "
          f"code={report['codeFiles']} problems={len(report['problems'])}")
    for problem in report["problems"]:
        print(f"[{TOOL}] FAIL: {problem}")
    if report["problems"] and args.check:
        return EXIT_FAIL
    return EXIT_OK


if __name__ == "__main__":
    raise SystemExit(main())
