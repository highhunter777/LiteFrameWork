# -*- coding: utf-8 -*-
"""Unified gate test entry (L1/L2/L3/All/Doc)."""

import argparse
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

ENV_KEYS = (
    "DOTNET_CLI_HOME",
    "DOTNET_CLI_TELEMETRY_OPTOUT",
    "DOTNET_PROCESSOR_COUNT",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE",
    "LITETEST_ARTIFACTS",
    "LITETEST_RUN_ID",
    "M10_LONGRUN",
    "NUGET_PACKAGES",
)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="LiteGame gate test entry")
    parser.add_argument("--lane", choices=("L1", "L2", "L3", "All", "Doc"), default="L1")
    parser.add_argument("--profile", choices=("PullRequest", "Nightly", "Release"), default="PullRequest")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--results-directory", default="TestResults")
    return parser.parse_args(argv)


def save_env():
    return {key: os.environ.get(key) for key in ENV_KEYS}


def restore_env(saved):
    for key, value in saved.items():
        if value is None:
            os.environ.pop(key, None)
        else:
            os.environ[key] = value


def discover_projects(project):
    tests = project / "Tests"
    all_csproj = sorted(tests.rglob("*.csproj"), key=lambda path: str(path).lower()) if tests.is_dir() else []
    projects = [path for path in all_csproj if path.name.endswith(".Tests.csproj")]
    if len(projects) != len(all_csproj):
        names = ", ".join(path.name for path in all_csproj if path not in projects)
        raise RuntimeError(
            "Test project name violates the *.Tests.csproj discovery contract and would be silently skipped: " + names
        )
    if not projects:
        raise RuntimeError("No test projects were found under Tests/.")
    return projects


def trx_total(path):
    total = 0
    if not path.is_file():
        return total
    root = ET.parse(path).getroot()
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] == "Counters":
            try:
                total += int(element.attrib.get("total", "0"))
            except ValueError:
                pass
    return total


def run_or_raise(command, label, cwd=None):
    result = subprocess.run(command, cwd=str(cwd) if cwd else None)
    if result.returncode != 0:
        raise RuntimeError(f"{label} failed with exit code {result.returncode}.")
    return result


def invoke_dotnet_lane(name, filter_text, hang_timeout, project, results, run_id, dotnet):
    info("gate/test", f"=== {name} | {os.environ.get('DOTNET_TEST_PROFILE', '')} ===")
    projects = discover_projects(project)
    for project_file in projects:
        project_name = project_file.stem
        info("gate/test", f"-> restore {project_name} (parallelism disabled)")
        run_or_raise(
            [
                dotnet,
                "restore",
                str(project_file),
                "--disable-parallel",
                "--ignore-failed-sources",
                "--verbosity",
                "minimal",
                "-m:1",
                "-p:BuildInParallel=false",
                "-p:NuGetAudit=false",
            ],
            f"{name} restore failed for {project_name}",
            cwd=project,
        )

    solution = project / "Tests" / "Tests.slnx"
    info("gate/test", "-> build solution (single MSBuild node)")
    run_or_raise(
        [
            dotnet,
            "build",
            str(solution),
            "--configuration",
            "Release",
            "--no-restore",
            "--verbosity",
            "minimal",
            "-m:1",
            "-p:BuildInParallel=false",
        ],
        f"{name} solution build failed",
        cwd=project,
    )

    failed = []
    total = 0
    for project_file in projects:
        project_name = project_file.stem
        result_name = f"{name.lower()}-{project_name}-{run_id}.trx"
        result_file = results / result_name
        info("gate/test", f"-> {project_name}")
        result = subprocess.run(
            [
                dotnet,
                "test",
                str(project_file),
                "--configuration",
                "Release",
                "--no-build",
                "--no-restore",
                "--verbosity",
                "minimal",
                "--results-directory",
                str(results),
                "--logger",
                f"trx;LogFileName={result_name}",
                "--blame-hang-timeout",
                hang_timeout,
                "--filter",
                filter_text,
            ],
            cwd=str(project),
        )
        if result.returncode != 0:
            failed.append(project_name)
            continue
        total += trx_total(result_file)

    if failed:
        raise RuntimeError(f"{name} failed in: {', '.join(failed)}. Results: {results}")
    if total <= 0:
        raise RuntimeError(f"{name} selected zero tests. Check traits and filter: {filter_text}")
    info("gate/test", f"PASS: {name} completed {total} tests.")


def invoke_doc_lint(project, profile):
    info("gate/test", f"doc-lint | {profile}")
    doc_lint = project / "scripts" / "gate" / "doc-lint.py"
    toolchain = project / "scripts" / "toolchain.py"
    if not doc_lint.is_file():
        raise RuntimeError(f"doc-lint script missing: {doc_lint}")
    if not toolchain.is_file():
        raise RuntimeError(f"toolchain CLI missing: {toolchain}")
    previous = os.environ.get("PYTHONIOENCODING")
    os.environ["PYTHONIOENCODING"] = "utf-8"
    try:
        run_or_raise([sys.executable, str(doc_lint), "--root", str(project)], "doc-lint", cwd=project)
        run_or_raise([sys.executable, str(toolchain), "docs", "--check"], "tool index check", cwd=project)
    finally:
        if previous is None:
            os.environ.pop("PYTHONIOENCODING", None)
        else:
            os.environ["PYTHONIOENCODING"] = previous
    info("gate/test", "PASS: doc-lint + tool index completed.")


def invoke_l2(project):
    if os.name != "nt":
        raise RuntimeError("L2 requires Windows, a complete Unity project and a licensed Unity Editor.")
    python_l2 = project / "scripts" / "gate" / "l2-unity-gate.py"
    if not python_l2.is_file():
        raise RuntimeError(f"L2 Python gate missing: {python_l2}")
    run_or_raise([sys.executable, str(python_l2), "--project-path", str(project)], "L2", cwd=project)


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    results = Path(args.results_directory)
    if not results.is_absolute():
        results = project / results
    results.mkdir(parents=True, exist_ok=True)

    dotnet = shutil.which("dotnet")
    if args.lane in {"L1", "L3", "All"} and not dotnet:
        return fail("gate/test", "dotnet not found on PATH")

    saved = save_env()
    run_id = f"{args.profile.lower()}-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}"
    os.environ.update(
        {
            "LITETEST_ARTIFACTS": str(results / "artifacts"),
            "LITETEST_RUN_ID": run_id,
            "DOTNET_CLI_HOME": str(project / ".dotnet-cli"),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_PROCESSOR_COUNT": "4",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_TEST_PROFILE": args.profile,
        }
    )
    nuget = Path.home() / ".nuget" / "packages"
    if nuget.is_dir():
        os.environ["NUGET_PACKAGES"] = str(nuget)

    try:
        if args.lane == "L1":
            invoke_dotnet_lane(
                "L1",
                "Category!=Integration&Category!=EndToEnd&Category!=Performance&Category!=Quarantine&Duration!=LongRunning",
                "5m",
                project,
                results,
                run_id,
                dotnet,
            )
        elif args.lane == "L3":
            filter_text = "(Category=Integration|Category=EndToEnd)&Category!=Quarantine"
            timeout = "5m"
            if args.profile == "PullRequest":
                filter_text += "&Duration!=LongRunning"
            else:
                os.environ["M10_LONGRUN"] = "1"
                timeout = "12m"
            invoke_dotnet_lane("L3", filter_text, timeout, project, results, run_id, dotnet)
        elif args.lane == "L2":
            invoke_l2(project)
        elif args.lane == "Doc":
            invoke_doc_lint(project, args.profile)
        else:
            invoke_dotnet_lane(
                "L1",
                "Category!=Integration&Category!=EndToEnd&Category!=Performance&Category!=Quarantine&Duration!=LongRunning",
                "5m",
                project,
                results,
                run_id,
                dotnet,
            )
            filter_text = "(Category=Integration|Category=EndToEnd)&Category!=Quarantine"
            timeout = "5m"
            if args.profile == "PullRequest":
                filter_text += "&Duration!=LongRunning"
            else:
                os.environ["M10_LONGRUN"] = "1"
                timeout = "12m"
            invoke_dotnet_lane("L3", filter_text, timeout, project, results, run_id, dotnet)
            invoke_l2(project)
        return EXIT_OK
    except RuntimeError as exc:
        return fail("gate/test", str(exc))
    finally:
        os.environ.pop("DOTNET_TEST_PROFILE", None)
        restore_env(saved)


if __name__ == "__main__":
    sys.exit(main())
