# -*- coding: utf-8 -*-
"""Contracts for the local CDN config loader (P2)."""

import importlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTENT = ROOT / "scripts" / "content"


def load_module():
    sys.path.insert(0, str(CONTENT))
    try:
        return importlib.import_module("_local_cdn")
    except ImportError as exc:
        raise AssertionError(f"_local_cdn missing: {exc}") from exc


class LocalCdnTests(unittest.TestCase):
    def write_config(self, directory, payload):
        path = Path(directory) / "local-cdn.json"
        path.write_text(json.dumps(payload), encoding="utf-8")
        return path

    def test_relative_directory_resolves_from_repo_root(self):
        module = load_module()
        with tempfile.TemporaryDirectory() as temp:
            path = self.write_config(temp, {"directory": "LocalCDN", "bind": "127.0.0.1", "port": 18090})
            config = module.load_config(path)
        self.assertEqual(config.directory, (ROOT / "LocalCDN").resolve())
        self.assertEqual(config.bind, "127.0.0.1")
        self.assertEqual(config.port, 18090)
        self.assertEqual(config.config_path, path.resolve())

    def test_absolute_directory_is_preserved(self):
        module = load_module()
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "cdn"
            path = self.write_config(temp, {"directory": str(target), "bind": "0.0.0.0", "port": 1})
            config = module.load_config(path)
        self.assertEqual(config.directory, target.resolve())

    def test_missing_file_fails_loudly(self):
        module = load_module()
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(FileNotFoundError):
                module.load_config(Path(temp) / "missing.json")

    def test_missing_field_fails_loudly(self):
        module = load_module()
        with tempfile.TemporaryDirectory() as temp:
            path = self.write_config(temp, {"directory": "x", "bind": "127.0.0.1"})
            with self.assertRaises(ValueError):
                module.load_config(path)


if __name__ == "__main__":
    unittest.main()
