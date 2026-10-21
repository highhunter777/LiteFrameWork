# -*- coding: utf-8 -*-
"""Register/repair the LiteGame-Nightly-Gate scheduled task."""

import argparse
import getpass
import json
import os
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path
from xml.sax.saxutils import escape

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, info, repo_root  # noqa: E402


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Register the LiteGame nightly task")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--task-name", default="")
    parser.add_argument("--script", default="")
    parser.add_argument("--time", default="03:00")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", "--what-if", dest="dry_run", action="store_true",
                        help="print the task registration without writing it (alias: --what-if)")
    return parser.parse_args(argv)


def task_xml(project, script_path, python, time_text):
    domain = os.environ.get("USERDOMAIN", "")
    user = getpass.getuser()
    user_id = f"{domain}\\{user}" if domain else user
    start = datetime.now().strftime("%Y-%m-%d") + f"T{time_text}:00"
    return f"""<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>LiteGame nightly gate (L1 + L2)</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{escape(start)}</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{escape(user_id)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{escape(python)}</Command>
      <Arguments>-X utf8 "{escape(str(script_path))}"</Arguments>
      <WorkingDirectory>{escape(str(project))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"""


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    manifest_path = project / "scripts" / "gate" / "toolchain.json"
    if not manifest_path.is_file():
        return fail("env/nightly-install", f"toolchain manifest missing: {manifest_path}")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    surface = next((item for item in manifest.get("surfaces", []) if item.get("id") == "nightly-task"), None)
    if not surface:
        return fail("env/nightly-install", "manifest has no nightly-task surface")

    task_name = args.task_name or surface.get("name")
    script = args.script or surface.get("script")
    if not task_name or not script:
        return fail("env/nightly-install", "nightly-task surface needs name and script")
    script_path = project / script
    if not script_path.is_file():
        return fail("env/nightly-install", f"nightly script missing: {script_path}")
    runner = surface.get("runner", "python")
    if runner != "python":
        return fail("env/nightly-install", f"manifest runner is {runner!r}, expected 'python'")
    python = sys.executable

    info("env/nightly-install", f"task:     {task_name}")
    info("env/nightly-install", f"runner:   {python}")
    info("env/nightly-install", f"script:   {script_path}")
    info("env/nightly-install", f"trigger:  daily {args.time}")
    info("env/nightly-install", f"user:     {os.environ.get('USERDOMAIN', '')}\\{getpass.getuser()} (Interactive/Limited)")

    if args.dry_run:
        info("env/nightly-install", "WhatIf: task registration skipped")
        return EXIT_OK

    xml_text = task_xml(project, script_path, python, args.time)
    with tempfile.NamedTemporaryFile("w", suffix=".xml", delete=False, encoding="utf-16") as handle:
        handle.write(xml_text)
        xml_path = Path(handle.name)
    try:
        command = ["schtasks", "/Create", "/TN", task_name, "/XML", str(xml_path)]
        if args.force:
            command.append("/F")
        result = subprocess.run(command, capture_output=True, text=True, encoding="gbk", errors="replace")
        if result.returncode != 0:
            return fail("env/nightly-install", f"schtasks create failed: {result.stdout}{result.stderr}")
        query = subprocess.run(
            ["schtasks", "/Query", "/TN", task_name, "/XML"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if query.returncode != 0:
            return fail("env/nightly-install", f"schtasks query failed: {query.stdout}{query.stderr}")
        info("env/nightly-install", f"registered: {task_name}")
        return EXIT_OK
    finally:
        xml_path.unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())
