# -*- coding: utf-8 -*-
"""结构化日志检索的契约测试（scripts/logs/query.py）。

覆盖：字段过滤（对局/席位/级别/关键字）、尾部 limit、--json 原样输出、--stats、坏行跳过。
运行：python -m unittest discover -s scripts/tests -p "test_log_query*.py" -v
"""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "scripts" / "logs" / "query.py"


def run_cli(*args, timeout=30):
    return subprocess.run(
        [sys.executable, str(CLI), *args],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def entry(ts, lvl, tag, mid, frame, player, msg):
    return json.dumps(
        {"ts": ts, "lvl": lvl, "tag": tag, "sid": "abc12345", "mid": mid,
         "f": frame, "p": player, "msg": msg},
        ensure_ascii=False,
    )


class LogQueryTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)
        lines = [
            entry("2026-10-21T15:00:00.000Z", "Info", "Battle", "match-alpha", 10, 0, "spawn ok"),
            entry("2026-10-21T15:00:01.000Z", "Warning", "Battle", "match-alpha", 11, 0, "lag spike"),
            entry("2026-10-21T15:00:02.000Z", "Error", "Net", "match-beta", 12, 1, "timeout"),
            "not-json",
        ]
        (self.dir / "log-abc-p1.jsonl").write_text("\n".join(lines) + "\n", encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def test_按对局与级别过滤(self):
        result = run_cli(str(self.dir), "--match", "alpha", "--level", "warning", "--json")
        self.assertEqual(0, result.returncode, result.stderr)
        rows = [line for line in result.stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(rows))
        self.assertIn("lag spike", rows[0])

    def test_按席位与关键字过滤(self):
        result = run_cli(str(self.dir), "--player", "1", "--contains", "TIME", "--json")
        self.assertEqual(0, result.returncode, result.stderr)
        rows = [line for line in result.stdout.splitlines() if line.strip()]
        self.assertEqual(1, len(rows))
        self.assertIn("timeout", rows[0])

    def test_limit取尾部(self):
        result = run_cli(str(self.dir), "--limit", "2", "--json")
        self.assertEqual(0, result.returncode, result.stderr)
        rows = [line for line in result.stdout.splitlines() if line.strip()]
        self.assertEqual(2, len(rows))
        self.assertIn("lag spike", rows[0])
        self.assertIn("timeout", rows[1])

    def test_stats汇总(self):
        result = run_cli(str(self.dir), "--stats")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("条数=3", result.stdout)
        self.assertIn("match-alpha", result.stdout)

    def test_stats不受limit影响(self):
        result = run_cli(str(self.dir), "--stats", "--limit", "2")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("条数=3", result.stdout, "stats 聚合全量命中，不取尾部")

    def test_坏行跳过不失败(self):
        result = run_cli(str(self.dir), "--json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(3, len([line for line in result.stdout.splitlines() if line.strip()]))
        self.assertIn("1 行非法 JSON", result.stderr)

    def test_路径不存在返回1(self):
        result = run_cli(str(self.dir / "missing.jsonl"))
        self.assertEqual(1, result.returncode)
        self.assertIn("没有可读的 .jsonl", result.stderr)


if __name__ == "__main__":
    unittest.main()
