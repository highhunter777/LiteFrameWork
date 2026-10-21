# -*- coding: utf-8 -*-
"""Contracts for the Python PostToolUse reminder hook (P1)."""

import json
import os
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOOK = ROOT / "scripts" / "hooks" / "shared-source-reminder.py"


class HookReminderTests(unittest.TestCase):
    def require_hook(self):
        if not HOOK.is_file():
            self.fail(f"hook missing: {HOOK}")

    def run_raw(self, raw, env_name):
        env = os.environ.copy()
        env.pop("ZCODE_PROJECT_DIR", None)
        env.pop("CLAUDE_PROJECT_DIR", None)
        env[env_name] = str(ROOT)
        return subprocess.run(
            [sys.executable, str(HOOK)],
            cwd=str(ROOT),
            input=raw,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=env,
            timeout=30,
        )

    def run_event(self, event, env_name="ZCODE_PROJECT_DIR"):
        return self.run_raw(json.dumps(event, ensure_ascii=False), env_name)

    def test_zcode_shared_source_warning(self):
        self.require_hook()
        event = {"tool_input": {"file_path": str(ROOT / "Assets" / "LiteSim" / "Core" / "Scripts" / "Foo.cs")}}
        result = self.run_event(event)
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertIn("红线1", payload["additionalContext"])
        self.assertIn("gen-build-hash.py", payload["additionalContext"])

    def test_claude_serialized_schema(self):
        self.require_hook()
        event = {"tool_input": {"file_path": str(ROOT / "Assets" / "LiteSim" / "Core" / "Scripts" / "Foo.cs")}}
        result = self.run_event(event, env_name="CLAUDE_PROJECT_DIR")
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertEqual(payload["hookSpecificOutput"]["hookEventName"], "PostToolUse")
        self.assertIn("红线1", payload["hookSpecificOutput"]["additionalContext"])

    def test_unity_asset_warning(self):
        self.require_hook()
        event = {"tool_input": {"file_path": str(ROOT / "Assets" / "ShareResource" / "UI" / "Screens" / "Foo.prefab")}}
        result = self.run_event(event)
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertIn("红线4", payload["additionalContext"])

    def test_non_match_is_silent(self):
        self.require_hook()
        event = {"tool_input": {"file_path": str(ROOT / "README.md")}}
        result = self.run_event(event)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "")

    def test_invalid_json_is_silent(self):
        self.require_hook()
        result = self.run_raw("{", "ZCODE_PROJECT_DIR")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "")


if __name__ == "__main__":
    unittest.main()
