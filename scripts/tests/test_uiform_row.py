#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""`scripts/codegen/_uiform_row.py` 的表路径注入与校验规则守卫（P2-2）。

所有用例都在 `#uiform.xlsx` 的临时副本上运行；真实表只读，前后用 SHA256 核对。
"""

import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import openpyxl  # noqa: F401 - 与助手相同的依赖，缺失时应显式失败

REPO = Path(__file__).resolve().parents[2]
HELPER = REPO / "scripts" / "codegen" / "_uiform_row.py"
REAL_TABLE = REPO / "Luban" / "Data" / "#uiform.xlsx"


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
        self.directory = Path(tempfile.mkdtemp(prefix="uiform-row-"))
        self.table = self.directory / "uiform.xlsx"
        shutil.copy2(REAL_TABLE, self.table)
        return self

    def __exit__(self, *exc):
        shutil.rmtree(self.directory, ignore_errors=True)


class UiFormRowTests(unittest.TestCase):
    def test_list_uses_injected_table(self):
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertGreaterEqual(len(payload["rows"]), 11, payload)
            self.assertTrue(all(row["prefab"].startswith("ShareResource/UI/Screens/") for row in payload["rows"]))

    def test_check_rejects_duplicate_and_reserved_ids(self):
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            existing_id = payload["rows"][0]["id"]
            code, payload, output = run(
                "--page", "Page_NewRow", "--id", existing_id, "--layer", 1, "--full-screen", "true",
                "--check", "--table", copy.table,
            )
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIn("已存在", payload["error"], output)

            code, payload, output = run(
                "--page", "Page_NewRow", "--id", 201, "--layer", 1, "--full-screen", "true",
                "--check", "--table", copy.table,
            )
            self.assertNotEqual(code, 0, output)
            self.assertIn("保留", payload["error"], output)

    def test_confirm_writes_copy_and_backup_then_rollback_restores(self):
        with TableCopy() as copy:
            before = sha256(copy.table)
            code, payload, output = run(
                "--page", "Page_NewRow", "--id", 71, "--layer", 1, "--full-screen", "true",
                "--confirm", "--table", copy.table,
            )
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            backup = Path(payload["backup"])
            self.assertTrue(backup.is_file(), output)
            self.assertEqual(backup.parent, copy.table.parent / "uiform-backups", backup)

            code, payload, output = run("--list", "--table", copy.table)
            self.assertTrue(
                any(row["id"] == 71 and row["prefab"].endswith("Page_NewRow.prefab") for row in payload["rows"]),
                output,
            )

            code, payload, output = run("--rollback", backup, "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertEqual(sha256(copy.table), before, "回滚后副本字节应恢复")

    def test_delete_id_removes_row_and_leaves_backup(self):
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            self.assertEqual(code, 0, output)
            target = payload["rows"][0]["id"]
            code, payload, output = run("--delete-id", target, "--confirm", "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertTrue(Path(payload["backup"]).is_file(), output)
            code, payload, output = run("--list", "--table", copy.table)
            self.assertTrue(all(row["id"] != target for row in payload["rows"]), output)

    def test_layer_bounds_and_pascal_case(self):
        with TableCopy() as copy:
            for layer in (-1, 4):
                code, payload, output = run(
                    "--page", "Page_NewRow", "--id", 72, "--layer", layer, "--full-screen", "true",
                    "--check", "--table", copy.table,
                )
                self.assertNotEqual(code, 0, output)
                self.assertIs(payload["ok"], False, output)
                self.assertIn("layer", payload["error"], output)

            code, payload, output = run(
                "--page", "Page_lower", "--id", 73, "--layer", 1, "--full-screen", "true",
                "--check", "--table", copy.table,
            )
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIn("PascalCase", payload["error"], output)

    @unittest.skipUnless(sys.platform == "win32", "只读目标替换失败是 Windows 语义；POSIX 上可能成功")
    def test_save_failure_returns_backup_and_keeps_table(self):
        with TableCopy() as copy:
            before = sha256(copy.table)
            os.chmod(copy.table, stat.S_IREAD)
            try:
                code, payload, output = run(
                    "--page", "Page_NewRow", "--id", 75, "--layer", 1, "--full-screen", "true",
                    "--confirm", "--table", copy.table,
                )
            finally:
                os.chmod(copy.table, stat.S_IWRITE | stat.S_IREAD)
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIsNotNone(payload.get("backup"), "写失败也必须回传备份句柄")
            self.assertTrue(Path(payload["backup"]).is_file(), output)
            self.assertEqual(sha256(copy.table), before, "保存失败不得破坏原表")

    def test_rollback_rejects_backup_outside_table_backup_dir(self):
        with TableCopy() as copy:
            outside = copy.directory / "outside.xlsx"
            shutil.copy2(copy.table, outside)
            code, payload, output = run("--rollback", outside, "--table", copy.table)
            self.assertNotEqual(code, 0, output)
            self.assertIs(payload["ok"], False, output)
            self.assertIn("备份", payload["error"], output)

    def test_missing_table_fails_explicitly(self):
        missing = Path(tempfile.mkdtemp(prefix="uiform-missing-")) / "nope.xlsx"
        code, payload, output = run("--list", "--table", missing)
        self.assertNotEqual(code, 0, output)
        self.assertIs(payload["ok"], False, output)

    def test_delete_dry_run_keeps_row(self):
        with TableCopy() as copy:
            code, payload, output = run("--list", "--table", copy.table)
            target = payload["rows"][0]["id"]
            code, payload, output = run("--delete-id", target, "--table", copy.table)
            self.assertEqual(code, 0, output)
            self.assertIs(payload["ok"], True, output)
            self.assertIs(payload.get("dryRun"), True, output)
            code, payload, output = run("--list", "--table", copy.table)
            self.assertTrue(any(row["id"] == target for row in payload["rows"]), output)

    def test_real_table_untouched(self):
        before = sha256(REAL_TABLE)
        with TableCopy() as copy:
            run("--list", "--table", copy.table)
            run("--suggest", "--table", copy.table)
            run(
                "--page", "Page_NewRow", "--id", 74, "--layer", 1, "--full-screen", "true",
                "--confirm", "--table", copy.table,
            )
        self.assertEqual(sha256(REAL_TABLE), before, "真实 #uiform.xlsx 不得被测试改动")


if __name__ == "__main__":
    unittest.main(verbosity=2)
