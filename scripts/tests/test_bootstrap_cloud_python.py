# -*- coding: utf-8 -*-
"""Offline contracts for the cloud Python bootstrap entry."""

import hashlib
import importlib
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "scripts" / "deploy"
sys.path.insert(0, str(DEPLOY))

bootstrap = importlib.import_module("bootstrap-cloud-python")


def completed(returncode=0, stdout="", stderr=""):
    return subprocess.CompletedProcess([], returncode, stdout, stderr)


class BootstrapTests(unittest.TestCase):
    def test_help(self):
        result = subprocess.run(
            [sys.executable, str(DEPLOY / "bootstrap-cloud-python.py"), "--help"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("--ssh-host", result.stdout)
        self.assertIn("--dry-run", result.stdout)
        self.assertIn("--what-if", result.stdout)

    def test_remote_version_parses_probe(self):
        with mock.patch.object(bootstrap, "probe_remote_python", return_value=completed(0, "Python 3.12.9\n")):
            self.assertEqual(bootstrap.remote_version("host"), (3, 12))
        with mock.patch.object(bootstrap, "probe_remote_python", return_value=completed(1, "", "not found")):
            self.assertIsNone(bootstrap.remote_version("host"))

    def test_download_verifies_sha256(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "python.zip"
            source.write_bytes(b"embedded-python-fixture")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            destination = Path(temp) / "copy.zip"
            self.assertEqual(bootstrap.download(source.as_uri(), destination, digest), digest)
            with self.assertRaises(RuntimeError):
                bootstrap.download(source.as_uri(), destination, "0" * 64)

    def test_preview_does_not_download(self):
        for flag in ("--dry-run", "--what-if"):
            with self.subTest(flag=flag), \
                 mock.patch.object(bootstrap, "resolve_ssh_host", return_value="host"), \
                 mock.patch.object(bootstrap, "remote_version", return_value=None), \
                 mock.patch.object(bootstrap, "download", side_effect=AssertionError("download called")):
                self.assertEqual(bootstrap.main([flag]), 0)

    def test_existing_python_uploads_runner(self):
        with mock.patch.object(bootstrap, "resolve_ssh_host", return_value="host"), \
             mock.patch.object(bootstrap, "remote_version", return_value=(3, 12)), \
             mock.patch.object(bootstrap, "ensure_remote_ready") as ensure:
            self.assertEqual(bootstrap.main([]), 0)
        ensure.assert_called_once_with("host")

    def test_install_remote_uploads_and_extracts(self):
        commands = []

        def fake_run(command):
            commands.append(command)
            return completed()

        def fake_ssh(host, command):
            commands.append(["ssh", host, command])
            return completed()

        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / "python.zip"
            archive.write_bytes(b"zip")
            with mock.patch.object(bootstrap, "_run", side_effect=fake_run), \
                 mock.patch.object(bootstrap, "ssh_run", side_effect=fake_ssh):
                bootstrap.install_remote("host", archive)
        joined = [" ".join(command) for command in commands]
        self.assertEqual(commands[0][0], "scp")
        self.assertIn("C:/litegame/python-embed.zip", commands[0][-1])
        self.assertTrue(any("tar.exe" in command for command in joined))
        self.assertTrue(any("mkdir" in command for command in joined))


if __name__ == "__main__":
    unittest.main()
