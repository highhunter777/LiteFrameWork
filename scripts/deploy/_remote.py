# -*- coding: utf-8 -*-
"""Upload and invoke the cloud-host Python runner over SSH."""

import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402

REMOTE_ROOT = "C:/litegame"
REMOTE_RUNNER = f"{REMOTE_ROOT}/remote_runner.py"
DEFAULT_REMOTE_PYTHON = f"{REMOTE_ROOT}/python/python.exe"
RUNNER_LOCAL = repo_root() / "deploy" / "remote_runner.py"


def remote_python():
    return os.environ.get("LITEGAME_REMOTE_PYTHON") or DEFAULT_REMOTE_PYTHON


def _run(command, **kwargs):
    return subprocess.run(
        command,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        **kwargs,
    )


def probe_remote_python(ssh_host, executable=None):
    return _run(["ssh", "-o", "BatchMode=yes", ssh_host, executable or remote_python(), "--version"])


def ensure_remote_ready(ssh_host):
    if not RUNNER_LOCAL.is_file():
        raise RuntimeError(f"remote runner payload missing: {RUNNER_LOCAL}")
    probe = probe_remote_python(ssh_host)
    if probe.returncode != 0:
        detail = ((probe.stdout or "") + (probe.stderr or "")).strip()
        raise RuntimeError(
            "cloud Python is unavailable "
            f"({remote_python()}): {detail[:200]}; run: python scripts/deploy/bootstrap-cloud-python.py"
        )
    upload = _run(["scp", "-q", str(RUNNER_LOCAL), f"{ssh_host}:{REMOTE_RUNNER}"])
    if upload.returncode != 0:
        detail = ((upload.stdout or "") + (upload.stderr or "")).strip()
        raise RuntimeError(f"scp remote_runner.py failed: {detail[:200]}")
    return ((probe.stdout or "") + (probe.stderr or "")).strip()


def remote_command(ssh_host, *args):
    return ["ssh", "-o", "BatchMode=yes", ssh_host, remote_python(), REMOTE_RUNNER, *args]


def run_remote(ssh_host, *args):
    return _run(remote_command(ssh_host, *args))


def run_remote_checked(ssh_host, *args, label="remote"):
    result = subprocess.run(remote_command(ssh_host, *args))
    if result.returncode != 0:
        raise RuntimeError(f"{label} failed (exit {result.returncode})")
    return result
