# -*- coding: utf-8 -*-
"""Offline contracts for the cloud remote runner and SSH launcher."""

import contextlib
import importlib
import io
import os
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "scripts" / "deploy"
RUNNER = ROOT / "deploy" / "remote_runner.py"
sys.path.insert(0, str(DEPLOY))
sys.path.insert(0, str(ROOT / "deploy"))

remote = importlib.import_module("_remote")
runner = importlib.import_module("remote_runner")


def completed(returncode=0, stdout="", stderr=""):
    return subprocess.CompletedProcess([], returncode, stdout, stderr)


class RemoteLauncherTests(unittest.TestCase):
    def test_command_uses_embedded_python_and_runner(self):
        command = remote.remote_command("litegame-cloud-tunnel", "room-stop")
        self.assertEqual(
            command,
            [
                "ssh", "-o", "BatchMode=yes", "litegame-cloud-tunnel",
                "C:/litegame/python/python.exe", "C:/litegame/remote_runner.py", "room-stop",
            ],
        )

    def test_missing_cloud_python_is_actionable(self):
        with mock.patch.object(remote, "probe_remote_python", return_value=completed(1, "", "not found")):
            with self.assertRaises(RuntimeError) as context:
                remote.ensure_remote_ready("host")
        self.assertIn("bootstrap-cloud-python.py", str(context.exception))

    def test_ensure_remote_ready_uploads_runner(self):
        with mock.patch.object(remote, "probe_remote_python", return_value=completed(0, "Python 3.12.9\n")):
            with mock.patch.object(remote, "_run", return_value=completed()) as run:
                remote.ensure_remote_ready("host")
        command = run.call_args.args[0]
        self.assertEqual(command[0], "scp")
        self.assertTrue(command[2].endswith("remote_runner.py"))
        self.assertIn("remote_runner.py", command[3])

    def test_run_remote_checked_raises_on_failure(self):
        with mock.patch.object(remote.subprocess, "run", return_value=completed(3)):
            with self.assertRaises(RuntimeError) as context:
                remote.run_remote_checked("host", "room-start", label="start service")
        self.assertIn("start service", str(context.exception))

    def test_remote_python_can_be_overridden(self):
        with mock.patch.dict(os.environ, {"LITEGAME_REMOTE_PYTHON": "python"}):
            self.assertEqual(remote.remote_python(), "python")

    def test_deploy_entries_have_no_powershell_payload(self):
        for name in ("deploy-room.py", "wg-peer.py"):
            text = (DEPLOY / name).read_text(encoding="utf-8").lower()
            self.assertNotIn("powershell", text, name)
            self.assertNotIn("remote_script", text, name)
            self.assertNotIn("stop_script", text, name)


class RemoteRunnerTests(unittest.TestCase):
    def test_help_lists_commands(self):
        result = subprocess.run(
            [sys.executable, str(RUNNER), "--help"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("room-stop", result.stdout)
        self.assertIn("room-status", result.stdout)
        self.assertIn("wg-peer", result.stdout)

    def test_next_ip_uses_highest_peer(self):
        conf = "[Peer]\nAllowedIPs = 10.8.0.2/32\n[Peer]\nAllowedIPs = 10.8.0.7/32\n"
        self.assertEqual(runner.next_ip(conf), "10.8.0.8")
        self.assertEqual(runner.next_ip(""), "10.8.0.2")

    def test_extract_tgz(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "room.tgz"
            with tarfile.open(archive, "w:gz") as handle:
                payload = root / "hello.txt"
                payload.write_text("ok", encoding="utf-8")
                handle.add(payload, arcname="hello.txt")
            destination = root / "out"
            self.assertEqual(runner.extract_tgz(archive, destination), 0)
            self.assertEqual((destination / "hello.txt").read_text(encoding="utf-8"), "ok")

    def test_duplicate_peer_is_rejected_before_wg(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            wg = root / "wg.exe"
            wg.write_text("stub", encoding="utf-8")
            conf = root / "wg0.conf"
            conf.write_text("[Peer]\n# alice\nAllowedIPs = 10.8.0.2/32\n", encoding="utf-8")
            out = io.StringIO()
            with mock.patch.object(runner, "WG_EXE", wg), mock.patch.object(runner, "WG_CONF", conf), \
                 mock.patch.object(runner, "SERVER_PUB", root / "server.pub"), \
                 contextlib.redirect_stdout(out):
                code = runner.wg_peer("alice", "", "", True)
        self.assertEqual(code, 2)
        self.assertIn("DUPLICATE", out.getvalue())

    def test_status_room_reports_state_and_markers(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "out.log"
            log.write_text(
                "[RoomServer] buildHash=aaaaaaaaaaaaaaaa\n"
                "[Tables] digest:0xbbbbbbbb\n",
                encoding="utf-8",
            )
            out = io.StringIO()
            with mock.patch.object(runner, "ROOM_LOG", log), \
                 mock.patch.object(runner, "process_running", return_value=True), \
                 mock.patch.object(runner, "run", return_value=completed(0, "Status: Running\n")), \
                 contextlib.redirect_stdout(out):
                code = runner.status_room()
        text = out.getvalue()
        self.assertEqual(code, 0)
        self.assertIn("process=RUNNING", text)
        self.assertIn("task=Running", text)
        self.assertIn("buildHash=aaaaaaaaaaaaaaaa", text)

    def test_start_room_reports_task_and_process(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            room_tgz = root / "room.tgz"
            with tarfile.open(room_tgz, "w:gz") as handle:
                payload = root / "room.txt"
                payload.write_text("room", encoding="utf-8")
                handle.add(payload, arcname="room/room.txt")
            out = io.StringIO()
            with mock.patch.object(runner, "CLOUD_ROOT", root), \
                 mock.patch.object(runner, "ROOM_TGZ", room_tgz), \
                 mock.patch.object(runner, "TABLES_TGZ", root / "tables.tgz"), \
                 mock.patch.object(runner, "ROOM_LOG", root / "out.log"), \
                 mock.patch.object(runner, "run", return_value=completed(0, "Status: Running\n")), \
                 mock.patch.object(runner, "process_running", return_value=True), \
                 mock.patch.object(runner.time, "sleep", return_value=None), \
                 contextlib.redirect_stdout(out):
                code = runner.start_room(False)
        text = out.getvalue()
        self.assertEqual(code, 0)
        self.assertIn("extract=0", text)
        self.assertIn("process=RUNNING", text)
        self.assertIn("task=Running", text)


if __name__ == "__main__":
    unittest.main()
