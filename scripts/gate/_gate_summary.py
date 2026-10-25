# -*- coding: utf-8 -*-
"""Machine-readable gate summaries (TestResults/summary-<suite>.json).

One writer per suite: `test.py` owns L1/L3/Doc, `l2-unity-gate.py` owns L2,
`nightly-gate.py` owns Nightly. CI, the nightly log and humans read the same
file instead of scraping console output.

Schema v2 fields: schemaVersion, suite, profile, status (pass|fail), runId,
total, passed, failed, skipped, artifacts[], optional error and suites[].
(v1 keyed these lane/lanes; the v2 bump marks the rename.)
"""

import json
from pathlib import Path

SCHEMA_VERSION = 2


def summary_path(results_dir, suite):
    return Path(results_dir) / f"summary-{str(suite).lower()}.json"


def relative_path(root, path):
    """Repo/project-relative POSIX path for the artifacts list; absolute on failure."""
    try:
        return Path(path).resolve().relative_to(Path(root).resolve()).as_posix()
    except ValueError:
        return str(path)


def write_summary(results_dir, *, suite, profile, status, total=0, passed=0, failed=0,
                  skipped=0, artifacts=(), error="", run_id="", suites=None):
    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "suite": suite,
        "profile": profile,
        "status": status,
        "runId": run_id,
        "total": int(total),
        "passed": int(passed),
        "failed": int(failed),
        "skipped": int(skipped),
        "artifacts": [str(item) for item in artifacts],
    }
    if error:
        payload["error"] = error
    if suites is not None:
        payload["suites"] = list(suites)
    path = summary_path(results_dir, suite)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return path
