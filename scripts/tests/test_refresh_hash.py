# -*- coding: utf-8 -*-
"""refresh-hash / local-chain reconciliation contracts.

Pinned by this file (tests-first, implementation task T1):

- both consumers read the BuildHash constant through the CURRENT path
  ``Packages/com.litegame.net/Core/Protocol/BuildHash.g.cs`` (single source:
  ``gen-build-hash.shared_out_file(root)``); the pre-Core
  ``Packages/com.litegame.net/Protocol/...`` path is rot (see codegen-rot-paths.json);
- ``refresh_hash.main()`` still reconciles both ends with the documented
  exit-code contract (unreadable server hash fails unless explicitly
  downgraded, mismatch fails, match passes);
- ``local_chain.build_hash(root)`` returns the 16-hex constant for an injected
  root and does not require ``deploy/.env``.
"""

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "codegen"))

BUILD_HASH_REL = "Packages/com.litegame.net/Core/Protocol/BuildHash.g.cs"
STALE_BUILD_HASH_REL = "Packages/com.litegame.net/Protocol/BuildHash.g.cs"


def load(name, relative):
    # Entry basenames are kebab-case (scripts/ naming rule), so load by path
    # instead of import_module.
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


refresh_hash = load("refresh_hash", "scripts/codegen/refresh-hash.py")
local_chain = load("local_chain", "scripts/local/local-chain.py")


class FakeCompleted:
    def __init__(self, stdout="", returncode=0):
        self.stdout = stdout
        self.stderr = ""
        self.returncode = returncode


def make_root(temp, client_hash="0123456789abcdef"):
    root = Path(temp)
    path = root / BUILD_HASH_REL
    path.parent.mkdir(parents=True)
    path.write_text(f'public const string Value = "{client_hash}";\n', encoding="utf-8")
    return root


def run_main(root, argv, probe_stdout=""):
    def fake_run(command, **kwargs):
        return FakeCompleted(stdout=probe_stdout)

    with mock.patch.object(refresh_hash, "repo_root", return_value=root), \
         mock.patch.object(refresh_hash.subprocess, "run", side_effect=fake_run), \
         mock.patch.object(sys, "argv", ["refresh-hash.py", *argv]):
        try:
            return refresh_hash.main()
        except Exception as exc:  # noqa: BLE001 - surface as an assertion failure
            return f"EXCEPTION {type(exc).__name__}: {exc}"


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

    def test_client_constant_is_read_from_the_core_protocol_path(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_root(temp, "aaaaaaaaaaaaaaaa")
            code = run_main(root, BASE_ARGS, probe_stdout="buildHash=aaaaaaaaaaaaaaaa")
            self.assertEqual(code, 0)
            stale = root / STALE_BUILD_HASH_REL
            self.assertFalse(stale.exists(), "test fixture must only feed the current path")


class LocalChainBuildHashTests(unittest.TestCase):
    def test_build_hash_reads_the_current_path_without_deploy_env(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_root(temp, "fedcba9876543210")
            self.assertEqual(local_chain.build_hash(root), "fedcba9876543210")

    def test_build_hash_reports_missing_when_only_the_stale_path_exists(self):
        with tempfile.TemporaryDirectory() as temp:
            stale = Path(temp) / STALE_BUILD_HASH_REL
            stale.parent.mkdir(parents=True)
            stale.write_text('public const string Value = "ffffffffffffffff";\n', encoding="utf-8")
            self.assertIn("missing", local_chain.build_hash(Path(temp)))


if __name__ == "__main__":
    unittest.main()
