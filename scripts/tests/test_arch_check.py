# -*- coding: utf-8 -*-
"""arch-check.py 的冒烟用例（工具链测试，不是 L1 用例本身）。"""
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "arch-check.py"


class ArchCheckTests(unittest.TestCase):
    def test_check_passes(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertEqual(result.returncode, 0, (result.stdout or "") + (result.stderr or ""))
        self.assertIn("34 units", result.stdout)

    def test_generated_scan_targets_has_meta(self):
        generated = ROOT / "Assets" / "Tools" / "DisciplineScanner" / "Scripts" / "ScanTargets.Generated.cs"
        meta = generated.with_name(generated.name + ".meta")
        self.assertTrue(generated.is_file())
        self.assertTrue(meta.is_file())
        self.assertIn("guid: ", meta.read_text(encoding="utf-8-sig"))


if __name__ == "__main__":
    unittest.main()
