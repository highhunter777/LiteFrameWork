#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Append/validate/rollback one tbuiform row for the UI page wizard.

The Unity wizard owns the interactive flow; this helper is the only writer of
Luban/Data/#uiform.xlsx. It never runs Luban itself: the caller commits the row
and then runs the `codegen.luban` toolchain command.

Output is one JSON object on stdout so the C# caller can render a deterministic
result. Use --confirm to write; everything else is a dry run.
"""

import argparse
import json
import re
import shutil
import sys
from datetime import datetime
from pathlib import Path

import openpyxl

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402

ROOT = Path(repo_root())
TABLE = ROOT / "Luban" / "Data" / "#uiform.xlsx"
BACKUP_DIR = ROOT / "Temp" / "uiform-backups"
SHEET = "Sheet"
HEADER_ROWS = 3
COL_ID, COL_LUA, COL_PREFAB, COL_LAYER, COL_FULL = 2, 3, 4, 5, 6
RESERVED_IDS = {201, 202, 203, 204, 205, 206}
PAGE_RE = re.compile(r"^Page_[A-Za-z][A-Za-z0-9]*$")
PREFAB_RE = re.compile(r"^ShareResource/UI/Screens/Page_[A-Za-z][A-Za-z0-9]*\.prefab$")


def emit(ok, **fields):
    payload = {"ok": bool(ok)}
    payload.update(fields)
    # ASCII escapes keep Windows consoles and the C# process boundary UTF-8-safe.
    print(json.dumps(payload))
    return 0 if ok else 1


def load_workbook():
    if not TABLE.is_file():
        raise ValueError(f"表源不存在：{TABLE.relative_to(ROOT)}")
    wb = openpyxl.load_workbook(TABLE)
    if SHEET not in wb.sheetnames:
        raise ValueError(f"工作表缺失：{SHEET}")
    ws = wb[SHEET]
    if ws.max_row < HEADER_ROWS:
        raise ValueError("表头行不完整")
    return wb, ws


def data_rows(ws):
    for row in range(HEADER_ROWS + 1, ws.max_row + 1):
        values = [ws.cell(row=row, column=col).value for col in range(1, 7)]
        if any(value is not None for value in values[1:]):
            yield row, values


def existing(ws):
    ids, lua_paths, prefabs = set(), set(), set()
    for _, values in data_rows(ws):
        if values[COL_ID - 1] is not None:
            ids.add(int(values[COL_ID - 1]))
        if values[COL_LUA - 1]:
            lua_paths.add(str(values[COL_LUA - 1]))
        if values[COL_PREFAB - 1]:
            prefabs.add(str(values[COL_PREFAB - 1]))
    return ids, lua_paths, prefabs


def validate(args, ws):
    page = args.page
    if not PAGE_RE.match(page):
        raise ValueError("页面名须为 Page_<PascalCase>（ASCII 字母数字）")
    if args.id <= 0:
        raise ValueError("id 必须为正整数")
    if args.id in RESERVED_IDS:
        raise ValueError(f"id {args.id} 属系统反馈面/静态先例保留区，不得复用")
    lua_path = f"UI.{page}"
    prefab = f"ShareResource/UI/Screens/{page}.prefab"
    if args.lua_path and args.lua_path != lua_path:
        raise ValueError(f"lua_path 与页面名不一致：{args.lua_path} != {lua_path}")
    if args.prefab and args.prefab != prefab:
        raise ValueError(f"prefab 与页面名不一致：{args.prefab} != {prefab}")
    if args.layer < 0:
        raise ValueError("layer 不得为负")
    ids, lua_paths, prefabs = existing(ws)
    if args.id in ids:
        raise ValueError(f"id 已存在：{args.id}")
    if lua_path in lua_paths:
        raise ValueError(f"lua_path 已存在：{lua_path}")
    if prefab in prefabs:
        raise ValueError(f"prefab 已存在：{prefab}")
    return lua_path, prefab


def suggest(ws):
    ids, _, _ = existing(ws)
    candidate = 16
    while candidate in ids or candidate in RESERVED_IDS:
        candidate += 1
    return candidate


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--page")
    parser.add_argument("--id", type=int, default=0)
    parser.add_argument("--lua-path", default="")
    parser.add_argument("--prefab", default="")
    parser.add_argument("--layer", type=int, default=0)
    parser.add_argument("--full-screen", type=lambda value: value.lower() == "true", default=True)
    parser.add_argument("--confirm", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--suggest", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--rollback", default="")
    args = parser.parse_args()

    try:
        if args.rollback:
            backup = Path(args.rollback).resolve()
            if not backup.is_file():
                raise ValueError(f"备份不存在：{backup}")
            if backup.parent != BACKUP_DIR.resolve():
                raise ValueError("只允许回滚本助手生成的备份")
            shutil.copy2(backup, TABLE)
            return emit(True, rolledBack=str(backup), table=str(TABLE.relative_to(ROOT)))

        wb, ws = load_workbook()
        if args.list:
            rows = []
            for _, values in data_rows(ws):
                if values[COL_ID - 1] is None:
                    continue
                rows.append({
                    "id": int(values[COL_ID - 1]),
                    "lua": str(values[COL_LUA - 1] or ""),
                    "prefab": str(values[COL_PREFAB - 1] or ""),
                    "layer": int(values[COL_LAYER - 1] or 0),
                    "fullScreen": str(values[COL_FULL - 1]).lower() == "true",
                })
            return emit(True, rows=rows)
        if args.suggest:
            return emit(True, id=suggest(ws))

        if not args.page:
            raise ValueError("缺少 --page")
        lua_path, prefab = validate(args, ws)
        row = [None, args.id, lua_path, prefab, args.layer, "true" if args.full_screen else "false"]
        if not args.confirm:
            return emit(True, dryRun=True, row=row)

        BACKUP_DIR.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
        backup = BACKUP_DIR / f"uiform-{stamp}.xlsx"
        shutil.copy2(TABLE, backup)
        ws.append(row)
        wb.save(TABLE)
        return emit(True, row=row, backup=str(backup), table=str(TABLE.relative_to(ROOT)))
    except Exception as exc:  # noqa: BLE001 - the CLI boundary maps every failure to one JSON error
        return emit(False, error=str(exc))


if __name__ == "__main__":
    sys.exit(main())
