# -*- coding: utf-8 -*-
"""Script-form checker contract tests (orphans / encoding / line endings).

Run with:
  python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
"""

import importlib
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"

EXEC_PY = """def main():
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
"""


def load_checker():
    sys.path.insert(0, str(SCRIPTS))
    try:
        module = importlib.import_module("_toolchain_scripts")
    except ImportError as exc:
        raise AssertionError(f"_toolchain_scripts missing: {exc}") from exc
    if not hasattr(module, "check_scripts"):
        raise AssertionError("_toolchain_scripts.check_scripts missing")
    return module


def make_manifest(tools=None, surfaces=None):
    return {
        "schemaVersion": 3,
        "categories": [{"id": "test", "title": "Test", "order": 1}],
        "tools": tools or [],
        "surfaces": surfaces or [],
    }


def make_tool(tool_id, command, docs="scripts/toolchain.py", kind="cli"):
    return {
        "id": tool_id,
        "title": tool_id,
        "category": "test",
        "kind": kind,
        "summary": "fixture",
        "docs": docs,
        "command": command,
        "writes": ["none"],
    }


def write(root, relative, text, encoding="utf-8", newline="\n"):
    path = Path(root) / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding=encoding, newline=newline) as handle:
        handle.write(text)
    return path


def issues_for(root, manifest, strict=False):
    return load_checker().check_scripts(Path(root), manifest, strict=strict)


def messages(issues, level=None):
    return [message for current, message in issues if level is None or current == level]


class OrphanCheckTests(unittest.TestCase):
    def test_unregistered_executable_is_warning(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/foo.py", EXEC_PY)
            found = messages(issues_for(temp, make_manifest()), "warning")
        self.assertTrue(any("unregistered executable script" in message and "foo.py" in message for message in found), found)

    def test_strict_upgrades_orphan_to_error(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/foo.py", EXEC_PY)
            found = messages(issues_for(temp, make_manifest(), strict=True), "error")
        self.assertTrue(any("unregistered executable script" in message and "foo.py" in message for message in found), found)

    def test_internal_library_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_lib.py", EXEC_PY)
            found = messages(issues_for(temp, make_manifest(), strict=True))
        self.assertEqual(found, [])

    def test_tests_directory_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/tests/test_fixture.py", EXEC_PY)
            found = messages(issues_for(temp, make_manifest(), strict=True))
        self.assertEqual(found, [])

    def test_oneshot_marker_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/ops/fix_once.py", "# @oneshot\n" + EXEC_PY)
            found = messages(issues_for(temp, make_manifest(), strict=True))
        self.assertEqual(found, [])

    def test_registered_entry_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/entry.py", EXEC_PY)
            manifest = make_manifest([make_tool("test.entry", ["python", "scripts/entry.py"])])
            found = messages(issues_for(temp, manifest, strict=True))
        self.assertEqual(found, [])

    def test_leaf_referenced_by_registered_entry_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/entry.py", "HELPERS = ['helper.py']\n")
            write(temp, "scripts/helper.py", EXEC_PY)
            manifest = make_manifest([make_tool("test.entry", ["python", "scripts/entry.py"])])
            found = messages(issues_for(temp, manifest, strict=True))
        self.assertEqual(found, [])

    def test_hook_surface_reference_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/hooks/hook.py", EXEC_PY)
            write(temp, ".zcode/config.json", '{"hook":"scripts/hooks/hook.py"}\n')
            manifest = make_manifest(
                surfaces=[{"id": "hook", "kind": "hook-config", "path": ".zcode/config.json", "required": True}]
            )
            found = messages(issues_for(temp, manifest, strict=True))
        self.assertEqual(found, [])

    def test_global_entry_is_exempt(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/toolchain.py", EXEC_PY)
            found = messages(issues_for(temp, make_manifest(), strict=True))
        self.assertEqual(found, [])


class EncodingCheckTests(unittest.TestCase):
    def test_python_bom_is_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_bom.py", "print(1)\n", encoding="utf-8-sig")
            found = messages(issues_for(temp, make_manifest(), strict=True), "error")
        self.assertTrue(any("BOM" in message for message in found), found)

    def test_powershell_script_is_not_allowed(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_cn.ps1", "param()\nWrite-Output 'x'\n", encoding="utf-8")
            found = messages(issues_for(temp, make_manifest(), strict=True), "error")
        self.assertTrue(any("PowerShell script is not allowed" in message for message in found), found)

    def test_crlf_is_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_crlf.py", "print(1)\n", newline="\r\n")
            found = messages(issues_for(temp, make_manifest(), strict=True), "error")
        self.assertTrue(any("CRLF" in message for message in found), found)

    def test_compliant_files_are_clean(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_ok.py", "print(1)\n")
            found = messages(issues_for(temp, make_manifest(), strict=True))
        self.assertEqual(found, [])


class PowerShellBanTests(unittest.TestCase):
    def test_powershell_script_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            write(temp, "scripts/_Old.ps1", "param()\nWrite-Output 'x'\n")
            found = messages(issues_for(temp, make_manifest(), strict=True), "error")
        self.assertTrue(any("PowerShell script is not allowed" in message for message in found), found)


class RepoCheckTests(unittest.TestCase):
    def test_repo_has_no_script_form_errors(self):
        import json

        manifest = json.loads((ROOT / "scripts" / "gate" / "toolchain.json").read_text(encoding="utf-8"))
        found = messages(issues_for(ROOT, manifest), "error")
        self.assertEqual(found, [])


if __name__ == "__main__":
    unittest.main()
