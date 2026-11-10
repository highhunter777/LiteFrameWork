#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""T6 冷启动子代理探针：只做两件事——生成 brief、量一次施工的触碰面。

用途（客户端架构重构演进路线图 §5.2 / 规范 02 §1.13-1.14 的验收代理）：
  --domain <目录>      打印一段可直接粘贴给「全新上下文子代理」的 brief：
                       域目录、AR1/AR2 对该域的报错摘要、一句占位需求，
                       并要求它只报告「触碰了哪些文件 / 是否需要追问」。
  --measure <a> <b>    跑 `git diff --name-only` 与 `git diff --numstat`，
                       报告区间内触碰的文件数与清单（<=2 文件 = 达标）。

⚠ 「零反问」无法由脚本判定：反问发生在子代理与主代理的对话里，脚本只看得到
  最终 diff。该项必须由人工/主代理记录（见设计文档 §5.2 的 T6 表格）。

用法：
  python scripts/arch/coldstart-probe.py --domain Assets/LiteGame/Modules/UI
  python scripts/arch/coldstart-probe.py --measure HEAD~1 HEAD
  python scripts/arch/coldstart-probe.py --domain <dir> --copy   # 写 brief 到 .dsh-artifacts

退出码：0 成功（--measure 默认不因超限而红）；1 参数/路径/git 错误，
        或 --strict 下触碰文件数超限。
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import configure_stdio, fail, info  # noqa: E402

TOOL = "quality/coldstart-probe"
MAX_FILES = 2
BRIEF_HEAD = "----- 8< ----- brief begin ----- 8< -----"
BRIEF_TAIL = "----- 8< -----  brief end  ----- 8< -----"


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def run(cmd, cwd):
    return subprocess.run(cmd, cwd=str(cwd), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")


def guard_summary(root: Path, script: Path, rel_domain: str, kind: str):
    """跑 AR1/AR2 的 --all --scope --json，抽摘要行 + 前几条具体报错文本。"""
    if not script.is_file():
        return [f"（守卫缺失：{script.name}）"]
    lines = []
    with tempfile.TemporaryDirectory() as temp_dir:
        json_path = Path(temp_dir) / "items.json"
        result = run([sys.executable, str(script), "--all", "--scope", rel_domain,
                      "--json", str(json_path)], root)
        for raw in (result.stdout + result.stderr).splitlines():
            text = raw.strip()
            if text.startswith("[quality/") and any(
                    key in text for key in ("scanned", "units=", "shallow-ns", "report-only")):
                lines.append(text)
        try:
            items = json.loads(json_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            items = []
    for item in items[:6]:
        if kind == "ar1":
            if item.get("kind") == "namespace":
                lines.append(f"FAIL: {item['file']} 期望 {item['expected']} "
                             f"实际 {item['actual']}")
            elif item.get("kind") == "no-namespace":
                lines.append(f"FAIL: {item['file']} 无 namespace（期望 {item['expected']}）")
            elif item.get("kind") == "dir-name":
                lines.append(f"FAIL: {item['file']} 目录名 {item['actual']} 属技术层"
                             "（应并回单元）")
            else:
                lines.append(f"FAIL: {item['file']} {item.get('actual', '')}")
        else:
            lines.append(f"FAIL: {item['unit']} ({','.join(item['violations'])}) "
                         f"files={item['files']} lines={item['lines']} "
                         f"entry={item['entry']}")
    if len(items) > 6:
        lines.append(f"... 共 {len(items)} 条（仅列前 6 条）")
    return lines[:16]


def dir_tree(root: Path, domain: Path, max_depth: int, max_lines: int):
    base_rel = domain.relative_to(root)
    lines = [base_rel.as_posix() + "/"]
    for dirpath, dirnames, filenames in os.walk(domain):
        rel = Path(dirpath).relative_to(domain)
        depth = 0 if str(rel) == "." else len(rel.parts)
        if depth >= max_depth:
            dirnames[:] = []
            continue
        dirnames[:] = sorted(d for d in dirnames
                             if d not in {"obj", "bin", "__pycache__", ".vs", ".dotnet"}
                             and not d.startswith("."))
        for name in sorted(filenames):
            if name.endswith((".cs", ".asmdef")):
                lines.append("  " * (depth + 1) + name)
        for name in dirnames:
            lines.append("  " * (depth + 1) + name + "/")
        if len(lines) >= max_lines:
            lines.append("  ...（目录树截断）")
            break
    return lines


def emit_brief(root: Path, domain: Path, max_depth: int):
    rel = domain.relative_to(root).as_posix()
    ar1 = guard_summary(root, root / "scripts" / "gate" / "namespace-path.py", rel, "ar1")
    ar2 = guard_summary(root, root / "scripts" / "gate" / "change-unit.py", rel, "ar2")
    head = run(["git", "rev-parse", "--short", "HEAD"], root)
    stamp = head.stdout.strip() or "unknown"
    brief = [
        "你是一个全新上下文的施工子代理，没读过这个仓库的任何文档。",
        "",
        f"## 目标域\n{rel}/（git {stamp}）",
        "",
        "## 目录（" + str(max_depth) + " 层）",
        *dir_tree(root, domain, max_depth, 60),
        "",
        "## 规则（AR1 路径=命名空间 / AR2 改动单元上限）",
        "AR1：代码目录路径必须与命名空间逐段相等；Abstractions/SubModules/Impl/Interfaces 计入违规，"
        "Services/Core/Common/Utils 只报不计（存量）。",
        "AR2：一个目录（=一个命名空间）≤5 个 .cs、总行 ≤600、入口文件唯一且与目录同名（`<目录>/<目录名>.cs`）。",
        "",
        "### AR1 对该域的现状报错摘要",
        *ar1,
        "",
        "### AR2 对该域的现状报错摘要",
        *ar2,
        "",
        "## 需求（占位——主代理替换成一句话）",
        "在这个域里完成：<一句话需求，例如“把 X 按钮的处理函数改名并保持行为不变”>。",
        "",
        "## 交付（只报告，不要解释方案）",
        "1. 你触碰了哪些文件：完整仓库相对路径列表（没有改动就写“无”）。",
        "2. 是否需要追问：是/否；若是，用一句话说明缺什么信息。",
        "",
        "约束：不要读 Docs/，不要跑生成器或构建，不要修改本 brief 之外的任何文件。",
    ]
    print(BRIEF_HEAD)
    print("\n".join(brief))
    print(BRIEF_TAIL)


def measure(root: Path, ref_a: str, ref_b: str):
    # core.quotepath=false：中文路径/文件名不转义成八进制，清单可直接粘贴
    names = run(["git", "-c", "core.quotepath=false", "diff", "--name-only", ref_a, ref_b], root)
    stat = run(["git", "-c", "core.quotepath=false", "diff", "--numstat", ref_a, ref_b], root)
    if names.returncode != 0:
        return None, (names.stderr or names.stdout).strip()
    if stat.returncode != 0:
        return None, (stat.stderr or stat.stdout).strip()
    files = [line.strip().replace("\\", "/") for line in names.stdout.splitlines() if line.strip()]
    stats = {}
    for line in stat.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) == 3:
            stats[parts[2].strip().replace("\\", "/")] = (parts[0], parts[1])
    return (files, stats), None


def main(argv=None) -> int:
    configure_stdio()
    ap = argparse.ArgumentParser(
        formatter_class=argparse.RawDescriptionHelpFormatter,
        description="T6 冷启动子代理探针：生成 brief / 量一次施工的触碰面",
        epilog=(
            "说明：\n"
            "  - 本探针只量化两件可测事实：brief 是否够用（由子代理对照）、一次施工触碰了多少文件。\n"
            "  - 「零反问」无法由脚本判定——反问只发生在子代理与主代理的对话里；\n"
            "    必须由人工/主代理记录到 T6 验收表，脚本只负责其余可测部分。\n"
            "  - --measure 的达标线：触碰文件数 <= 2。"
        ))
    ap.add_argument("--domain", default=None, help="目标域目录（仓库相对或绝对）")
    ap.add_argument("--measure", nargs=2, metavar=("REF_A", "REF_B"),
                    help="量 git diff REF_A REF_B 的触碰文件数")
    ap.add_argument("--tree-depth", type=int, default=2, help="brief 里目录树深度（默认 2）")
    ap.add_argument("--max-files", type=int, default=MAX_FILES,
                    help=f"--measure 达标线（默认 {MAX_FILES}）")
    ap.add_argument("--strict", action="store_true", help="--measure 超限时退出 1")
    args = ap.parse_args(argv)

    if bool(args.domain) == bool(args.measure):
        ap.error("--domain 与 --measure 二选一（必须给且只给一个）")

    root = repo_root()

    if args.domain:
        domain = Path(args.domain)
        if not domain.is_absolute():
            domain = root / domain
        domain = domain.resolve()
        if not domain.is_dir():
            return fail(TOOL, f"域目录不存在：{args.domain}")
        if root not in domain.parents and domain != root:
            return fail(TOOL, f"域不在仓库内：{domain}")
        info(TOOL, f"domain={domain.relative_to(root).as_posix()} "
                   f"depth={args.tree_depth}（brief 可直接粘贴给全新上下文子代理）")
        emit_brief(root, domain, args.tree_depth)
        return 0

    ref_a, ref_b = args.measure
    result, error = measure(root, ref_a, ref_b)
    if result is None:
        return fail(TOOL, f"git diff {ref_a} {ref_b} 失败：{error}")
    files, stats = result
    code_files = [f for f in files if f.endswith((".cs", ".asmdef"))]
    info(TOOL, f"diff {ref_a}..{ref_b}: {len(files)} 个文件（代码 {len(code_files)} 个）")
    for name in files:
        added, deleted = stats.get(name, ("?", "?"))
        print(f"[{TOOL}]   +{added:<6} -{deleted:<6} {name}")
    verdict = "PASS 达标" if len(files) <= args.max_files else "FAIL 未达标"
    print(f"[{TOOL}] {verdict}: 触碰 {len(files)} 个文件（达标线 <= {args.max_files}）")
    if len(files) > args.max_files and args.strict:
        return fail(TOOL, f"触碰 {len(files)} 个文件 > {args.max_files}")
    print(f"[{TOOL}] 注意：本结果只覆盖 diff；「零反问」须人工/主代理记录，脚本无法判定")
    return 0


if __name__ == "__main__":
    sys.exit(main())
