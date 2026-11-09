# -*- coding: utf-8 -*-
"""零使用边门禁契约（T3 / AC1）：脚本 + 基线存在，且 --check 绿。

刻意不 import 门禁脚本：脚本/基线缺失必须表现为**断言失败**（红），
不是 ImportError；--check 失败时贴出脚本原始输出便于定位。
"""

import json
import subprocess
import sys
import tempfile
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

    def _run(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )

    def _current_baseline(self, directory):
        """把当前零使用集合写成临时基线（不改 arch/zero-use-edges.json）。"""
        path = Path(directory) / "current.json"
        result = self._run("--write", "--baseline", str(path))
        self.assertEqual(
            result.returncode,
            0,
            "生成临时基线失败：\n" + (result.stdout or "") + (result.stderr or ""),
        )
        return path

    def test_new_zero_use_edge_fails_check(self):
        """UT-ZERO-2：合成一条基线外的新零使用边 => --check 红。"""
        with tempfile.TemporaryDirectory() as directory:
            empty = Path(directory) / "empty-unit"
            empty.mkdir()
            model = json.loads((ROOT / "arch" / "units.json").read_text(encoding="utf-8-sig"))
            synthetic = dict(model)
            synthetic["units"] = list(model["units"]) + [
                {
                    "id": "Synthetic.ZeroUse",
                    "path": empty.as_posix(),
                    "references": ["LiteGame.Modules.Config"],
                }
            ]
            model_path = Path(directory) / "units.json"
            model_path.write_text(
                json.dumps(synthetic, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
            result = self._run("--check", "--model", str(model_path))
            output = (result.stdout or "") + (result.stderr or "")
            self.assertNotEqual(
                result.returncode,
                0,
                "合成的新零使用边（空源 unit -> Config）必须判失败（只减不增）。\n" + output,
            )
            self.assertIn("Synthetic.ZeroUse", output)
            self.assertIn("LiteGame.Modules.Config", output)

    def test_resolved_baseline_edge_passes(self):
        """UT-ZERO-3：基线中一条边在当前集合里消失（已消解）=> --check 通过。"""
        with tempfile.TemporaryDirectory() as directory:
            current_path = self._current_baseline(directory)
            data = json.loads(current_path.read_text(encoding="utf-8-sig"))
            current = {(item["from"], item["to"]) for item in (data.get("edges") or [])}
            model = json.loads((ROOT / "arch" / "units.json").read_text(encoding="utf-8-sig"))
            unit_ids = {unit["id"] for unit in model["units"]}
            candidate = None
            for unit in model["units"]:
                for reference in unit.get("references") or []:
                    pair = (unit["id"], reference)
                    if reference in unit_ids and reference != unit["id"] and pair not in current:
                        candidate = {"from": pair[0], "to": pair[1]}
                        break
                if candidate:
                    break
            self.assertIsNotNone(
                candidate,
                "找不到未处于零使用集合的 unit→unit 引用，无法模拟已消失的基线边",
            )
            extended = dict(data)
            extended["edges"] = list(data.get("edges") or []) + [candidate]
            extended_path = Path(directory) / "extended.json"
            extended_path.write_text(
                json.dumps(extended, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
            result = self._run("--check", "--baseline", str(extended_path))
            self.assertEqual(
                result.returncode,
                0,
                "基线中已消失的边必须放行（只减不增）。\n"
                + (result.stdout or "")
                + (result.stderr or ""),
            )


if __name__ == "__main__":
    unittest.main()
