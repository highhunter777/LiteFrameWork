# -*- coding: utf-8 -*-
"""Shared single source for the generator toolbox (scripts/codegen/*).

Every generator here follows one form (see the generator toolbox design doc):
  - repo root comes from _common.repo_root(), the single implementation;
  - output goes through info/warn/fail with a `[codegen/<tool>]` prefix;
  - expected failures surface as ONE clean `[codegen/<tool>] FAILED: ...`
    line + exit code 1 -- never a traceback, never a silent no-op.

Import from each tool:  from _codegen import repo_root, info, warn, fail, run_tool
"""

import os
import sys

SCRIPTS_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

from _common import fail as _fail, info as _info, repo_root as _repo_root, warn as _warn  # noqa: E402


def repo_root():
    """scripts/codegen -> repo root, delegated to scripts/_common.py."""
    return str(_repo_root())


def info(tool, message):
    _info(f"codegen/{tool}", message)


def warn(tool, message):
    _warn(f"codegen/{tool}", message)


def fail(tool, message):
    return _fail(f"codegen/{tool}", message)


def run_tool(tool, main):
    """Unified execution face: run <main>, convert exceptions to one clean
    [codegen/<tool>] FAILED line + exit 1 (expected-failure contract)."""
    try:
        code = main()
        sys.exit(0 if code is None else code)
    except SystemExit:
        raise
    except Exception as exc:  # noqa: BLE001 - single clean failure line is the contract
        fail(tool, str(exc))
        sys.exit(1)
