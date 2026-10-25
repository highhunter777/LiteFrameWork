# -*- coding: utf-8 -*-
"""Filter composition and nightly L3 wiring contracts."""

import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
TEST_PY = ROOT / "scripts" / "gate" / "test.py"
NIGHTLY_PY = ROOT / "scripts" / "gate" / "nightly-gate.py"


def load_module():
    spec = importlib.util.spec_from_file_location("gate_test_under_test", TEST_PY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class LaneFilterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = load_module()

    def test_no_override_keeps_default(self):
        self.assertEqual(self.mod.compose_filter("A", None), "A")

    def test_override_replaces_classification_and_keeps_quarantine(self):
        self.assertEqual(self.mod.compose_filter("A", "B"), "(B)&(Category!=Quarantine)")

    def test_test_cli_accepts_filter(self):
        result = subprocess.run(
            [sys.executable, str(TEST_PY), "--help"], cwd=str(ROOT),
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--filter", result.stdout)

    def test_nightly_has_l3_weaknet_step(self):
        result = subprocess.run(
            [sys.executable, str(NIGHTLY_PY), "--help"], cwd=str(ROOT),
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--skip-l3", result.stdout)
        text = NIGHTLY_PY.read_text(encoding="utf-8")
        self.assertIn("L3-WeakNet", text)
        self.assertIn("FullyQualifiedName~WeakNet&(Category!=Quarantine)", text)


if __name__ == "__main__":
    unittest.main()
