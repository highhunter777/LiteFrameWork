from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SKILL_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = SKILL_ROOT / "scripts" / "workflow.py"

PASS_COMMAND = {
    "name": "smoke",
    "cmd": [sys.executable, "-c", "print('smoke ok')"],
    "timeout": 60,
}
FAIL_COMMAND = {
    "name": "smoke",
    "cmd": [
        sys.executable,
        "-c",
        "import sys; print('boom', file=sys.stderr); raise SystemExit(3)",
    ],
    "timeout": 60,
}


class WorkflowCliTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.slug = "demo"
        self.run_dir = self.root / ".workflow" / "active" / self.slug

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def cli(self, *args: object, expect: int | None = 0) -> subprocess.CompletedProcess[str]:
        command = [
            sys.executable,
            str(SCRIPT),
            "--root",
            str(self.root),
            *[str(arg) for arg in args],
        ]
        env = dict(os.environ, PYTHONIOENCODING="utf-8")
        result = subprocess.run(
            command,
            cwd=self.root,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=env,
        )
        if expect is not None:
            self.assertEqual(
                expect,
                result.returncode,
                msg=f"命令: {command}\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
        return result

    @staticmethod
    def output(result: subprocess.CompletedProcess[str]) -> str:
        return result.stdout + result.stderr

    def init(self, slug: str | None = None, title: str = "示例功能") -> None:
        self.slug = slug or self.slug
        self.run_dir = self.root / ".workflow" / "active" / self.slug
        self.cli("init", self.slug, "--title", title)

    def status(self) -> dict:
        return json.loads(self.cli("status", "--json").stdout)

    def config(self) -> dict:
        path = self.root / ".workflow" / "config.json"
        return json.loads(path.read_text(encoding="utf-8"))

    def save_config(self, config: dict) -> None:
        path = self.root / ".workflow" / "config.json"
        path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    def tasks(self) -> list[dict]:
        return json.loads(self.cli("task", "list", "--json").stdout)["tasks"]

    def fill_current_stage(self) -> None:
        state = self.status()
        if state["stage"] is None:
            return
        stage_id = state["stage"]["id"]
        config = self.config()
        stage = next(item for item in config["stages"] if item["id"] == stage_id)
        placeholders = config.get("validation", {}).get("placeholders", ["待填写"])
        for artifact in stage.get("artifacts", []):
            if artifact["path"] == "tasks.md":
                continue
            path = self.run_dir / artifact["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.exists():
                text = path.read_text(encoding="utf-8")
            else:
                sections = "\n\n".join(
                    f"## {section}\n" for section in artifact.get("sections", [])
                )
                text = f"# {artifact['path']}\n\n{sections}\n"
            for placeholder in placeholders:
                text = text.replace(placeholder, "已完成并核对；依据方案、任务与测试记录。")
            path.write_text(text, encoding="utf-8")
        for verdict in stage.get("verdicts", []):
            path = self.run_dir / verdict["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            text = path.read_text(encoding="utf-8") if path.exists() else ""
            for field, allowed in verdict["fields"].items():
                value = allowed[0]
                pattern = re.compile(rf"(?m)^(-\s*{re.escape(field)}\s*[:：]\s*).*$")
                if pattern.search(text):
                    text = pattern.sub(lambda match, v=value: match.group(1) + v, text)
                else:
                    if "## 评审结论" not in text:
                        text = text.rstrip() + "\n\n## 评审结论\n"
                    text = text.rstrip() + f"\n- {field}: {value}\n"
            path.write_text(text, encoding="utf-8")

    def advance(self, expect: int = 0, approve: str | None = None):
        args = ["advance"]
        if approve:
            args.extend(["--approve", approve])
        return self.cli(*args, expect=expect)

    def add_task(self, title: str = "核心实现", deps: str | None = None) -> str:
        args = [
            "task",
            "add",
            "--title",
            title,
            "--acceptance",
            "行为满足方案验收标准",
            "--verify",
            "运行自动化测试",
        ]
        if deps:
            args.extend(["--deps", deps])
        self.cli(*args)
        return self.tasks()[-1]["id"]

    def finish_task(self, task_id: str, evidence: str = "自动化测试通过") -> None:
        self.cli("task", "start", task_id)
        self.cli("task", "done", task_id, "--evidence", evidence)

    def set_commands(self, commands: list[dict], red_commands: list[dict] | None = None) -> None:
        config = self.config()
        config["commands"] = commands
        if red_commands is not None:
            config["red_commands"] = red_commands
        self.save_config(config)

    def reach_verify(self) -> str:
        self.init()
        self.set_commands([PASS_COMMAND])
        self.fill_current_stage()
        self.advance()
        task_id = self.add_task()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.fill_current_stage()
        self.finish_task(task_id)
        self.advance()
        self.assertEqual("verify", self.status()["stage"]["id"])
        return task_id

    def test_placeholder_content_blocks_stage(self) -> None:
        self.init()
        plan = (self.run_dir / "plan.md").read_text(encoding="utf-8")
        self.assertIn("## 需求澄清与假设", plan)
        result = self.advance(expect=1)
        self.assertIn("待填写", self.output(result))
        checked = self.cli("check", expect=1)
        self.assertIn("待填写", self.output(checked))
        state = self.status()
        self.assertTrue(any("待填写" in item["message"] for item in state["blockers"]))

    def test_task_lifecycle_and_dependency_rules(self) -> None:
        self.init()
        first = self.add_task("任务一")
        second = self.add_task("任务二", deps=first)

        result = self.cli("task", "start", second, expect=1)
        self.assertIn("依赖", self.output(result))

        result = self.cli("task", "done", first, "--evidence", "done", expect=1)
        self.assertIn("进行中", self.output(result))

        self.cli("task", "start", first)
        result = self.cli("task", "done", first, expect=1)
        self.assertIn("证据", self.output(result))
        self.cli("task", "done", first, "--evidence", "pytest tests/test_a.py")
        self.cli("task", "start", second)

        self.cli("task", "block", second, "--reason", "等待测试环境")
        blocked_again = self.cli("task", "start", second, expect=1)
        self.assertIn("阻塞", self.output(blocked_again))
        self.cli("task", "unblock", second)
        task = next(item for item in self.tasks() if item["id"] == second)
        self.assertEqual("todo", task["status"])

        third = self.add_task("任务三")
        fourth = self.add_task("任务四", deps=third)
        result = self.cli("task", "update", third, "--deps", fourth, expect=1)
        self.assertIn("循环", self.output(result))

        result = self.cli(
            "task",
            "add",
            "--title",
            "任务五",
            "--acceptance",
            "x",
            "--verify",
            "y",
            "--deps",
            "T99",
            expect=1,
        )
        self.assertIn("不存在", self.output(result))

        result = self.cli("task", "drop", second, expect=1)
        self.assertIn("理由", self.output(result))
        self.cli("task", "drop", second, "--reason", "范围调整")
        task = next(item for item in self.tasks() if item["id"] == second)
        self.assertEqual("cancelled", task["status"])

        self.cli("task", "reopen", second, "--reason", "重新纳入")
        task = next(item for item in self.tasks() if item["id"] == second)
        self.assertEqual("todo", task["status"])

    def test_verification_failure_and_staleness(self) -> None:
        self.reach_verify()
        self.set_commands([FAIL_COMMAND])
        result = self.cli("verify", expect=1)
        self.assertIn("FAIL", result.stdout)
        evidence = json.loads(
            (self.run_dir / "evidence" / "verify.json").read_text(encoding="utf-8")
        )
        self.assertFalse(evidence["ok"])

        blocked = self.advance(expect=1)
        self.assertIn("验证未通过", self.output(blocked))

        self.set_commands([PASS_COMMAND])
        self.cli("verify")
        self.advance()
        self.assertEqual("review-code", self.status()["stage"]["id"])

        self.add_task("补做任务")
        state = self.status()
        self.assertTrue(state["verification"]["stale"])
        blocked = self.advance(expect=1)
        self.assertIn("过期", self.output(blocked))

    def test_verification_requires_configured_commands(self) -> None:
        self.reach_verify()
        self.set_commands([])
        blocked = self.advance(expect=1)
        self.assertIn("验证命令", self.output(blocked))

    def test_red_verification_gate(self) -> None:
        self.init()
        self.set_commands([PASS_COMMAND], red_commands=[PASS_COMMAND])
        self.fill_current_stage()
        self.advance()
        self.add_task()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.assertEqual("tests-first", self.status()["stage"]["id"])
        self.fill_current_stage()

        blocked = self.advance(expect=1)
        self.assertIn("预期失败", self.output(blocked))

        failed_red = self.cli("verify", "--red", expect=1)
        self.assertIn("未出现预期失败", failed_red.stdout)

        self.set_commands([PASS_COMMAND], red_commands=[FAIL_COMMAND])
        self.cli("verify", "--red")
        self.advance()
        self.assertEqual("implement", self.status()["stage"]["id"])

        raw_state = json.loads((self.run_dir / "state.json").read_text(encoding="utf-8"))
        self.assertFalse(raw_state["red_verification"]["stale"])
        self.cli("task", "start", "T1")
        raw_state = json.loads((self.run_dir / "state.json").read_text(encoding="utf-8"))
        self.assertFalse(raw_state["red_verification"]["stale"])

        self.cli("rewind", "tests-first", "--reason", "重新确认红测")
        raw_state = json.loads((self.run_dir / "state.json").read_text(encoding="utf-8"))
        self.assertTrue(raw_state["red_verification"]["stale"])

    def test_rewind_reopens_earlier_stage(self) -> None:
        self.init()
        self.fill_current_stage()
        self.advance()
        self.add_task()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.assertEqual("tests-first", self.status()["stage"]["id"])

        result = self.cli("rewind", "breakdown", "--reason", "方案调整，重排任务")
        self.assertIn("breakdown", result.stdout)
        state = self.status()
        self.assertEqual("breakdown", state["stage"]["id"])
        self.assertEqual(["propose"], [item["id"] for item in state["completed_stages"]])
        log = (self.run_dir / "build-log.md").read_text(encoding="utf-8")
        self.assertIn("方案调整，重排任务", log)

        result = self.cli("rewind", "nope", "--reason", "x", expect=1)
        self.assertIn("未知阶段", self.output(result))

    def test_custom_stage_and_optional_approval(self) -> None:
        self.init()
        config = self.config()
        config["commands"] = [PASS_COMMAND]
        config["stages"].insert(
            -1,
            {
                "id": "security",
                "title": "安全检查",
                "artifacts": [
                    {
                        "path": "security.md",
                        "sections": ["检查范围", "检查结果"],
                    }
                ],
                "verdicts": [
                    {"path": "security.md", "fields": {"结论": ["PASS"]}}
                ],
                "approval": True,
            },
        )
        self.save_config(config)

        self.fill_current_stage()
        self.advance()
        task_id = self.add_task()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.fill_current_stage()
        self.advance()
        self.fill_current_stage()
        self.finish_task(task_id)
        self.advance()
        self.cli("verify")
        self.advance()
        self.assertEqual("review-code", self.status()["stage"]["id"])
        self.fill_current_stage()
        self.advance()
        self.assertEqual("security", self.status()["stage"]["id"])

        blocked = self.advance(expect=1)
        self.assertIn("security.md", self.output(blocked))

        (self.run_dir / "security.md").write_text(
            "# 安全检查\n\n## 检查范围\n认证与权限边界。\n\n"
            "## 检查结果\n未发现高危问题。\n\n## 评审结论\n- 结论: PASS\n",
            encoding="utf-8",
        )
        blocked = self.advance(expect=1)
        self.assertIn("审批", self.output(blocked))

        self.advance(approve="hunter")
        state = self.status()
        self.assertEqual("writeback", state["stage"]["id"])
        raw_state = json.loads((self.run_dir / "state.json").read_text(encoding="utf-8"))
        security = next(
            item for item in raw_state["completed_stages"] if item["id"] == "security"
        )
        self.assertEqual("hunter", security["approved_by"])

    def test_full_workflow_reaches_done_and_archives(self) -> None:
        self.init()
        self.set_commands([PASS_COMMAND])

        blocked = self.advance(expect=1)
        self.assertIn("待填写", self.output(blocked))
        self.fill_current_stage()
        self.advance()
        self.assertEqual("breakdown", self.status()["stage"]["id"])

        blocked = self.advance(expect=1)
        self.assertIn("至少需要", self.output(blocked))
        task_id = self.add_task()
        self.advance()
        self.assertEqual("review-tasks", self.status()["stage"]["id"])

        self.fill_current_stage()
        self.advance()
        self.assertEqual("tests-first", self.status()["stage"]["id"])
        self.fill_current_stage()
        self.advance()
        self.assertEqual("implement", self.status()["stage"]["id"])

        self.fill_current_stage()
        blocked = self.advance(expect=1)
        self.assertIn("未完成", self.output(blocked))
        self.finish_task(task_id)
        self.advance()
        self.assertEqual("verify", self.status()["stage"]["id"])

        blocked = self.advance(expect=1)
        self.assertIn("尚未运行验证", self.output(blocked))
        verified = self.cli("verify")
        self.assertIn("PASS", verified.stdout)
        self.advance()
        self.assertEqual("review-code", self.status()["stage"]["id"])

        self.fill_current_stage()
        review_path = self.run_dir / "reviews" / "code-review.md"
        valid_review = review_path.read_text(encoding="utf-8")
        invalid_review = re.sub(
            r"(?m)^(-\s*(通用性|扩展性)\s*[:：]\s*).*$",
            r"\1FAIL",
            valid_review,
        )
        self.assertNotEqual(valid_review, invalid_review)
        review_path.write_text(invalid_review, encoding="utf-8")
        blocked = self.advance(expect=1)
        self.assertIn("通用性", self.output(blocked))
        self.assertIn("扩展性", self.output(blocked))
        review_path.write_text(valid_review, encoding="utf-8")
        self.advance()
        self.assertEqual("writeback", self.status()["stage"]["id"])

        self.fill_current_stage()
        self.advance()

        state = self.status()
        self.assertEqual("done", state["status"])
        self.assertIsNone(state["stage"])

        plan_text = (self.run_dir / "plan.md").read_text(encoding="utf-8")
        (self.run_dir / "plan.md").unlink()
        blocked = self.cli("archive", expect=1)
        self.assertIn("plan.md", self.output(blocked))
        (self.run_dir / "plan.md").write_text(plan_text, encoding="utf-8")

        evidence_path = self.run_dir / "evidence" / "verify.json"
        evidence_text = evidence_path.read_text(encoding="utf-8")
        evidence_path.unlink()
        blocked = self.cli("archive", expect=1)
        self.assertIn("证据", self.output(blocked))
        evidence_path.write_text(evidence_text, encoding="utf-8")

        archived = self.cli("archive")
        self.assertIn("已归档", archived.stdout)
        self.assertFalse(self.run_dir.exists())
        archive_root = self.root / ".workflow" / "archive"
        archived_dirs = [item for item in archive_root.iterdir() if item.is_dir()]
        self.assertEqual(1, len(archived_dirs))
        archived_dir = archived_dirs[0]
        self.assertTrue((archived_dir / "ARCHIVE.md").exists())
        archived_state = json.loads(
            (archived_dir / "state.json").read_text(encoding="utf-8")
        )
        self.assertEqual("archived", archived_state["status"])
        index = (archive_root / "INDEX.md").read_text(encoding="utf-8")
        self.assertIn("demo", index)
        listed = json.loads(self.cli("list", "--json").stdout)
        self.assertTrue(
            any(
                item["slug"] == "demo" and item["status"] == "archived"
                for item in listed["runs"]
            )
        )

    def test_project_template_override_and_trailing_options(self) -> None:
        templates = self.root / ".workflow" / "templates"
        templates.mkdir(parents=True)
        source = (SKILL_ROOT / "assets" / "templates" / "plan.md").read_text(
            encoding="utf-8"
        )
        source = source.replace(
            "# 方案：{{title}}", "# 方案：{{title}}\n\nPROJECT-TEMPLATE-MARKER"
        )
        (templates / "plan.md").write_text(source, encoding="utf-8")

        self.init(title="覆盖模板")
        plan = (self.run_dir / "plan.md").read_text(encoding="utf-8")
        self.assertIn("PROJECT-TEMPLATE-MARKER", plan)
        self.assertIn("覆盖模板", plan)

        self.init("second", title="第二个运行")
        result = self.cli("status", "--json", "--run", "second")
        state = json.loads(result.stdout)
        self.assertEqual("second", state["slug"])

    def test_verification_supports_working_directory(self) -> None:
        self.reach_verify()
        subdirectory = self.root / "sub"
        subdirectory.mkdir()
        (subdirectory / "value.txt").write_text("ok", encoding="utf-8")
        command = {
            "name": "cwd-check",
            "cmd": [
                sys.executable,
                "-c",
                "from pathlib import Path; assert Path('value.txt').read_text() == 'ok'",
            ],
            "cwd": "sub",
            "timeout": 60,
        }
        self.set_commands([command])
        self.cli("verify")
        self.advance()
        self.assertEqual("review-code", self.status()["stage"]["id"])

    def test_custom_stage_bootstrapped_before_init(self) -> None:
        workflow_dir = self.root / ".workflow"
        workflow_dir.mkdir()
        config = json.loads(
            (SKILL_ROOT / "assets" / "config.default.json").read_text(encoding="utf-8")
        )
        config["stages"].insert(
            -1,
            {
                "id": "docs-check",
                "title": "文档检查",
                "artifacts": [
                    {
                        "path": "docs-check.md",
                        "sections": ["文档范围", "检查结论"],
                    }
                ],
            },
        )
        (workflow_dir / "config.json").write_text(
            json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

        self.init()
        self.assertEqual(9, len(self.config()["stages"]))
        artifact = (self.run_dir / "docs-check.md").read_text(encoding="utf-8")
        self.assertIn("## 文档范围", artifact)
        self.assertIn("## 检查结论", artifact)
        self.assertIn("待填写", artifact)

    def test_multiple_active_runs_require_explicit_run(self) -> None:
        self.init("one")
        self.init("two")
        result = self.cli("status", expect=1)
        self.assertIn("多个", self.output(result))
        result = self.cli("--run", "two", "status", "--json")
        state = json.loads(result.stdout)
        self.assertEqual("two", state["slug"])

    def test_duplicate_init_and_bad_config(self) -> None:
        self.init()
        result = self.cli("init", "demo", "--title", "重复", expect=1)
        self.assertIn("已存在", self.output(result))

        (self.root / ".workflow" / "config.json").write_text("{not json", encoding="utf-8")
        result = self.cli("status", expect=1)
        self.assertIn("配置", self.output(result))


if __name__ == "__main__":
    unittest.main(verbosity=2)
