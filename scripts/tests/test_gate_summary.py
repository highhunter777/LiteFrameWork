# -*- coding: utf-8 -*-
"""Gate summary writer contracts (TestResults/summary-<lane>.json)."""

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "gate"))

from _gate_summary import relative_path, summary_path, write_summary  # noqa: E402


class SummaryWriterTests(unittest.TestCase):
    def test_path_is_lane_scoped_and_lowercase(self):
        self.assertEqual(summary_path("out", "L1").name, "summary-l1.json")
        self.assertEqual(summary_path("out", "Doc").name, "summary-doc.json")

    def test_writes_schema_and_lf(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_summary(
                temp,
                lane="L1",
                profile="PullRequest",
                status="pass",
                total=10,
                passed=9,
                failed=1,
                skipped=0,
                artifacts=["TestResults/a.trx"],
                run_id="run-1",
            )
            raw = path.read_bytes()
            self.assertNotIn(b"\r\n", raw)
            payload = json.loads(raw.decode("utf-8"))
        self.assertEqual(payload["schemaVersion"], 1)
        self.assertEqual(payload["lane"], "L1")
        self.assertEqual(payload["profile"], "PullRequest")
        self.assertEqual(payload["status"], "pass")
        self.assertEqual(payload["runId"], "run-1")
        self.assertEqual(payload["total"], 10)
        self.assertEqual(payload["passed"], 9)
        self.assertEqual(payload["failed"], 1)
        self.assertEqual(payload["skipped"], 0)
        self.assertEqual(payload["artifacts"], ["TestResults/a.trx"])
        self.assertNotIn("error", payload)
        self.assertNotIn("lanes", payload)

    def test_error_and_lanes_are_optional(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_summary(
                temp,
                lane="Nightly",
                profile="Nightly",
                status="fail",
                failed=1,
                error="L2 failed",
                lanes=[{"lane": "L1", "status": "pass", "exitCode": 0}],
            )
            payload = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(payload["error"], "L2 failed")
        self.assertEqual(payload["lanes"][0]["lane"], "L1")

    def test_relative_path_inside_and_outside_root(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "project"
            target = root / "TestResults" / "a.trx"
            target.parent.mkdir(parents=True)
            target.write_text("x", encoding="utf-8")
            self.assertEqual(relative_path(root, target), "TestResults/a.trx")
            outside = Path(temp) / "elsewhere" / "b.trx"
            self.assertEqual(relative_path(root, outside), str(outside))


if __name__ == "__main__":
    unittest.main()
