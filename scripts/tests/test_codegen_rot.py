# -*- coding: utf-8 -*-
"""UT-12: replay of the 8 historical codegen rot findings (6 paths + 2 comments).

The fixture (``fixtures/codegen-rot-paths.json``) is a static snapshot, so the
red evidence does not depend on reverting the repository:

- every ``path`` entry: the OLD declared path must be gone (that is the red
  condition doctor would report) and the ACTUAL path must exist (green);
- every ``comment`` entry: the stale wording must be gone from the declaring
  file (contract/manual pair for the 2 comment rots);
- live doctor must be green once the paths are fixed (C1 end state).
"""

import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "scripts" / "tests" / "fixtures" / "codegen-rot-paths.json"
TOOLBOX = ROOT / "scripts" / "codegen" / "toolbox.py"
EXPECTED_TOOLS = (
    "build-hash", "proto", "luban", "sync-code", "ui-page", "ui-api",
    "refresh-hash", "local-chain",
)


def load_fixture():
    with FIXTURE.open(encoding="utf-8") as handle:
        return json.load(handle)


class RotFixtureShapeTests(unittest.TestCase):
    def test_fixture_keeps_the_eight_history_entries(self):
        entries = load_fixture()["entries"]
        self.assertEqual(len(entries), 8, "fixture must keep all 8 historical findings")
        self.assertEqual(sum(1 for e in entries if e["kind"] == "path"), 6)
        self.assertEqual(sum(1 for e in entries if e["kind"] == "comment"), 2)

    def test_entries_are_well_formed(self):
        for entry in load_fixture()["entries"]:
            with self.subTest(tool=entry.get("tool")):
                self.assertIn(entry["kind"], ("path", "comment"))
                self.assertTrue((ROOT / entry["file"]).is_file(), entry["file"])
                if entry["kind"] == "path":
                    self.assertTrue(entry["declared"])
                    self.assertTrue(entry["actual"])
                else:
                    self.assertTrue(entry["declared_text"])


class RotReplayTests(unittest.TestCase):
    def test_old_declared_paths_are_red(self):
        for entry in load_fixture()["entries"]:
            if entry["kind"] != "path":
                continue
            with self.subTest(tool=entry["tool"], declared=entry["declared"]):
                self.assertFalse(
                    (ROOT / entry["declared"]).exists(),
                    f"the historical rot path exists again: {entry['declared']}")

    def test_actual_paths_are_green(self):
        for entry in load_fixture()["entries"]:
            if entry["kind"] != "path":
                continue
            with self.subTest(tool=entry["tool"], actual=entry["actual"]):
                self.assertTrue(
                    (ROOT / entry["actual"]).exists(),
                    f"declared actual path is missing: {entry['actual']}")

    def test_stale_comment_wording_is_gone(self):
        for entry in load_fixture()["entries"]:
            if entry["kind"] != "comment":
                continue
            with self.subTest(tool=entry["tool"]):
                text = (ROOT / entry["file"]).read_text(encoding="utf-8", errors="replace")
                self.assertNotIn(
                    entry["declared_text"], text,
                    f"{entry['file']} still carries the rotted wording")


class DoctorGreenTests(unittest.TestCase):
    """Live doctor: after the path fixes every declared path must be green."""

    def test_doctor_exits_zero_and_covers_every_path_provider(self):
        result = subprocess.run(
            [sys.executable, str(TOOLBOX), "doctor"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=180,
        )
        output = (result.stdout or "") + (result.stderr or "")
        self.assertEqual(result.returncode, 0, output)
        for tool in EXPECTED_TOOLS:
            with self.subTest(tool=tool):
                self.assertIn(tool, output)

    def test_doctor_does_not_report_the_current_paths_as_missing(self):
        result = subprocess.run(
            [sys.executable, str(TOOLBOX), "doctor"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=180,
        )
        output = ((result.stdout or "") + (result.stderr or "")).replace("\\", "/").lower()
        lines = output.splitlines()
        for entry in load_fixture()["entries"]:
            if entry["kind"] != "path":
                continue
            with self.subTest(tool=entry["tool"]):
                for line in lines:
                    if entry["actual"].lower() in line:
                        self.assertNotIn(
                            "missing", line,
                            f"doctor flags a current path as missing: {line.strip()}")


if __name__ == "__main__":
    unittest.main()
