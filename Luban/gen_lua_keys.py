# -*- coding: utf-8 -*-
# 注册表 Lua 路径 -> LuaKeys.g.cs 生成器（设计方案 §277）。
# 不硬编码注册表文件名——扫描 Data 目录全部 xlsx（跳过 __*.xlsx），
# 凡 `##var` 表头含 `lua_path` / `entry` 列即认定为注册表表（xlsx 改名/新增注册表表零改动；
# 重命名 xlsx 的唯一联动点是 __tables__.xlsx 的 input_file 列，与本脚本无关）。
# 按"根.名字"两段式校验（根限定 UI/Content/Strategies），未知根/重名直接抛（fail the build）。
# 产物头走 _codegen.signature（toolbox/luban + 注册表指纹 + regen）；指纹只覆盖注册表 xlsx 集，
# 非注册表表改动不移动头（否则任意改表都让 luban --check 假 stale）。
import argparse
import glob
import openpyxl
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CODEGEN_DIR = os.path.join(ROOT, "scripts", "codegen")
if CODEGEN_DIR not in sys.path:
    sys.path.insert(0, CODEGEN_DIR)
from _codegen import input_digest, signature  # noqa: E402  (shared head/digest implementation)

# Toolbox subcommand that runs this helper (the table chain) -- the artifact's
# regen line names it, not this script.
TOOLBOX_TOOL = "luban"

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Data")
# OUT_FILE = 共享常量面（Assets/LiteGame/Modules/Shared/Generated）——LuaKeys 被 Ui/Procedure 两个域
#   与 Lua 桥适配器同时消费，不属任何单一域；落这里既不出域，也不让消费方反向依赖 Ui 域。
#   本路径是生成落点单源：改这里要同批改 gen_luban.py 的发布目标、toolchain.json 的产物表、
#   test_generated_artifacts.py 的路径映射（否则旧路径会再生成一份同名 LuaKeys，CS0101）。
OUT_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "..", "Assets", "LiteGame", "Modules", "Shared", "Generated", "LuaKeys.g.cs")

PATH_FIELDS = ("lua_path", "entry")               # 注册表路径列字段名
ROOTS = ("UI", "Content", "Strategies")           # §4.4 三个固定根
PATH_RE = re.compile(r"^(?P<root>[A-Za-z_]\w*)\.(?P<name>[A-Za-z_]\w*)$")


def scan():
    """Scan Data for registry tables. Returns (paths, registry_files):
    a workbook counts as a registry table only when its `##var` header carries a
    `lua_path`/`entry` column -- the artifact head fingerprints exactly that set,
    so editing a non-registry table never churns LuaKeys.g.cs."""
    paths = []                                    # [(root, name, raw)]
    registry_files = []
    workbooks = sorted(glob.glob(os.path.join(DATA_DIR, "*.xlsx")))
    for full in workbooks:
        filename = os.path.basename(full)
        if filename.startswith("__"):             # 表单注册/枚举/bean 元文件
            continue
        wb = openpyxl.load_workbook(full)
        ws = wb.active
        header_row = None
        columns = {}
        for i, row in enumerate(ws.iter_rows(values_only=True), start=1):
            first = str(row[0]) if row and row[0] is not None else ""
            if first == "##var":                  # 字段名行（Luban 表头约定）
                header_row = i
                for j, cell in enumerate(row):
                    if cell is not None:
                        columns[str(cell).strip()] = j + 1
                break
        if header_row is None:
            print("skip (no ##var header): %s" % filename)
            continue
        path_cols = [columns[f] for f in PATH_FIELDS if f in columns]
        if not path_cols:
            print("skip (no path column): %s" % filename)
            continue
        registry_files.append(full)
        for row in ws.iter_rows(min_row=header_row + 1, values_only=True):
            first = str(row[0]) if row and row[0] is not None else ""
            if first.startswith("##"):            # 跳过 ##type / ## 注释行
                continue
            for col in path_cols:
                raw = str(row[col - 1]).strip() if col - 1 < len(row) and row[col - 1] is not None else ""
                if not raw:
                    continue                      # 空路径 = 留空用 C# 默认实现（合法）
                m = PATH_RE.match(raw)
                if not m or m.group("root") not in ROOTS:
                    raise SystemExit("gen_lua_keys: 未知根或非法两段式路径 '%s'（%s）——根限定 %s"
                                     % (raw, filename, ROOTS))
                paths.append((m.group("root"), m.group("name"), raw))
    return paths, registry_files


def collect():
    """Registry Lua paths (root, name, raw) -- the only scanning face."""
    return scan()[0]


def fingerprint(registry_files):
    """Head digest over the registry workbook set only (relative name + content,
    line endings normalized by the shared _codegen implementation)."""
    entries = [os.path.relpath(full, DATA_DIR).replace("\\", "/") for full in registry_files]
    return input_digest(entries, root=DATA_DIR)


def emit(paths, head):
    by_root = {}
    for root, name, raw in paths:
        if root not in ROOTS:                     # 纵深防御：绕过 collect 的直调也拦
            raise SystemExit("gen_lua_keys: 未知根 '%s'（%s）——根限定 %s" % (root, raw, ROOTS))
        seen = by_root.setdefault(root, {})
        if name in seen and seen[name] != raw:
            raise SystemExit("gen_lua_keys: 重名冲突 '%s.%s'" % (root, name))
        seen[name] = raw

    lines = list(head)
    lines.append("namespace LiteGame.Modules.Shared")
    lines.append("{")
    lines.append("    /// <summary>注册表 Lua 路径常量（生成物；收口散落字符串路径，设计方案 §277）。</summary>")
    lines.append("    public static class LuaKeys")
    lines.append("    {")
    for root in ROOTS:
        names = by_root.get(root, {})
        if not names:
            continue
        lines.append("        public static class %s" % root)
        lines.append("        {")
        for name in sorted(names):
            lines.append('            /// <summary>%s.%s</summary>' % (root, name))
            lines.append('            public const string %s = "%s.%s";' % (name, root, name))
        lines.append("        }")
    lines.append("    }")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description="Generate registered Lua path constants")
    parser.add_argument("--output", default=OUT_FILE)
    output_file = os.path.abspath(parser.parse_args().output)
    paths, registry_files = scan()
    head = signature(TOOLBOX_TOOL, "%d registry tables" % len(registry_files),
                     fingerprint(registry_files))
    content = emit(paths, head)
    os.makedirs(os.path.dirname(output_file), exist_ok=True)
    with open(output_file, "w", encoding="utf-8", newline="\n") as f:
        f.write(content)
    print("LuaKeys.g.cs written: %d paths -> %s" % (len(paths), output_file))


if __name__ == "__main__":
    main()
