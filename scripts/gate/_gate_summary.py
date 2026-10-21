# -*- coding: utf-8 -*-
"""Machine-readable gate summaries (TestResults/summary-<lane>.json).

One writer per lane: `test.py` owns L1/L3/Doc, `l2-unity-gate.py` owns L2,
`nightly-gate.py` owns Nightly. CI, the nightly log and humans read the same
file instead of scraping console output.

Schema v1 fields: schemaVersion, lane, profile, status (pass|fail), runId,
total, passed, failed, skipped, artifacts[], optional error and lanes[].
"""

import json
from pathlib import Path

SCHEMA_VERSION = 1


def summary_path(results_dir, lane):
    return Path(results_dir) / f"summary-{str(lane).lower()}.json"


def relative_path(root, path):
    """Repo/project-relative POSIX path for the artifacts list; absolute on failure."""
    try:
        return Path(path).resolve().relative_to(Path(root).resolve()).as_posix()
    except ValueError:
        return str(path)


def write_summary(results_dir, *, lane, profile, status, total=0, passed=0, failed=0,
                  skipped=0, artifacts=(), error="", run_id="", lanes=None):
    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "lane": lane,
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
    if lanes is not None:
        payload["lanes"] = list(lanes)
    path = summary_path(results_dir, lane)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return path
