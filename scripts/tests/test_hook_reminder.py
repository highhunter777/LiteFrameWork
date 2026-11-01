# -*- coding: utf-8 -*-
"""Contracts for the Python PostToolUse reminder hook (P1).

Pinned by this file (tests-first, implementation task T1):

- the reminder fires for the CURRENT repository paths (the pre-flatten
  ``Assets/LiteSim/Core/...`` prefixes are rot, see codegen-rot-paths.json);
- every directory in ``gen-build-hash.SHARED_TARGETS`` (all 10 source sets)
  triggers the 红线1 reminder - the hook and the BuildHash source set are
  interlocked by this test instead of a path fixture that can rot;
- the copy points at the toolbox entry (``toolbox.py build-hash`` /
  ``toolbox.py proto``), not at a direct single-script invocation.
"""

import importlib.util
import json
import os
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOOK = ROOT / "scripts" / "hooks" / "shared-source-reminder.py"
GEN_BUILD_HASH = ROOT / "scripts" / "codegen" / "gen-build-hash.py"

SHARED_TARGET_DIRS = (
    "Assets/LiteSim/Scripts",
    "Assets/LiteSim/Systems",
    "Assets/LiteNet/Core/Proto",
    "Assets/LiteNet/Core/Protocol",
    "Assets/LiteNet/Core/Transport/Security",
    "Assets/LiteSync/Shared",
    "Assets/LiteSync/Snapshot",
    "Assets/LiteSync/Input",
    "Assets/LiteSync/Events",
    "Assets/GameData/Generated",
)


def load_gen_build_hash():
    spec = importlib.util.spec_from_file_location("gen_build_hash_hook", GEN_BUILD_HASH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


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

    def reminder(self, relative, env_name="ZCODE_PROJECT_DIR"):
        result = self.run_event({"tool_input": {"file_path": str(ROOT / relative)}}, env_name)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(result.stdout.strip(), f"no reminder for {relative}")
        return json.loads(result.stdout)

    def test_zcode_shared_source_warning(self):
        self.require_hook()
        payload = self.reminder("Assets/LiteSim/Scripts/World/Foo.cs")
        context = payload["additionalContext"]
        self.assertIn("红线1", context)
        self.assertIn("toolbox.py build-hash", context)

    def test_claude_serialized_schema(self):
        self.require_hook()
        payload = self.reminder("Assets/LiteSim/Scripts/World/Foo.cs", env_name="CLAUDE_PROJECT_DIR")
        self.assertEqual(payload["hookSpecificOutput"]["hookEventName"], "PostToolUse")
        context = payload["hookSpecificOutput"]["additionalContext"]
        self.assertIn("红线1", context)
        self.assertIn("toolbox.py build-hash", context)

    def test_every_shared_target_directory_triggers_the_reminder(self):
        """Hook prefixes and gen-build-hash.SHARED_TARGETS stay interlocked."""
        self.require_hook()
        module = load_gen_build_hash()
        declared = {target.replace("\\", "/") for target in module.SHARED_TARGETS}
        for directory in SHARED_TARGET_DIRS:
            with self.subTest(source_set=directory):
                self.assertIn(directory, declared,
                              "hook test list drifted from gen-build-hash.SHARED_TARGETS")
                payload = self.reminder(directory + "/Probe.cs")
                context = payload["additionalContext"]
                self.assertIn("红线1", context)
                self.assertIn("toolbox.py build-hash", context)

    def test_proto_file_reminder_points_at_the_toolbox(self):
        self.require_hook()
        context = self.reminder("Assets/LiteNet/Core/Proto/battle.proto")["additionalContext"]
        self.assertIn("红线1", context)
        self.assertIn("toolbox.py proto", context)
        self.assertIn("toolbox.py build-hash", context)

    def test_generated_proto_directory_reminder(self):
        self.require_hook()
        context = self.reminder("Assets/LiteNet/Core/Proto/Generated/Battle.cs")["additionalContext"]
        self.assertIn("红线1", context)

    def test_meta_source_reminder_points_at_the_toolbox(self):
        self.require_hook()
        context = self.reminder("Server/MetaServer/Host/Program.cs")["additionalContext"]
        self.assertIn("toolbox.py build-hash", context)

    def test_table_source_reminder_points_at_the_toolbox(self):
        self.require_hook()
        context = self.reminder("Luban/Data/tbcombatnum.xlsx")["additionalContext"]
        self.assertIn("红线1", context)
        self.assertIn("toolbox.py build-hash", context)

    def test_unity_asset_warning(self):
        self.require_hook()
        payload = self.reminder("Assets/ShareResource/UI/Screens/Foo.prefab")
        self.assertIn("红线4", payload["additionalContext"])

    def test_non_match_is_silent(self):
        self.require_hook()
        result = self.run_event({"tool_input": {"file_path": str(ROOT / "README.md")}})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "")

    def test_invalid_json_is_silent(self):
        self.require_hook()
        result = self.run_raw("{", "ZCODE_PROJECT_DIR")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "")


if __name__ == "__main__":
    unittest.main()
