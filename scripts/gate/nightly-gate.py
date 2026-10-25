# -*- coding: utf-8 -*-
"""LiteGame nightly gate: run L1 + L2, write a UTF-8 log, return 0/1."""

import argparse
import os
import re
import subprocess
import sys
import traceback
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, repo_root  # noqa: E402
from _gate_summary import relative_path, write_summary  # noqa: E402


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="LiteGame nightly gate (L1 + L2)")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--skip-l1", action="store_true")
    parser.add_argument("--skip-l3", action="store_true")
    return parser.parse_args(argv)


def unity_process_count():
    try:
        result = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    except OSError:
        return 0
    return sum(1 for line in (result.stdout or "").splitlines() if "Unity.exe" in line)


def run_capture(command, project):
    return subprocess.run(
        command,
        cwd=str(project),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    stamp = datetime.now().strftime("%Y-%m-%d_%H%M%S")
    log_dir = project / "TestResults" / "nightly"
    log_dir.mkdir(parents=True, exist_ok=True)
    log_file = log_dir / f"L2-{stamp}.log"
    l3_log = log_dir / f"L3-{stamp}.log"

    def write_log(text=""):
        print(f"[env/nightly] {text}")
        with log_file.open("a", encoding="utf-8", newline="\n") as handle:
            handle.write(f"[env/nightly] {text}\n")

    exit_code = 0
    suites = []
    error_text = ""
    write_log(f"==== LiteGame 夜间门禁 {stamp} ====")
    write_log(f"工程：{project}")
    write_log(f"主机：{os.environ.get('COMPUTERNAME', '')}  用户：{os.environ.get('USERNAME', '')}")
    write_log(f"编辑器进程：{unity_process_count()} 个")

    try:
        if not args.skip_l1:
            write_log("")
            write_log("---- L1 ----")
            result = run_capture(
                [sys.executable, str(project / "scripts" / "gate" / "test.py"), "--suite", "L1", "--profile", "Nightly"],
                project,
            )
            for line in (result.stdout + result.stderr).splitlines():
                write_log(f"  {line}")
            if result.returncode != 0:
                write_log(f"  [FAIL] L1 退出码 {result.returncode}")
                exit_code = 1
            else:
                write_log("  [PASS] L1")
            suites.append({
                "suite": "L1",
                "status": "fail" if result.returncode != 0 else "pass",
                "exitCode": result.returncode,
            })

        write_log("")
        write_log("---- L2 ----")
        python_l2 = project / "scripts" / "gate" / "l2-unity-gate.py"
        if not python_l2.is_file():
            raise RuntimeError(f"L2 Python gate missing: {python_l2}")
        l2_result = run_capture([sys.executable, str(python_l2), "--project-path", str(project)], project)
        l2_text = l2_result.stdout + l2_result.stderr
        for line in l2_text.splitlines():
            write_log(f"  {line}")
        if l2_result.returncode != 0:
            write_log(f"  [FAIL] L2 退出码 {l2_result.returncode}")
            exit_code = 1
        else:
            write_log("  [PASS] L2")
        suites.append({
            "suite": "L2",
            "status": "fail" if l2_result.returncode != 0 else "pass",
            "exitCode": l2_result.returncode,
        })

        if not args.skip_l3:
            write_log("")
            write_log("---- L3 (WeakNet) ----")
            l3_result = run_capture(
                [sys.executable, str(project / "scripts" / "gate" / "test.py"), "--suite", "L3",
                 "--profile", "Nightly",
                 "--filter", "FullyQualifiedName~WeakNet&(Category!=Quarantine)"],
                project,
            )
            l3_text = l3_result.stdout + l3_result.stderr
            for line in l3_text.splitlines():
                print(f"[env/nightly]   {line}")
                with l3_log.open("a", encoding="utf-8", newline="\n") as handle:
                    handle.write(f"[env/nightly]   {line}\n")
            if l3_result.returncode != 0:
                write_log(f"  [FAIL] L3 退出码 {l3_result.returncode}")
                exit_code = 1
            else:
                write_log("  [PASS] L3")
            suites.append({
                "suite": "L3-WeakNet",
                "status": "fail" if l3_result.returncode != 0 else "pass",
                "exitCode": l3_result.returncode,
            })

        ran_playmode = bool(
            re.search(r"run_tests\(PlayMode\)", l2_text) or re.search(r"PlayMode 测试(通过|未通过)", l2_text)
        )
        write_log("")
        write_log("PlayMode：已执行" if ran_playmode else "PlayMode：**本次未执行**——请检查 L2 是否提前失败")
    except Exception as exc:
        error_text = str(exc)
        write_log(f"[FAIL] 夜间门禁异常：{exc}")
        write_log(traceback.format_exc())
        exit_code = 1
    finally:
        write_log("")
        write_log(f"==== 结束：退出码 {exit_code} ====")
        write_log(f"日志：{log_file}")
    write_summary(
        project / "TestResults",
        suite="Nightly",
        profile="Nightly",
        status="pass" if exit_code == 0 else "fail",
        failed=sum(1 for suite in suites if suite["status"] == "fail"),
        artifacts=[relative_path(project, log_file)]
        + ([relative_path(project, l3_log)] if l3_log.exists() else []),
        error=error_text,
        suites=suites,
    )
    return exit_code


if __name__ == "__main__":
    sys.exit(main())

