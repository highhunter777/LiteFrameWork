# -*- coding: utf-8 -*-
"""Shared primitives for scripts/ Python entries (P0 foundation)."""

import subprocess
import sys
from pathlib import Path

EXIT_OK = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_FOUND = 4
EXIT_EDITOR_UNAVAILABLE = 5


def repo_root():
    return Path(__file__).resolve().parent.parent


def configure_stdio():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def info(tool, message):
    print(f"[{tool}] {message}")


def warn(tool, message):
    print(f"[{tool}] WARN: {message}", file=sys.stderr)


def fail(tool, message):
    print(f"[{tool}] FAILED: {message}", file=sys.stderr)
    return EXIT_FAIL


def run(cmd, cwd=None, env=None, **kwargs):
    return subprocess.run(cmd, cwd=cwd, env=env, **kwargs)
