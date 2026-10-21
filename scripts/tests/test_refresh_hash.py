# -*- coding: utf-8 -*-
"""refresh-hash reconciliation contract: unreadable server hash must not pass."""

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "codegen"))

# Entry basename is kebab-case (scripts/ naming rule), so load by path
# instead of import_module.
_spec = importlib.util.spec_from_file_location(
    "refresh_hash", ROOT / "scripts" / "codegen" / "refresh-hash.py")
refresh_hash = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(refresh_hash)


class FakeCompleted:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.stderr = ""
        self.returncode = returncode


def make_root(temp, client_hash="0123456789abcdef"):
    root = Path(temp)
    path = root / "Assets" / "LiteNet" / "Protocol" / "BuildHash.g.cs"
    path.parent.mkdir(parents=True)
    path.write_text(f'public const string Value = "{client_hash}";\n', encoding="utf-8")
    return root


def run_main(root, argv, probe_stdout=""):
    def fake_run(command, **kwargs):
        return FakeCompleted(stdout=probe_stdout)

    with mock.patch.object(refresh_hash, "repo_root", return_value=root), \
         mock.patch.object(refresh_hash.subprocess, "run", side_effect=fake_run), \
         mock.patch.object(sys, "argv", ["refresh-hash.py", *argv]):
        return refresh_hash.main()


BASE_ARGS = ["--skip-server-build", "--skip-unity-recompile"]


class RefreshHashTests(unittest.TestCase):
    def test_unreadable_server_hash_fails_by_default(self):
        with tempfile.TemporaryDirectory() as temp:
            code = run_main(make_root(temp), BASE_ARGS)
        self.assertEqual(code, 1)

    def test_allow_incomplete_downgrades_explicitly(self):
        with tempfile.TemporaryDirectory() as temp:
            code = run_main(make_root(temp), BASE_ARGS + ["--allow-incomplete"])
        self.assertEqual(code, 0)

    def test_mismatched_server_hash_still_fails(self):
        with tempfile.TemporaryDirectory() as temp:
            code = run_main(
                make_root(temp, "aaaaaaaaaaaaaaaa"),
                BASE_ARGS,
                probe_stdout="buildHash=bbbbbbbbbbbbbbbb",
            )
        self.assertEqual(code, 1)

    def test_matching_server_hash_passes(self):
        with tempfile.TemporaryDirectory() as temp:
            code = run_main(
                make_root(temp, "aaaaaaaaaaaaaaaa"),
                BASE_ARGS,
                probe_stdout="buildHash=aaaaaaaaaaaaaaaa",
            )
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main()
