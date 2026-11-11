#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""目录平铺预算门禁：单一目录直接存放的代码文件不超过 5 个。

规则（规范 02 §1.12 / 09-分层与命名 §1.1）：
  F1 同一目录下"直接存放"的代码文件 > 5 即违规；代码按职责进子目录。
  F2 排除生成物（`Generated/`、`*.g.cs`、`*.Generated.cs`、`*.Designer.cs`）、随包第三方
     （`Adapters/kcp2k`）与 `obj`/`bin`。
  F3 存量按基线棘轮：已登记目录只许减不许增（`--update` 收紧，默认拒绝增长）。

用法：
  python scripts/gate/dir-flat-lint.py                # 对基线校验（本地/CI 默认）
  python scripts/gate/dir-flat-lint.py --all          # 全量普查（报告模式，不红）
  python scripts/gate/dir-flat-lint.py --all --strict # 全量普查且违规即红
  python scripts/gate/dir-flat-lint.py --update       # 基线收紧（只减不增）
  python scripts/gate/dir-flat-lint.py --json out.json

退出码：0 通过；1 违规。
"""
import argparse
import json
import os
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, info, repo_root  # noqa: E402

TOOL = "quality/dir-flat-lint"
THRESHOLD = 5
BASELINE_REL = Path("scripts") / "gate" / "dir-flat-baseline.json"
CODE_SUFFIXES = {".cs", ".proto", ".hlsl", ".shader", ".cginc", ".compute", ".asmdef", ".asmref"}
SCOPE_ROOTS = (
    "Assets/LiteGame",
    "Assets/LiteCodeGen",
    "Assets/LiteTesting",
    "Assets/Tests",
    "Packages/com.litegame.framework",
    "Packages/com.litegame.sim",
    "Packages/com.litegame.net",
    "Packages/com.litegame.sync",
)
SKIP_DIRS = {"obj", "bin", "__pycache__", "node_modules"}
EXCLUDE_DIRS = re.compile(r"/(Adapters/kcp2k|ThirdParty|Plugins|\.git)(/|$)", re.IGNORECASE)
GENERATED = re.compile(r"/Generated/|\.g\.cs$|\.Generated\.cs$|\.Designer\.cs$", re.IGNORECASE)


def load_next_roots(root: Path) -> list[str]:
    """读 arch/client-roots.json 的 Next 相关根：新架构已废除 AR2，Next 由 G2/G3 管。"""
    path = root / "arch" / "client-roots.json"
    if not path.is_file():
        return []
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError, TypeError):
        return []
    roots = [data.get("nextRoot"), data.get("frameworkNextRoot"), data.get("sharedRoot")]
    roots.extend(data.get("nextTests") or [])
    return [str(item).strip("/") for item in roots if item]


def scan_counts(root: Path) -> dict[str, int]:
    counts: dict[str, int] = {}
    next_roots = load_next_roots(root)
    for scope in SCOPE_ROOTS:
        base = root / scope
        if not base.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            rel_dir = Path(dirpath).relative_to(root).as_posix()
            if any(rel_dir == item or rel_dir.startswith(item + "/") for item in next_roots):
                dirnames[:] = []
                continue
            dirnames[:] = [d for d in dirnames
                           if d not in SKIP_DIRS and not EXCLUDE_DIRS.search(f"{rel_dir}/{d}/")]
            n = 0
            for name in filenames:
                if Path(name).suffix.lower() not in CODE_SUFFIXES:
                    continue
                if GENERATED.search(f"{rel_dir}/{name}"):
                    continue
                n += 1
            if n:
                counts[rel_dir] = n
    return counts


def load_baseline(root: Path) -> dict[str, int]:
    path = root / BASELINE_REL
    if not path.is_file():
        return {}
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return {str(k): int(v) for k, v in (data.get("dirs") or {}).items()}


def write_baseline(root: Path, counts: dict[str, int]) -> Path:
    path = root / BASELINE_REL
    payload = {
        "rule": f"同一目录直接存放的代码文件 <= {THRESHOLD}",
        "guard": "python scripts/gate/dir-flat-lint.py",
        "threshold": THRESHOLD,
        "dirs": {k: counts[k] for k in sorted(counts) if counts[k] > THRESHOLD},
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    return path


def check(root: Path, counts: dict[str, int], baseline: dict[str, int]):
    violations, resolved, stale = [], [], []
    for rel_dir in sorted(counts):
        n = counts[rel_dir]
        if n <= THRESHOLD:
            continue
        base = baseline.get(rel_dir)
        if base is None:
            violations.append((rel_dir, n, None))
        elif n > base:
            violations.append((rel_dir, n, base))
    for rel_dir, base in sorted(baseline.items()):
        if rel_dir not in counts:
            stale.append(rel_dir)
        elif counts[rel_dir] <= THRESHOLD:
            resolved.append((rel_dir, counts[rel_dir]))
    return violations, resolved, stale


def print_table(counts: dict[str, int], limit: int) -> None:
    over = sorted(((k, v) for k, v in counts.items() if v > THRESHOLD), key=lambda kv: (-kv[1], kv[0]))
    for rel_dir, n in over[:limit]:
        print(f"[{TOOL}]   {n:3d}  {rel_dir}")
    if len(over) > limit:
        print(f"[{TOOL}]   ... {len(over) - limit} more")


def main(argv=None) -> int:
    configure_stdio()
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=None)
    ap.add_argument("--all", action="store_true", help="全量普查（忽略基线，报告模式）")
    ap.add_argument("--strict", action="store_true", help="配合 --all：违规即红")
    ap.add_argument("--update", action="store_true", help="按当前树收紧基线（只减不增）")
    ap.add_argument("--allow-growth", action="store_true", help="配合 --update：允许基线增长")
    ap.add_argument("--shrink-only", action="store_true",
                    help="配合 --update：只登记已解决的目录，当前超基线的目录保持原值（继续红）")
    ap.add_argument("--json", default=None, help="违规清单落盘")
    ap.add_argument("--max-print", type=int, default=20)
    args = ap.parse_args(argv)

    root = Path(args.root).resolve() if args.root else repo_root()
    counts = scan_counts(root)
    baseline = load_baseline(root)

    if args.update:
        growth = [(k, counts[k], baseline[k]) for k in baseline
                  if k in counts and counts[k] > baseline[k]]
        if growth and not args.allow_growth and not args.shrink_only:
            for rel_dir, now, before in growth:
                print(f"[{TOOL}] REFUSE: {rel_dir} {before} -> {now}（--update 只减不增；确需放行加 --allow-growth）")
            print(f"[{TOOL}] FAILED: {len(growth)} 个目录基线增长，未写入")
            return EXIT_FAIL
        if args.shrink_only:
            merged = dict(counts)
            for rel_dir, now, before in growth:
                merged[rel_dir] = before
                print(f"[{TOOL}] KEEP: {rel_dir} 当前 {now} > 基线 {before}，保留原基线（仍红）")
            path = write_baseline(root, merged)
        else:
            path = write_baseline(root, counts)
        kept = len(json.loads(path.read_text(encoding="utf-8"))["dirs"])
        print(f"[{TOOL}] baseline updated: {path.relative_to(root).as_posix()} ({kept} dir(s) > {THRESHOLD})")
        return EXIT_OK

    violations, resolved, stale = check(root, counts, baseline)

    if args.all:
        print_table(counts, args.max_print)
        over = sum(1 for v in counts.values() if v > THRESHOLD)
        info(TOOL, f"{over} dir(s) > {THRESHOLD} flat code file(s), {len(counts)} dir(s) scanned")
        return EXIT_FAIL if (args.strict and over) else EXIT_OK

    if args.json:
        Path(args.json).write_text(
            json.dumps([{"dir": d, "count": n, "baseline": b} for d, n, b in violations],
                       ensure_ascii=False, indent=1),
            encoding="utf-8")

    for rel_dir, n, base in violations[:args.max_print]:
        if base is None:
            print(f"[{TOOL}] FAIL: {rel_dir} {n} > {THRESHOLD}（新增平铺目录）")
        else:
            print(f"[{TOOL}] FAIL: {rel_dir} {n} > {THRESHOLD}（基线 {base}，回涨 {n - base}）")
    if len(violations) > args.max_print:
        print(f"[{TOOL}] ... {len(violations) - args.max_print} more (use --json)")
    for rel_dir, n in resolved[:10]:
        print(f"[{TOOL}] INFO: {rel_dir} 已降到 {n}，可 --update 收紧基线")
    for rel_dir in stale[:10]:
        print(f"[{TOOL}] INFO: 基线条目已不存在: {rel_dir}（可 --update 清理）")

    if violations:
        by_area = Counter(v[0].split("/")[0] + "/" + v[0].split("/")[1] if "/" in v[0] else v[0]
                          for v in violations)
        for area, c in by_area.most_common(5):
            print(f"[{TOOL}]   {c:3d}  {area}")
        print(f"[{TOOL}] FAILED: {len(violations)} dir(s) exceed {THRESHOLD} flat code file(s)")
        return EXIT_FAIL

    info(TOOL, f"OK: {len(counts)} dir(s) scanned, {len(baseline)} baselined, no growth")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
