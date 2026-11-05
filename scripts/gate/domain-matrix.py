#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""域间引用矩阵门禁。

统计 Assets/LiteGame/Modules/<域>/**/*.cs 中"源域代码引用目标域顶层类型名"的次数，
生成 Docs/说明/18-域间引用矩阵.md；--check 复算并比对（过期即红），供 .workflow/config.json
的 domain-matrix 命令使用。

统计口径（与文档同文）：
- 只统计 .cs 的**代码区**：去掉 //、///、/* */ 注释与字符串字面量；
- 只统计**顶层类型声明**（namespace 块内 depth=1 的 class/struct/interface/enum/delegate），
  嵌套类型（如 LuaKeys.UI）不单列；
- 计数单位 = 引用**出现次数**（同一行多次出现分别计）；源域=目标域不计（对角线 -）；
- 这是"域 → 域"的类型直引面；端口/契约仍落在目标域（计数>0 不等于违规，允许边见文档）。
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402

ROOT = repo_root()
MODULES = ROOT / "Assets" / "LiteGame" / "Modules"
DOC = ROOT / "Docs" / "说明" / "18-域间引用矩阵.md"

ARCH_MODEL = ROOT / "arch" / "units.json"


def load_arch_model():
    """域清单/角色/允许边全部来自架构单元模型（B5 单源）。"""
    import json

    data = json.loads(ARCH_MODEL.read_text(encoding="utf-8-sig"))
    domains = [item["id"] for item in data["domains"]]
    roles = {item["id"]: item["role"] for item in data["domains"]}
    edges = {(item["from"], item["to"]): item["reason"] for item in data["domainEdges"]}
    return domains, roles, edges


DOMAIN_ORDER, DOMAIN_ROLES, ALLOWED_EDGES = load_arch_model()

DECL_RE = re.compile(r"\b(?:class|struct|interface|enum|delegate)\s+(\w+)")
NS_RE = re.compile(r"(?m)^\s*namespace\s+([\w\.]+)")
# 关键字误匹配（如泛型约束/参数里的 bool 被 DECL_RE 扫到）——顶层类型名不含 C# 内建类型。
KEYWORDS = {
    "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint",
    "long", "ulong", "short", "ushort", "object", "string", "void", "dynamic",
}


def read_text(path: Path) -> str:
    data = path.read_bytes()
    for encoding in ("utf-8-sig", "gb18030"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    raise SystemExit("domain-matrix: 无法解码 " + str(path))


def strip_comments_and_strings(text: str) -> str:
    """去掉注释与字符串（保留行结构；够用的轻量词法）。"""
    out = []
    i = 0
    n = len(text)
    while i < n:
        ch = text[i]
        if ch == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            if j < 0:
                break
            out.append("\n")
            i = j + 1
            continue
        if ch == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append("\n")
            continue
        if ch == '"':
            if i > 0 and text[i - 1] == "@":
                i += 1
                while i < n:
                    if text[i] == '"':
                        if i + 1 < n and text[i + 1] == '"':
                            i += 2
                            continue
                        i += 1
                        break
                    i += 1
                out.append('""')
                continue
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
            i += 1
            out.append('""')
            continue
        if ch == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
            i += 1
            out.append("''")
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def top_level_types(text: str) -> set:
    names = set()
    depth = 0
    for raw in text.splitlines():
        if depth == 1:
            match = DECL_RE.search(raw)
            if match and match.group(1) not in KEYWORDS:
                names.add(match.group(1))
        depth += raw.count("{") - raw.count("}")
    return names


def collect_types() -> dict:
    result = {}
    for domain in DOMAIN_ORDER:
        folder = MODULES / domain
        if not folder.is_dir():
            raise SystemExit("domain-matrix: 缺域目录 " + str(folder))
        names = set()
        for path in sorted(folder.rglob("*.cs")):
            names |= top_level_types(strip_comments_and_strings(read_text(path)))
        result[domain] = names
    return result


def count_matrix(types: dict) -> dict:
    counts = {(a, b): 0 for a in DOMAIN_ORDER for b in DOMAIN_ORDER}
    for source in DOMAIN_ORDER:
        for path in sorted((MODULES / source).rglob("*.cs")):
            code = strip_comments_and_strings(read_text(path))
            for target in DOMAIN_ORDER:
                if target == source:
                    continue
                for name in types[target]:
                    counts[(source, target)] += len(re.findall(r"\b" + re.escape(name) + r"\b", code))
    return counts


def unregistered_edges(counts: dict) -> list:
    return [(source, target, count) for (source, target), count in sorted(counts.items())
            if source != target and count > 0 and (source, target) not in ALLOWED_EDGES]


def render(types: dict, counts: dict) -> str:
    lines = [
        "# 域间引用矩阵（Modules 顶层 12 域）",
        "",
        "> **生成物，禁手改**：单源 arch/units.json（域清单/角色/允许边）；",
        "> python scripts/gate/domain-matrix.py --write 重写；--check 复算比对（.workflow/config.json 门禁命令 domain-matrix）。",
        "> 计数口径：源域 .cs 的**代码区**（去注释/字符串）出现**目标域顶层类型名**的次数；对角线 -。",
        "> 口径本身由脚本注释与 --check 钉住——只改文档不改脚本会被判过期；非零跨域边未登记会直接判失败。",
        "",
        "## 引用矩阵",
        "",
        "| 源 ↓ / 目标 → | " + " | ".join(DOMAIN_ORDER) + " |",
        "|---|" + "---:|" * len(DOMAIN_ORDER),
    ]
    for source in DOMAIN_ORDER:
        cells = []
        for target in DOMAIN_ORDER:
            cells.append("-" if source == target else str(counts[(source, target)]))
        lines.append("| **" + source + "** | " + " | ".join(cells) + " |")

    lines += [
        "",
        "## 域清单（12）",
        "",
        "| 域 | 角色 | 顶层类型数 |",
        "|---|---|---:|",
    ]
    for domain in DOMAIN_ORDER:
        lines.append("| " + domain + " | " + DOMAIN_ROLES[domain] + " | " + str(len(types[domain])) + " |")

    lines += ["", "## 已登记允许边（跨域直引）", ""]
    for (source, target), reason in sorted(ALLOWED_EDGES.items()):
        lines.append("- " + source + " → " + target + "：" + reason)
    lines += [
        "",
        "> 未列出的非零跨域直引视为未登记边：--check 直接失败；先确认是否应改走端口，再把结论写进本表与",
        "> arch/units.json 的 domainEdges（单源）。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="域间引用矩阵（--check 过期即红）")
    parser.add_argument("--write", action="store_true", help="重写文档（默认只校验）")
    parser.add_argument("--check", action="store_true", help="复算并与文档比对（默认行为）")
    args = parser.parse_args()

    types = collect_types()
    counts = count_matrix(types)
    content = render(types, counts)
    missing = unregistered_edges(counts)
    if missing:
        detail = ", ".join(f"{a}->{b}({n})" for a, b, n in missing)
        if not args.write:
            print("[quality/domain-matrix] CHECK FAILED: 未登记的跨域直引: " + detail)
            return 1
        print("[quality/domain-matrix] WARN: 未登记的跨域直引（已写入文档，评审须补 ALLOWED_EDGES）: " + detail)

    if args.write:
        DOC.parent.mkdir(parents=True, exist_ok=True)
        DOC.write_text(content, encoding="utf-8")
        print("[quality/domain-matrix] wrote " + str(DOC.relative_to(ROOT)))
        return 0

    if not DOC.exists():
        print("[quality/domain-matrix] CHECK FAILED: 缺 " + str(DOC.relative_to(ROOT)) + "（先 --write）")
        return 1
    actual = DOC.read_text(encoding="utf-8")
    if actual != content:
        print("[quality/domain-matrix] CHECK FAILED: 矩阵过期——文件与当前代码不一致；重跑 --write 并评审差异")
        return 1
    print("[quality/domain-matrix] OK: 12 域矩阵与代码一致")
    return 0


if __name__ == "__main__":
    sys.exit(main())
