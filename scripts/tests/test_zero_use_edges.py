# -*- coding: utf-8 -*-
"""零使用边门禁契约（T3 / AC1）：脚本 + 基线存在，且 --check 绿。

刻意不 import 门禁脚本：脚本/基线缺失必须表现为**断言失败**（红），
不是 ImportError；--check 失败时贴出脚本原始输出便于定位。
"""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "zero-use-edges.py"
BASELINE = ROOT / "arch" / "zero-use-edges.json"


class ZeroUseEdgesTests(unittest.TestCase):
    def test_gate_script_exists(self):
        self.assertTrue(
            SCRIPT.is_file(),
            "缺零使用边门禁脚本：scripts/gate/zero-use-edges.py"
            "（T3 未完成；应复用 domain-matrix 口径：去注释/字符串、depth=1 顶层类型、按出现次数）",
        )

    def test_baseline_exists(self):
        self.assertTrue(
            BASELINE.is_file(),
            "缺零使用边基线：arch/zero-use-edges.json"
            "（T3 未完成；--check 只减不增的基线文件）",
        )

    def test_check_passes(self):
        self.assertTrue(
            SCRIPT.is_file(),
            "无法运行 --check：缺零使用边门禁脚本 scripts/gate/zero-use-edges.py（T3 未完成）",
        )
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertEqual(
            result.returncode,
            0,
            "零使用边 --check 必须绿：当前零使用边集合须是 arch/zero-use-edges.json 基线的子集（新增即红，只减不增）。\n"
            + (result.stdout or "")
            + (result.stderr or ""),
        )


if __name__ == "__main__":
    unittest.main()
