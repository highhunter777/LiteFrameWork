# -*- coding: utf-8 -*-
"""Shared single source for the generator toolbox (scripts/codegen/*).

Every generator here follows one form (see the generator toolbox design doc):
  - repo root is derived from the script's own location, never from CWD;
  - output goes through info/warn/fail with a `[codegen/<tool>]` prefix;
  - expected failures surface as ONE clean `[codegen/<tool>] FAILED: ...`
    line + exit code 1 -- never a traceback, never a silent no-op.

Import from each tool:  from _codegen import repo_root, info, warn, fail, run_tool
"""

import os
import sys


def repo_root():
    """scripts/codegen -> repo root (works from any CWD; scripts/README convention)."""
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def info(tool, message):
    print(f"[codegen/{tool}] {message}")


def warn(tool, message):
    print(f"[codegen/{tool}] WARN: {message}")


def fail(tool, message):
    print(f"[codegen/{tool}] FAILED: {message}", file=sys.stderr)


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
