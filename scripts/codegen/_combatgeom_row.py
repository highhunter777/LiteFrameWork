#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Write/read the single `#combatgeom` data row (判定几何 13 列).

The Unity baking tools own the interactive flow; this helper is the only writer
of the table source (`Luban/Data/#combatgeom.xlsx` by default). It never runs
Luban itself: after a successful write the caller runs `Luban/gen.bat` and then
`scripts/codegen/gen-build-hash.py`.

Output is one JSON object on stdout so the C# caller can render a deterministic
result. `--set` is a dry run unless `--confirm` is given.

`--table` overrides the table path (tests use a copy); the backup directory
follows the table (`Temp/combatgeom-backups` for the production table, and
`<table.dir>/combatgeom-backups` for a copy) so a copy never mixes with
production backups.
"""

import argparse
import json
import math
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
DEFAULT_TABLE = ROOT / "Luban" / "Data" / "#combatgeom.xlsx"
DEFAULT_BACKUP_DIR = ROOT / "Temp" / "combatgeom-backups"
SHEET = "Sheet"
HEADER_ROWS = 3
COL_ID = 2
FIRST_VALUE_COL = 3

# 第 4 行是唯一数据行（单行表）；列序与 `##var` 行一一对应。
COLUMNS = [
    "body_radius",
    "hitscan_height",
    "hitscan_radius",
    "head_ratio",
    "head_radius",
    "muzzle_fwd",
    "muzzle_right",
    "muzzle_height",
    "headshot_damage_shift",
    "hip_spread_factor",
    "corpse_frames",
    "hard_max_speed",
    "face_turn_rad_per_sec",
]
INT_COLUMNS = {"headshot_damage_shift", "corpse_frames"}
POSITIVE_COLUMNS = {
    "body_radius",
    "hitscan_height",
    "hitscan_radius",
    "head_ratio",
    "head_radius",
    "hip_spread_factor",
    "hard_max_speed",
    "face_turn_rad_per_sec",
}
NON_NEGATIVE_INT_COLUMNS = {"headshot_damage_shift", "corpse_frames"}
INT_RE = re.compile(r"^[+-]?\d+$")


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
    return DEFAULT_BACKUP_DIR if table.resolve() == DEFAULT_TABLE.resolve() else table.parent / "combatgeom-backups"


def load_workbook(table):
    if not table.is_file():
        raise ValueError(f"表源不存在：{table}")
    wb = openpyxl.load_workbook(table)
    if SHEET not in wb.sheetnames:
        raise ValueError(f"工作表缺失：{SHEET}")
    ws = wb[SHEET]
    if ws.max_row < HEADER_ROWS + 1:
        raise ValueError("表头行不完整或缺数据行")
    return wb, ws


def header_types(ws):
    """`##type` 行（第 2 行）按列名给出 int/float；白名单与表头必须一致。"""
    types = {}
    for index, column in enumerate(COLUMNS):
        declared = ws.cell(row=2, column=FIRST_VALUE_COL + index).value
        if declared is None:
            raise ValueError(f"表头缺 `##type`：{column}")
        types[column] = str(declared).strip().lower()
    return types


def find_row(ws, row_id):
    for row in range(HEADER_ROWS + 1, ws.max_row + 1):
        value = ws.cell(row=row, column=COL_ID).value
        if value is None:
            continue
        try:
            if int(value) == row_id:
                return row
        except (TypeError, ValueError):
            continue
    raise ValueError(f"表内缺 id={row_id} 数据行（本助手不新插行）")


def read_row(ws, row, types):
    values = {}
    for index, column in enumerate(COLUMNS):
        raw = ws.cell(row=row, column=FIRST_VALUE_COL + index).value
        if raw is None:
            raise ValueError(f"数据行缺值：{column}")
        if types[column] == "int":
            values[column] = int(raw)
        else:
            values[column] = float(raw)
    return values


def parse_set(value, types):
    text = str(value).strip()
    if not text:
        raise ValueError("--set 为空")

    updates = {}
    for item in text.split(","):
        item = item.strip()
        if not item:
            raise ValueError("--set 含空条目")
        if "=" not in item:
            raise ValueError(f"--set 条目缺 '='：{item}")
        column, raw = item.split("=", 1)
        column = column.strip()
        raw = raw.strip()
        if column not in COLUMNS:
            raise ValueError(f"未知列名：{column}（合法：{', '.join(COLUMNS)}）")
        if column in updates:
            raise ValueError(f"--set 重复列：{column}")
        updates[column] = parse_value(column, raw, types[column])
    return updates


def parse_value(column, raw, declared_type):
    if declared_type == "int":
        if not INT_RE.match(raw):
            raise ValueError(f"{column} 为 int 列，不接受小数/非整数：{raw}")
        return int(raw)
    if declared_type != "float":
        raise ValueError(f"{column} 表头类型未知：{declared_type}（只接受 int/float）")
    try:
        value = float(raw)
    except ValueError as exc:
        raise ValueError(f"{column} 不是合法数值：{raw}") from exc
    if not math.isfinite(value):
        raise ValueError(f"{column} 必须为有限数值：{raw}")
    return value


def validate_row(values):
    """与 `SimConfigMapper.Validate` 同口径（宁严勿松）。"""
    for column in POSITIVE_COLUMNS:
        if not values[column] > 0:
            raise ValueError(f"{column} 必须为正有限数值：{values[column]}")
    if not (0 < values["head_ratio"] <= 1):
        raise ValueError(f"head_ratio 必须落在 (0,1]：{values['head_ratio']}")
    for column in NON_NEGATIVE_INT_COLUMNS:
        if values[column] < 0:
            raise ValueError(f"{column} 必须为非负整数：{values[column]}")


def make_backup(table):
    directory = backup_dir_for(table)
    directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    backup = directory / f"combatgeom-{stamp}.xlsx"
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
    parser.add_argument("--set", default="", help="col=value[,col=value...] 写入数据行")
    parser.add_argument("--list", action="store_true", help="输出当前数据行")
    parser.add_argument("--confirm", action="store_true", help="真正写盘（缺省 dry-run）")
    parser.add_argument("--rollback", default="", help="备份文件路径（只接受本表备份目录内）")
    parser.add_argument("--id", type=int, default=1, help="数据行主键（缺省 1）")
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
        types = header_types(ws)
        row_index = find_row(ws, args.id)
        current = read_row(ws, row_index, types)

        if args.list:
            return emit(True, row=current, table=str(table))

        if not args.set:
            raise ValueError("缺少 --set 或 --list")

        updates = parse_set(args.set, types)
        merged = dict(current)
        merged.update(updates)
        validate_row(merged)

        diff = {column: [current[column], value] for column, value in updates.items() if current[column] != value}
        if not args.confirm:
            return emit(True, dryRun=True, row=merged, diff=diff, table=str(table))

        backup = None
        if diff:
            backup = make_backup(table)
            try:
                for column, value in diff.items():
                    ws.cell(row=row_index, column=FIRST_VALUE_COL + COLUMNS.index(column)).value = merged[column]
                atomic_save(wb, table)
            except Exception as exc:  # noqa: BLE001 - 写失败也要把备份句柄交给调用方
                if backup is not None:
                    try:
                        shutil.copy2(backup, table)
                    except Exception:
                        pass
                return emit(False, error=str(exc), backup=str(backup) if backup else "", table=str(table))

        payload = {"dryRun": False, "row": merged, "diff": diff, "table": str(table)}
        if backup is not None:
            payload["backup"] = str(backup)
        return emit(True, **payload)
    except Exception as exc:  # noqa: BLE001 - the CLI boundary maps every failure to one JSON error
        return emit(False, error=str(exc), table=str(table))


if __name__ == "__main__":
    sys.exit(main())
