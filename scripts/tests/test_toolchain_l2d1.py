# -*- coding: utf-8 -*-
"""L2-D1 contracts: manifest args schema and the CLI run surface."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "scripts" / "toolchain.py"
MANIFEST = ROOT / "scripts" / "gate" / "toolchain.json"

CHILD = ["python", "-c", "import sys;print('CHILD-RAN', sys.argv[1:])"]
CHILD_FAIL = ["python", "-c", "import sys;sys.exit(3)"]


def run_cli(*args, timeout=60):
    return subprocess.run(
        [sys.executable, str(CLI), *args],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def make_tool(tool_id, command, args=None, safety=None, outputs=None):
    tool = {
        "id": tool_id,
        "title": tool_id,
        "category": "test",
        "kind": "cli",
        "plane": "external",
        "purpose": "dev",
        "summary": "fixture",
        "docs": "scripts/toolchain.py",
        "command": command,
        "writes": ["none"],
    }
    if args is not None:
        tool["args"] = args
    if safety is not None:
        tool["safety"] = safety
    if outputs is not None:
        tool["outputs"] = outputs
    return tool


def make_manifest(tools):
    return {
        "schemaVersion": 6,
        "categories": [{"id": "test", "title": "Test", "order": 1}],
        "tools": tools,
        "surfaces": [],
    }


def write_manifest(temp, tools):
    path = Path(temp) / "toolchain.json"
    path.write_text(json.dumps(make_manifest(tools), ensure_ascii=False), encoding="utf-8")
    return path


ARG_TOOL = make_tool(
    "fixture.args",
    CHILD,
    args=[
        {"name": "name", "type": "string", "required": True, "description": "player name"},
        {"name": "count", "type": "int", "required": False},
        {"name": "mode", "type": "enum", "values": ["a", "b"], "required": False},
        {"name": "verbose", "type": "bool", "required": False},
    ],
)


class RunArgsTests(unittest.TestCase):
    def test_missing_required_argument_fails_before_child(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli("run", "--manifest", str(path), "fixture.args")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("--name", result.stderr)
        self.assertNotIn("CHILD-RAN", result.stdout)

    def test_invalid_int_fails_before_child(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli(
                "run", "--manifest", str(path), "fixture.args", "--", "--name", "x", "--count", "NaN"
            )
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("count", result.stderr)
        self.assertNotIn("CHILD-RAN", result.stdout)

    def test_invalid_enum_fails_before_child(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli(
                "run", "--manifest", str(path), "fixture.args", "--", "--name", "x", "--mode", "c"
            )
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("mode", result.stderr)
        self.assertNotIn("CHILD-RAN", result.stdout)

    def test_unknown_option_before_separator_is_usage_error(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli("run", "--manifest", str(path), "fixture.args", "--bogus")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertNotIn("CHILD-RAN", result.stdout)

    def test_valid_arguments_run_child_and_print_exit(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli(
                "run", "--manifest", str(path), "fixture.args", "--",
                "--name", "x", "--count", "3", "--mode", "a",
            )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("CHILD-RAN", result.stdout)
        self.assertIn("[toolchain] fixture.args exit=0", result.stdout)

    def test_help_renders_manifest_args_without_child(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [ARG_TOOL])
            result = run_cli("run", "--manifest", str(path), "fixture.args", "--help")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("--name", result.stdout)
        self.assertIn("required", result.stdout)
        self.assertIn("--mode", result.stdout)
        self.assertNotIn("CHILD-RAN [", result.stdout)

    def test_child_exit_code_passes_through(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [make_tool("fixture.fail", CHILD_FAIL)])
            result = run_cli("run", "--manifest", str(path), "fixture.fail")
        self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
        self.assertIn("[toolchain] fixture.fail exit=3", result.stdout)


class DryRunTests(unittest.TestCase):
    def test_unsupported_dry_run_is_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [make_tool("fixture.plain", CHILD)])
            result = run_cli("run", "--manifest", str(path), "fixture.plain", "--dry-run")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("dry-run", result.stderr.lower())
        self.assertNotIn("CHILD-RAN", result.stdout)

    def test_supported_dry_run_maps_to_declared_flag(self):
        tool = make_tool(
            "fixture.dry",
            CHILD,
            safety={"dryRun": "supported", "dryRunArg": "--check", "undo": "none", "confirmation": "none"},
            outputs=[{"path": "scripts/toolchain.py", "kind": "generated"}],
        )
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [tool])
            result = run_cli("run", "--manifest", str(path), "fixture.dry", "--dry-run")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("--check", result.stdout)
        self.assertIn("CHILD-RAN", result.stdout)


class ManifestArgsValidationTests(unittest.TestCase):
    def check_manifest(self, args_schema):
        tool = make_tool("fixture.invalid", ["python", "scripts/toolchain.py"], args=args_schema)
        with tempfile.TemporaryDirectory() as temp:
            path = write_manifest(temp, [tool])
            return run_cli("check", "--manifest", str(path))

    def test_args_must_be_array(self):
        result = self.check_manifest({"name": "x"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("args", result.stdout + result.stderr)

    def test_duplicate_names_rejected(self):
        result = self.check_manifest([
            {"name": "x", "type": "string"},
            {"name": "x", "type": "int"},
        ])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duplicate", result.stdout + result.stderr)

    def test_unsupported_type_rejected(self):
        result = self.check_manifest([{"name": "x", "type": "float"}])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("type", result.stdout + result.stderr)

    def test_enum_requires_values(self):
        result = self.check_manifest([{"name": "x", "type": "enum"}])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("values", result.stdout + result.stderr)

    def test_required_argument_cannot_declare_default(self):
        result = self.check_manifest([{"name": "x", "type": "string", "required": True, "default": "y"}])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("default", result.stdout + result.stderr)

    def test_enum_default_must_be_in_values(self):
        result = self.check_manifest([{"name": "x", "type": "enum", "values": ["a"], "default": "b"}])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("default", result.stdout + result.stderr)

    def test_valid_schema_passes(self):
        result = self.check_manifest([
            {"name": "name", "type": "string", "required": True},
            {"name": "mode", "type": "enum", "values": ["a", "b"]},
        ])
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


class RealManifestTests(unittest.TestCase):
    def load(self):
        return json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_high_frequency_tools_declare_args(self):
        by_id = {tool["id"]: tool for tool in self.load()["tools"]}
        for tool_id in (
            "codegen.build-hash",
            "codegen.refresh-hash",
            "build.player",
            "content.gen-candidate",
            "deploy.gm",
            "deploy.wg-peer",
        ):
            self.assertTrue(by_id[tool_id].get("args"), f"{tool_id} args missing")

    def test_run_help_uses_real_manifest(self):
        result = run_cli("run", "deploy.gm", "--help")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("--action", result.stdout)
        self.assertIn("settle-flush", result.stdout)

    def test_real_required_argument_fails_fast(self):
        result = run_cli("run", "content.gen-candidate")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("content-dir", result.stderr)


if __name__ == "__main__":
    unittest.main()
