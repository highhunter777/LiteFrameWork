# -*- coding: utf-8 -*-
"""Offline contracts for the SSH destination resolver (P5)."""

import importlib
import os
import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "scripts" / "deploy"


def load_module():
    sys.path.insert(0, str(DEPLOY))
    try:
        return importlib.import_module("_ssh_host")
    except ImportError as exc:
        raise AssertionError(f"_ssh_host missing: {exc}") from exc


class SshHostTests(unittest.TestCase):
    def test_explicit_env_wins_without_network(self):
        module = load_module()
        previous = os.environ.get("LITEGAME_SSH_HOST")
        os.environ["LITEGAME_SSH_HOST"] = "explicit-alias"
        try:
            self.assertEqual(module.resolve_ssh_host(), "explicit-alias")
        finally:
            if previous is None:
                os.environ.pop("LITEGAME_SSH_HOST", None)
            else:
                os.environ["LITEGAME_SSH_HOST"] = previous

    def test_unreachable_tunnel_fails_instead_of_public_fallback(self):
        module = load_module()
        with self.assertRaises(RuntimeError) as context:
            module.resolve_ssh_host(
                tunnel_address="127.0.0.1",
                tunnel_port=1,
                probe_timeout_ms=50,
                public_alias="public-alias",
            )
        message = str(context.exception)
        self.assertIn("WireGuard", message)
        self.assertIn("public-alias", message)

    def test_tunnel_alias_must_match_hostname(self):
        module = load_module()
        with mock.patch.object(module, "_reachable", return_value=True), \
             mock.patch.object(module.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "hostname other\n", "")):
            with self.assertRaises(RuntimeError) as context:
                module.resolve_ssh_host(tunnel_address="10.8.0.1", tunnel_alias="tunnel", public_alias="public")
        self.assertIn("未配置", str(context.exception))


if __name__ == "__main__":
    unittest.main()
