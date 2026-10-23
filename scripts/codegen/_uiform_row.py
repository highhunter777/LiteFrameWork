#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Append/delete/validate/rollback one tbuiform row for the UI page tools.

The Unity tools own the interactive flow; this helper is the only writer of the
table source (`Luban/Data/#uiform.xlsx` by default). It never runs Luban itself:
the caller commits the row and then runs the `codegen.luban` toolchain command.

Output is one JSON object on stdout so the C# caller can render a deterministic
result. Use --confirm to write; everything else is a dry run.

`--table` overrides the table path (tests use a copy); the backup directory
follows the table (`<table.dir>/uiform-backups`) so a copy never mixes with
production backups.
"""

import argparse
import json
import os
import re
import shutil
import sys
from datetime import datetime
from pathlib import Path

import openpyxl

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402

ROOT = Path(repo_root())
DEFAULT_TABLE = ROOT / "Luban" / "Data" / "#uiform.xlsx"
DEFAULT_BACKUP_DIR = ROOT / "Temp" / "uiform-backups"
SHEET = "Sheet"
HEADER_ROWS = 3
COL_ID, COL_LUA, COL_PREFAB, COL_LAYER, COL_FULL = 2, 3, 4, 5, 6
RESERVED_IDS = {201, 202, 203, 204, 205, 206}
PAGE_RE = re.compile(r"^Page_[A-Z][A-Za-z0-9]*$")
PREFAB_RE = re.compile(r"^ShareResource/UI/Screens/Page_[A-Z][A-Za-z0-9]*\.prefab$")


def emit(ok, **fields):
    payload = {"ok": bool(ok)}
    payload.update(fields)
    # ASCII escapes keep Windows consoles and the C# process boundary UTF-8-safe.
    print(json.dumps(payload))
    return 0 if ok else 1


def resolve_table(value):
    if not value:
        return DEFAULT_TABLE
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def backup_dir_for(table):
    return DEFAULT_BACKUP_DIR if table.resolve() == DEFAULT_TABLE.resolve() else table.parent / "uiform-backups"


def load_workbook(table):
    if not table.is_file():
        raise ValueError(f"表源不存在：{table}")
    wb = openpyxl.load_workbook(table)
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
        raise ValueError("页面名须为 Page_<PascalCase>（ASCII 字母数字，首字母大写）")
    if args.id <= 0:
        raise ValueError("id 必须为正整数")
    if args.id in RESERVED_IDS:
        raise ValueError(f"id {args.id} 属系统反馈面/静态先例保留区，不得复用")
    if args.layer < 0 or args.layer > 3:
        raise ValueError("layer 必须在 0..3")
    lua_path = f"UI.{page}"
    prefab = f"ShareResource/UI/Screens/{page}.prefab"
    if args.lua_path and args.lua_path != lua_path:
        raise ValueError(f"lua_path 与页面名不一致：{args.lua_path} != {lua_path}")
    if args.prefab and args.prefab != prefab:
        raise ValueError(f"prefab 与页面名不一致：{args.prefab} != {prefab}")
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


def make_backup(table):
    directory = backup_dir_for(table)
    directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    backup = directory / f"uiform-{stamp}.xlsx"
    shutil.copy2(table, backup)
    return backup


def atomic_save(workbook, table):
    """临时件 + 替换：save 中途失败不破坏原表；失败时由调用方按备份恢复。"""
    temp = table.with_name(table.name + ".tmp")
    try:
        workbook.save(temp)
        os.replace(temp, table)
    finally:
        if temp.exists():
            temp.unlink()


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
    parser.add_argument("--delete-id", type=int, default=0)
    parser.add_argument("--table", default="", help="表源路径覆盖（默认生产表；测试用副本）")
    args = parser.parse_args()
    table = resolve_table(args.table)
    backup_dir = backup_dir_for(table)

    try:
        if args.rollback:
            backup = Path(args.rollback).resolve()
            if not backup.is_file():
                raise ValueError(f"备份不存在：{backup}")
            if backup.parent != backup_dir.resolve():
                raise ValueError("只允许回滚本表对应备份目录中的备份")
            shutil.copy2(backup, table)
            return emit(True, rolledBack=str(backup), table=str(table))

        wb, ws = load_workbook(table)
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
            return emit(True, rows=rows, table=str(table))
        if args.suggest:
            return emit(True, id=suggest(ws), table=str(table))

        if args.delete_id:
            target = None
            for row, values in data_rows(ws):
                if values[COL_ID - 1] is not None and int(values[COL_ID - 1]) == args.delete_id:
                    target = (row, values)
                    break
            if target is None:
                raise ValueError(f"id 不存在：{args.delete_id}")
            if not args.confirm:
                return emit(True, dryRun=True, deleteId=args.delete_id, row=target[1], table=str(table))

            backup = make_backup(table)
            try:
                ws.delete_rows(target[0], 1)
                atomic_save(wb, table)
            except Exception as exc:  # noqa: BLE001 - 写失败也要把备份句柄交给调用方
                try:
                    shutil.copy2(backup, table)
                except Exception:
                    pass
                return emit(False, error=str(exc), backup=str(backup), table=str(table))
            return emit(True, deleteId=args.delete_id, row=target[1], backup=str(backup), table=str(table))

        if not args.page:
            raise ValueError("缺少 --page")
        lua_path, prefab = validate(args, ws)
        row = [None, args.id, lua_path, prefab, args.layer, "true" if args.full_screen else "false"]
        if not args.confirm:
            return emit(True, dryRun=True, row=row, table=str(table))

        backup = make_backup(table)
        try:
            ws.append(row)
            atomic_save(wb, table)
        except Exception as exc:  # noqa: BLE001 - 写失败也要把备份句柄交给调用方
            try:
                shutil.copy2(backup, table)
            except Exception:
                pass
            return emit(False, error=str(exc), backup=str(backup), table=str(table))
        return emit(True, row=row, backup=str(backup), table=str(table))
    except Exception as exc:  # noqa: BLE001 - the CLI boundary maps every failure to one JSON error
        return emit(False, error=str(exc))


if __name__ == "__main__":
    sys.exit(main())
