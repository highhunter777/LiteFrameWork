#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""`scripts/codegen/_combatgeom_row.py` 的写表规则守卫。

所有用例都在 `#combatgeom.xlsx` 的临时副本上运行；真实表只读，前后用 SHA256 核对。
"""

import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import openpyxl  # noqa: F401 - 与助手相同的依赖，缺失时应显式失败

REPO = Path(__file__).resolve().parents[2]
HELPER = REPO / "scripts" / "codegen" / "_combatgeom_row.py"
REAL_TABLE = REPO / "Luban" / "Data" / "#combatgeom.xlsx"
REAL_BACKUP_DIR = REPO / "Temp" / "combatgeom-backups"
SIM_PARAMS_CS = REPO / "Assets" / "LiteGame" / "Modules" / "Config" / "SimParamsOverrides.cs"


def csharp_columns():
    """C# 侧列名单源（SimParamsOverrides 的 const 声明序）。"""
    text = SIM_PARAMS_CS.read_text(encoding="utf-8")
    return re.findall(r'public const string \w+Column = "([^"]+)";', text)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def run(*args):
    result = subprocess.run(
        [sys.executable, str(HELPER)] + [str(arg) for arg in args],
        cwd=str(REPO),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=120,
    )
    payload = None
    for line in reversed((result.stdout or "").strip().splitlines()):
        try:
            payload = json.loads(line)
            break
        except json.JSONDecodeError:
            continue
    return result.returncode, payload, result.stdout + result.stderr


class TableCopy:
    def __enter__(self):
        self.directory = Path(tempfile.mkdtemp(prefix="combatgeom-row-"))
        self.table = self.directory / "combatgeom.xlsx"
        shutil.copy2(REAL_TABLE, self.table)
        return self

    def __exit__(self, *exc):
        shutil.rmtree(self.directory, ignore_errors=True)


class CombatGeomRowTests(unittest.TestCase):
    def test_list_reports_all_columns(self):
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            row = payload["row"]
            self.assertEqual(
                list(row),
                [
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
                ],
                output,
            )
            self.assertEqual(row["body_radius"], 0.32, output)
            self.assertIsInstance(row["corpse_frames"], int, output)

    def test_columns_match_csharp_single_source(self):
        """Python 写表列序必须等于 C# 调参面列名单源（防跨语言漂移）。"""
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertEqual(list(payload["row"]), csharp_columns(),
                             "scripts/codegen/_combatgeom_row.py 的 COLUMNS 与 "
                             "SimParamsOverrides.Columns 漂移")

    def test_unknown_column_rejected(self):
        with TableCopy() as copy:
            code, payload, output = run("--set", "bogus=1", "--table", copy.table)
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIn("未知列名", payload["error"], output)

    def test_int_column_rejects_decimal(self):
        with TableCopy() as copy:
            code, payload, output = run("--set", "corpse_frames=1.5", "--table", copy.table)
            self.assertNotEqual(code, 0, output)
            self.assertIn("int 列", payload["error"], output)

    def test_non_finite_rejected(self):
        with TableCopy() as copy:
            for value in ("nan", "inf", "-inf"):
                code, payload, output = run("--set", "body_radius=" + value, "--table", copy.table)
                self.assertNotEqual(code, 0, output)
                self.assertIs(payload["ok"], False, output)
                self.assertIn("有限", payload["error"], output)

    def test_out_of_range_rejected(self):
        with TableCopy() as copy:
            cases = [
                ("head_ratio=1.2", "head_ratio"),
                ("head_ratio=0", "head_ratio"),
                ("body_radius=0", "body_radius"),
                ("hip_spread_factor=-1", "hip_spread_factor"),
                ("corpse_frames=-1", "corpse_frames"),
                ("headshot_damage_shift=-2", "headshot_damage_shift"),
            ]
            for value, expected in cases:
                code, payload, output = run("--set", value, "--table", copy.table)
                self.assertNotEqual(code, 0, output)
                self.assertIs(payload["ok"], False, output)
                self.assertIn(expected, payload["error"], output)

    def test_dry_run_does_not_touch_file(self):
        with TableCopy() as copy:
            before = sha256(copy.table)
            code, payload, output = run("--set", "body_radius=0.33", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertIs(payload["dryRun"], True, output)
            self.assertEqual(payload["diff"], {"body_radius": [0.32, 0.33]}, output)
            self.assertEqual(sha256(copy.table), before, "dry-run 不得改表")

    def test_confirm_writes_value_and_backup(self):
        with TableCopy() as copy:
            code, payload, output = run("--set", "body_radius=0.33", "--confirm", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertIs(payload["dryRun"], False, output)
            backup = Path(payload["backup"])
            self.assertTrue(backup.is_file(), output)
            self.assertEqual(backup.parent, copy.table.parent / "combatgeom-backups", backup)

            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertEqual(payload["row"]["body_radius"], 0.33, output)

    def test_list_round_trip_has_empty_diff(self):
        with TableCopy() as copy:
            code, listed, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            row = listed["row"]
            args = ",".join(f"{column}={row[column]}" for column in row)
            code, payload, output = run("--set", args, "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertEqual(payload["diff"], {}, output)

    def test_confirm_with_unchanged_value_does_not_backup(self):
        with TableCopy() as copy:
            before = sha256(copy.table)
            code, payload, output = run("--set", "body_radius=0.32", "--confirm", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertNotIn("backup", payload, output)
            self.assertEqual(sha256(copy.table), before, output)

    def test_rollback_restores_table(self):
        with TableCopy() as copy:
            before = sha256(copy.table)
            code, payload, output = run("--set", "body_radius=0.33", "--confirm", "--table", copy.table)
            self.assertEqual(code, 0, output)
            backup = payload["backup"]

            code, payload, output = run("--rollback", backup, "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertEqual(sha256(copy.table), before, "回滚后副本字节应恢复")

    def test_rollback_rejects_backup_outside_table_backup_dir(self):
        with TableCopy() as copy:
            outside = copy.directory / "outside.xlsx"
            shutil.copy2(copy.table, outside)
            code, payload, output = run("--rollback", outside, "--table", copy.table)
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIn("备份", payload["error"], output)

    def test_table_copy_does_not_pollute_production_backup_dir(self):
        production_backups = set(REAL_BACKUP_DIR.glob("*.xlsx")) if REAL_BACKUP_DIR.is_dir() else set()
        before = sha256(REAL_TABLE)
        with TableCopy() as copy:
            code, payload, output = run("--set", "body_radius=0.33", "--confirm", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertEqual(Path(payload["backup"]).parent, copy.table.parent / "combatgeom-backups")
        after = set(REAL_BACKUP_DIR.glob("*.xlsx")) if REAL_BACKUP_DIR.is_dir() else set()
        self.assertEqual(after, production_backups, "副本写盘不得污染生产备份目录")
        self.assertEqual(sha256(REAL_TABLE), before, "真实 #combatgeom.xlsx 不得被测试改动")

    def test_missing_table_fails_explicitly(self):
        missing = Path(tempfile.mkdtemp(prefix="combatgeom-missing-")) / "nope.xlsx"
        code, payload, output = run("--list", "--table", missing)
        self.assertNotEqual(code, 0, output)
        self.assertIs(payload["ok"], False, output)
        self.assertIn("表源不存在", payload["error"], output)


if __name__ == "__main__":
    unittest.main(verbosity=2)
