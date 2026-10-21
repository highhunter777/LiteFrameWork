# -*- coding: utf-8 -*-
"""Offline contracts for the SSH destination resolver (P5)."""

import importlib
import os
import sys
import unittest
from pathlib import Path

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

    def test_unreachable_tunnel_falls_back_to_public_alias(self):
        module = load_module()
        alias = module.resolve_ssh_host(
            tunnel_address="127.0.0.1",
            tunnel_port=1,
            probe_timeout_ms=50,
            public_alias="public-alias",
        )
        self.assertEqual(alias, "public-alias")


if __name__ == "__main__":
    unittest.main()
