#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime
from pathlib import Path
from typing import Any

SCHEMA_VERSION = 1
WORKFLOW_DIR = ".workflow"
DEFAULT_TIMEOUT = 600
OUTPUT_TAIL_LINES = 40
OUTPUT_TAIL_CHARS = 6000
TASK_STATUSES = ("todo", "doing", "done", "blocked", "cancelled")
STATUS_LABELS = {
    "todo": "待办",
    "doing": "进行中",
    "done": "完成",
    "blocked": "阻塞",
    "cancelled": "取消",
}
SKILL_ROOT = Path(__file__).resolve().parent.parent
ASSETS_DIR = SKILL_ROOT / "assets"
TEMPLATES_DIR = ASSETS_DIR / "templates"
DEFAULT_CONFIG_PATH = ASSETS_DIR / "config.default.json"


class WorkflowError(Exception):
    pass


def _configure_stdio() -> None:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure:
            reconfigure(encoding="utf-8", errors="replace")


def _now() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


def _stamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S")


def _print_json(data: Any) -> None:
    print(json.dumps(data, ensure_ascii=False, indent=2))


def _atomic_write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(
        dir=str(path.parent), prefix=f".{path.name}.", suffix=".tmp"
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def _read_json(path: Path, label: str) -> Any:
    try:
        with path.open(encoding="utf-8") as handle:
            return json.load(handle)
    except FileNotFoundError as exc:
        raise WorkflowError(f"{label}不存在: {path}") from exc
    except json.JSONDecodeError as exc:
        raise WorkflowError(
            f"{label}无法解析: {path}（{exc.msg}，第 {exc.lineno} 行）"
        ) from exc


def _safe_rel(value: Any, label: str) -> Path:
    if not isinstance(value, str) or not value.strip():
        raise WorkflowError(f"{label} 必须是非空相对路径")
    path = Path(value)
    if (
        path.is_absolute()
        or path.drive
        or path == Path(".")
        or any(part == ".." for part in path.parts)
    ):
        raise WorkflowError(f"{label} 必须是工作区内的相对路径: {value}")
    return path


def _resolve_root(value: str | None) -> Path:
    if value:
        return Path(value).expanduser().resolve()
    current = Path.cwd().resolve()
    for candidate in (current, *current.parents):
        if (candidate / WORKFLOW_DIR / "config.json").is_file():
            return candidate
    return current


def _clip(value: str, limit: int = 160) -> str:
    value = " ".join(str(value).split())
    return value if len(value) <= limit else value[: limit - 1] + "…"


def _dedupe_blockers(blockers: list[dict[str, str]]) -> list[dict[str, str]]:
    seen: set[tuple[str, str]] = set()
    unique: list[dict[str, str]] = []
    for blocker in blockers:
        key = (blocker.get("code", ""), blocker.get("message", ""))
        if key not in seen:
            seen.add(key)
            unique.append(blocker)
    return unique


def _blocker(code: str, message: str) -> dict[str, str]:
    return {"code": code, "message": message}


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------


def _validate_commands(commands: Any, label: str) -> None:
    if not isinstance(commands, list):
        raise WorkflowError(f"配置 {label} 必须是数组")
    seen: set[str] = set()
    for index, item in enumerate(commands):
        where = f"{label}[{index}]"
        if not isinstance(item, dict):
            raise WorkflowError(f"配置 {where} 必须是对象")
        name = item.get("name")
        if not isinstance(name, str) or not name.strip():
            raise WorkflowError(f"配置 {where}.name 必须是非空字符串")
        if name in seen:
            raise WorkflowError(f"配置 {label} 中存在重复命令名: {name}")
        seen.add(name)
        command = item.get("cmd")
        valid_string = isinstance(command, str) and bool(command.strip())
        valid_list = (
            isinstance(command, list)
            and bool(command)
            and all(isinstance(part, str) and part != "" for part in command)
        )
        if not (valid_string or valid_list):
            raise WorkflowError(f"配置 {where}.cmd 必须是非空字符串或字符串数组")
        timeout = item.get("timeout", DEFAULT_TIMEOUT)
        if not isinstance(timeout, (int, float)) or isinstance(timeout, bool) or timeout <= 0:
            raise WorkflowError(f"配置 {where}.timeout 必须是正数")
        if item.get("cwd") is not None:
            _safe_rel(item["cwd"], f"{where}.cwd")


def _validate_config(config: Any) -> None:
    if not isinstance(config, dict):
        raise WorkflowError("配置根节点必须是对象")
    if config.get("schema_version") != SCHEMA_VERSION:
        raise WorkflowError(f"配置 schema_version 必须为 {SCHEMA_VERSION}")
    stages = config.get("stages")
    if not isinstance(stages, list) or not stages:
        raise WorkflowError("配置 stages 必须是非空数组")
    seen_ids: set[str] = set()
    for index, stage in enumerate(stages):
        where = f"stages[{index}]"
        if not isinstance(stage, dict):
            raise WorkflowError(f"配置 {where} 必须是对象")
        stage_id = stage.get("id")
        if not isinstance(stage_id, str) or not re.fullmatch(
            r"[a-z0-9][a-z0-9-]*", stage_id
        ):
            raise WorkflowError(f"配置 {where}.id 必须是小写字母、数字或连字符")
        if stage_id in seen_ids:
            raise WorkflowError(f"配置中存在重复阶段 id: {stage_id}")
        seen_ids.add(stage_id)
        if not isinstance(stage.get("title"), str) or not stage["title"].strip():
            raise WorkflowError(f"配置 {where}.title 必须是非空字符串")
        artifacts = stage.get("artifacts", [])
        if not isinstance(artifacts, list):
            raise WorkflowError(f"配置 {where}.artifacts 必须是数组")
        for artifact_index, artifact in enumerate(artifacts):
            artifact_where = f"{where}.artifacts[{artifact_index}]"
            if not isinstance(artifact, dict):
                raise WorkflowError(f"配置 {artifact_where} 必须是对象")
            _safe_rel(artifact.get("path"), f"{artifact_where}.path")
            sections = artifact.get("sections", [])
            if not isinstance(sections, list) or not all(
                isinstance(section, str) and section.strip() for section in sections
            ):
                raise WorkflowError(f"配置 {artifact_where}.sections 必须是字符串数组")
            if artifact.get("template") is not None:
                if not isinstance(artifact["template"], str):
                    raise WorkflowError(f"配置 {artifact_where}.template 必须是字符串")
                _safe_rel(artifact["template"], f"{artifact_where}.template")
        verdicts = stage.get("verdicts", [])
        if not isinstance(verdicts, list):
            raise WorkflowError(f"配置 {where}.verdicts 必须是数组")
        for verdict_index, verdict in enumerate(verdicts):
            verdict_where = f"{where}.verdicts[{verdict_index}]"
            if not isinstance(verdict, dict):
                raise WorkflowError(f"配置 {verdict_where} 必须是对象")
            _safe_rel(verdict.get("path"), f"{verdict_where}.path")
            fields = verdict.get("fields")
            if not isinstance(fields, dict) or not fields:
                raise WorkflowError(f"配置 {verdict_where}.fields 必须是非空对象")
            for field, allowed in fields.items():
                if not isinstance(field, str) or not field.strip():
                    raise WorkflowError(f"配置 {verdict_where}.fields 存在空字段名")
                if not isinstance(allowed, list) or not allowed or not all(
                    isinstance(value, str) and value.strip() for value in allowed
                ):
                    raise WorkflowError(
                        f"配置 {verdict_where}.fields.{field} 必须是非空字符串数组"
                    )
        min_tasks = stage.get("min_tasks", 0)
        if (
            not isinstance(min_tasks, int)
            or isinstance(min_tasks, bool)
            or min_tasks < 0
        ):
            raise WorkflowError(f"配置 {where}.min_tasks 必须是非负整数")
        for flag in (
            "approval",
            "require_tasks_complete",
            "require_verification",
            "require_red_verification",
        ):
            if flag in stage and not isinstance(stage[flag], bool):
                raise WorkflowError(f"配置 {where}.{flag} 必须是布尔值")

    validation = config.get("validation", {})
    if not isinstance(validation, dict):
        raise WorkflowError("配置 validation 必须是对象")
    placeholders = validation.get("placeholders", ["待填写"])
    if not isinstance(placeholders, list) or not all(
        isinstance(item, str) and item for item in placeholders
    ):
        raise WorkflowError("配置 validation.placeholders 必须是字符串数组")
    min_section_chars = validation.get("min_section_chars", 1)
    if (
        not isinstance(min_section_chars, int)
        or isinstance(min_section_chars, bool)
        or min_section_chars < 1
    ):
        raise WorkflowError("配置 validation.min_section_chars 必须是正整数")
    _validate_commands(config.get("commands", []), "commands")
    _validate_commands(config.get("red_commands", []), "red_commands")
    timeout_seconds = config.get("timeout_seconds", DEFAULT_TIMEOUT)
    if (
        not isinstance(timeout_seconds, (int, float))
        or isinstance(timeout_seconds, bool)
        or timeout_seconds <= 0
    ):
        raise WorkflowError("配置 timeout_seconds 必须是正数")


def _load_config(root: Path) -> dict[str, Any]:
    path = root / WORKFLOW_DIR / "config.json"
    if not path.is_file():
        raise WorkflowError(f"未找到配置: {path}，请先运行 init")
    config = _read_json(path, "配置")
    _validate_config(config)
    return config


def _stage_index(config: dict[str, Any], stage_id: str) -> int:
    for index, stage in enumerate(config["stages"]):
        if stage["id"] == stage_id:
            return index
    raise WorkflowError(f"未知阶段: {stage_id}")


def _find_stage(config: dict[str, Any], stage_id: str | None) -> dict[str, Any] | None:
    if stage_id is None:
        return None
    for stage in config["stages"]:
        if stage["id"] == stage_id:
            return stage
    return None


# ---------------------------------------------------------------------------
# Runs and state
# ---------------------------------------------------------------------------


def _read_state(run_dir: Path) -> dict[str, Any]:
    state = _read_json(run_dir / "state.json", "运行状态")
    if not isinstance(state, dict) or state.get("schema_version") != SCHEMA_VERSION:
        raise WorkflowError(f"运行状态格式无效: {run_dir / 'state.json'}")
    return state


def _write_state(run_dir: Path, state: dict[str, Any]) -> None:
    state["updated_at"] = _now()
    _atomic_write(
        run_dir / "state.json",
        json.dumps(state, ensure_ascii=False, indent=2) + "\n",
    )


def _active_dir(root: Path) -> Path:
    return root / WORKFLOW_DIR / "active"


def _archive_dir(root: Path) -> Path:
    return root / WORKFLOW_DIR / "archive"


def _iter_run_states(base: Path) -> list[tuple[Path, dict[str, Any]]]:
    if not base.is_dir():
        return []
    runs: list[tuple[Path, dict[str, Any]]] = []
    for run_dir in sorted(base.iterdir()):
        if not run_dir.is_dir() or not (run_dir / "state.json").is_file():
            continue
        try:
            runs.append((run_dir, _read_state(run_dir)))
        except WorkflowError:
            continue
    return runs


def _resolve_run(root: Path, run_slug: str | None) -> tuple[Path, dict[str, Any]]:
    active = _active_dir(root)
    if run_slug:
        candidate = active / run_slug
        if candidate.is_dir() and (candidate / "state.json").is_file():
            return candidate, _read_state(candidate)
        archived_candidates: list[tuple[Path, dict[str, Any]]] = []
        for archive_run, state in _iter_run_states(_archive_dir(root)):
            if state.get("slug") == run_slug:
                archived_candidates.append((archive_run, state))
        if archived_candidates:
            archived_candidates.sort(key=lambda item: item[0].name, reverse=True)
            return archived_candidates[0]
        raise WorkflowError(f"运行不存在: {run_slug}")

    active_runs = _iter_run_states(active)
    if len(active_runs) == 0:
        broken = (
            [path for path in active.iterdir() if path.is_dir()] if active.is_dir() else []
        )
        if broken:
            raise WorkflowError(
                f"无法读取进行中的运行状态: {broken[0] / 'state.json'}"
            )
        archived = _iter_run_states(_archive_dir(root))
        suffix = f"（已有 {len(archived)} 个归档运行，可使用 list 查看）" if archived else ""
        raise WorkflowError(f"没有进行中的运行{suffix}，请先运行 init")
    if len(active_runs) > 1:
        slugs = ", ".join(state.get("slug", run_dir.name) for run_dir, state in active_runs)
        raise WorkflowError(f"存在多个进行中的运行: {slugs}；请使用 --run <slug> 指定")
    return active_runs[0]


def _ensure_active(state: dict[str, Any]) -> None:
    status = state.get("status")
    if status == "archived":
        raise WorkflowError("该运行已归档，不能继续修改")
    if status == "done":
        raise WorkflowError("该运行已完成；如需继续修改，请先使用 rewind 回到较早阶段")
    if status != "active":
        raise WorkflowError(f"运行状态不可写: {status}")


# ---------------------------------------------------------------------------
# Markdown artifacts
# ---------------------------------------------------------------------------


def _normalize_heading(value: str) -> str:
    value = value.strip().strip("#").strip()
    value = re.sub(r"^\d+[.、)]\s*", "", value)
    return value.rstrip("：:").strip()


def _section_map(text: str) -> dict[str, str]:
    lines = text.splitlines()
    headings: list[tuple[int, int, str]] = []
    in_fence = False
    for index, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("```") or stripped.startswith("~~~"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        match = re.match(r"^(#{1,6})\s+(.+?)\s*$", line)
        if match:
            headings.append(
                (index, len(match.group(1)), _normalize_heading(match.group(2)))
            )
    sections: dict[str, str] = {}
    for position, (line_index, level, title) in enumerate(headings):
        end = len(lines)
        for next_index, next_level, _ in headings[position + 1 :]:
            if next_level <= level:
                end = next_index
                break
        body = "\n".join(lines[line_index + 1 : end])
        sections.setdefault(title, body)
    return sections


def _strip_comments(text: str) -> str:
    return re.sub(r"<!--.*?-->", "", text, flags=re.DOTALL)


def _content_chars(text: str) -> str:
    text = _strip_comments(text)
    kept: list[str] = []
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        if re.fullmatch(r"\|?[\s:|\-]+\|?", stripped):
            continue
        kept.append(stripped)
    joined = "\n".join(kept)
    return re.sub(r"[\s*_`#>|\\\-\[\]()]+", "", joined)


def _validate_artifact(
    run_dir: Path, artifact: dict[str, Any], config: dict[str, Any]
) -> list[dict[str, str]]:
    relative = Path(artifact["path"])
    path = run_dir / relative
    if not path.is_file():
        return [_blocker("missing_artifact", f"缺少产物: {relative.as_posix()}")]
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        return [_blocker("unreadable_artifact", f"无法读取产物 {relative}: {exc}")]
    sections = _section_map(text)
    validation = config.get("validation", {})
    placeholders = validation.get("placeholders", ["待填写"])
    min_chars = validation.get("min_section_chars", 1)
    blockers: list[dict[str, str]] = []
    for required in artifact.get("sections", []):
        normalized = _normalize_heading(required)
        if normalized not in sections:
            blockers.append(
                _blocker(
                    "missing_section",
                    f"{relative.as_posix()} 缺少章节: {required}",
                )
            )
            continue
        body = sections[normalized]
        visible = _strip_comments(body)
        matched = next((item for item in placeholders if item in visible), None)
        if matched:
            blockers.append(
                _blocker(
                    "placeholder_content",
                    f"{relative.as_posix()} 章节「{required}」仍包含占位内容「{matched}」",
                )
            )
            continue
        if len(_content_chars(body)) < min_chars:
            blockers.append(
                _blocker(
                    "empty_section",
                    f"{relative.as_posix()} 章节「{required}」内容为空",
                )
            )
    return blockers


def _validate_verdict(
    run_dir: Path, verdict: dict[str, Any]
) -> list[dict[str, str]]:
    relative = Path(verdict["path"])
    path = run_dir / relative
    if not path.is_file():
        return [_blocker("missing_artifact", f"缺少产物: {relative.as_posix()}")]
    text = path.read_text(encoding="utf-8")
    blockers: list[dict[str, str]] = []
    for field, allowed in verdict["fields"].items():
        pattern = re.compile(
            rf"^\s*[-*]?\s*{re.escape(field)}\s*[:：]\s*(.+?)\s*$", re.MULTILINE
        )
        match = pattern.search(text)
        if not match:
            blockers.append(
                _blocker(
                    "missing_verdict_field",
                    f"{relative.as_posix()} 缺少评审字段「{field}」",
                )
            )
            continue
        actual = match.group(1).strip()
        normalized = actual.strip("`*_\"'“”‘’ ")
        normalized = normalized.rstrip("。；;，,")
        allowed_normalized = [item.strip().casefold() for item in allowed]
        if normalized.casefold() not in allowed_normalized:
            blockers.append(
                _blocker(
                    "verdict_failed",
                    f"{relative.as_posix()} 字段「{field}」期望 {' / '.join(allowed)}，实际: {actual}",
                )
            )
    return blockers


# ---------------------------------------------------------------------------
# Tasks and verification
# ---------------------------------------------------------------------------


def _task_stats(tasks: list[dict[str, Any]]) -> dict[str, Any]:
    counts = {status: 0 for status in TASK_STATUSES}
    for task in tasks:
        status = task.get("status", "todo")
        if status in counts:
            counts[status] += 1
    unfinished = [
        task["id"] for task in tasks if task.get("status") in ("todo", "doing", "blocked")
    ]
    return {
        "total": len(tasks),
        **counts,
        "complete": len(unfinished) == 0,
        "unfinished": unfinished,
    }


def _find_task(tasks: list[dict[str, Any]], task_id: str) -> dict[str, Any]:
    for task in tasks:
        if task.get("id") == task_id:
            return task
    raise WorkflowError(f"任务不存在: {task_id}")


def _parse_csv(value: str | None) -> list[str]:
    if not value:
        return []
    result: list[str] = []
    for item in value.split(","):
        item = item.strip()
        if item and item not in result:
            result.append(item)
    return result


def _validate_dependencies(
    tasks: list[dict[str, Any]], deps: list[str], current_id: str | None = None
) -> None:
    known = {task["id"] for task in tasks}
    for dependency in deps:
        if dependency == current_id:
            raise WorkflowError("任务不能依赖自身")
        if dependency not in known:
            raise WorkflowError(f"依赖任务不存在: {dependency}")
    if current_id and _would_cycle(tasks, current_id, deps):
        raise WorkflowError("任务依赖存在循环")


def _would_cycle(
    tasks: list[dict[str, Any]], task_id: str, new_deps: list[str]
) -> bool:
    graph = {
        task["id"]: list(new_deps if task["id"] == task_id else task.get("deps", []))
        for task in tasks
    }
    seen: set[str] = set()
    stack = list(new_deps)
    while stack:
        current = stack.pop()
        if current == task_id:
            return True
        if current in seen:
            continue
        seen.add(current)
        stack.extend(graph.get(current, []))
    return False


def _mark_verification_stale(state: dict[str, Any]) -> None:
    record = state.get("verification")
    if isinstance(record, dict):
        record["stale"] = True


def _command_fingerprint(commands: list[dict[str, Any]]) -> str:
    payload = json.dumps(commands, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _verification_summary(
    config: dict[str, Any],
    state: dict[str, Any],
    run_dir: Path,
    kind: str,
) -> dict[str, Any]:
    commands_key = "red_commands" if kind == "red" else "commands"
    record_key = "red_verification" if kind == "red" else "verification"
    commands = config.get(commands_key, [])
    record = state.get(record_key)
    summary: dict[str, Any] = {
        "configured": len(commands),
        "applicable": bool(commands),
        "exists": isinstance(record, dict),
        "ok": False,
        "stale": False,
        "stale_reason": None,
        "at": None,
        "evidence": None,
    }
    if not isinstance(record, dict):
        return summary
    summary["at"] = record.get("at")
    summary["evidence"] = record.get("evidence")
    if record.get("stale"):
        summary["stale"] = True
        summary["stale_reason"] = "任务已变更"
    elif record.get("command_fingerprint") != _command_fingerprint(commands):
        summary["stale"] = True
        summary["stale_reason"] = "验证命令已变更"
    else:
        evidence = record.get("evidence")
        try:
            evidence_path = _safe_rel(evidence, "验证证据路径")
        except WorkflowError:
            summary["stale"] = True
            summary["stale_reason"] = "验证证据路径无效"
        else:
            if not (run_dir / evidence_path).is_file():
                summary["stale"] = True
                summary["stale_reason"] = "验证证据缺失"
    summary["ok"] = bool(record.get("ok")) and not summary["stale"]
    return summary


def _verification_blockers(
    config: dict[str, Any], state: dict[str, Any], run_dir: Path, kind: str
) -> list[dict[str, str]]:
    commands_key = "red_commands" if kind == "red" else "commands"
    commands = config.get(commands_key, [])
    label = "先行测试验证" if kind == "red" else "验证"
    if not commands:
        if kind == "red":
            return []
        return [
            _blocker(
                "no_verification_commands",
                "未配置验证命令：请在 .workflow/config.json 的 commands 中添加至少一个命令",
            )
        ]
    summary = _verification_summary(config, state, run_dir, kind)
    if not summary["exists"]:
        if kind == "red":
            return [
                _blocker(
                    "red_verification_missing",
                    "尚未记录先行测试的预期失败证据（运行 workflow.py verify --red）",
                )
            ]
        return [_blocker("verification_missing", "尚未运行验证（运行 workflow.py verify）")]
    if summary["stale"]:
        reason = summary["stale_reason"] or "任务或配置已变更"
        if kind == "red":
            return [
                _blocker(
                    "red_verification_stale",
                    f"先行测试证据已过期（{reason}），请重新运行 verify --red",
                )
            ]
        return [
            _blocker(
                "verification_stale",
                f"验证结果已过期（{reason}），请重新运行 verify",
            )
        ]
    if not summary["ok"]:
        if kind == "red":
            return [
                _blocker(
                    "red_verification_failed",
                    "先行测试验证未通过：未观察到预期失败",
                )
            ]
        return [
            _blocker("verification_failed", "验证未通过：请修复失败命令后重新运行 verify")
        ]
    return []


def _all_artifact_blockers(
    config: dict[str, Any], run_dir: Path
) -> list[dict[str, str]]:
    blockers: list[dict[str, str]] = []
    for stage in config["stages"]:
        for artifact in stage.get("artifacts", []):
            blockers.extend(_validate_artifact(run_dir, artifact, config))
        for verdict in stage.get("verdicts", []):
            blockers.extend(_validate_verdict(run_dir, verdict))
    return _dedupe_blockers(blockers)


def _stage_blockers(
    run_dir: Path,
    state: dict[str, Any],
    config: dict[str, Any],
    stage: dict[str, Any],
    approve: str | None = None,
) -> list[dict[str, str]]:
    blockers: list[dict[str, str]] = []
    for artifact in stage.get("artifacts", []):
        blockers.extend(_validate_artifact(run_dir, artifact, config))
    for verdict in stage.get("verdicts", []):
        blockers.extend(_validate_verdict(run_dir, verdict))
    tasks = state.get("tasks", [])
    min_tasks = stage.get("min_tasks", 0)
    if len(tasks) < min_tasks:
        blockers.append(
            _blocker(
                "min_tasks",
                f"任务清单至少需要 {min_tasks} 个任务（当前 {len(tasks)} 个）",
            )
        )
    if stage.get("require_tasks_complete"):
        unfinished = [
            task for task in tasks if task.get("status") in ("todo", "doing", "blocked")
        ]
        if unfinished:
            detail = ", ".join(
                f"{task['id']}（{task.get('status')}）" for task in unfinished
            )
            blockers.append(
                _blocker("unfinished_tasks", f"仍有未完成任务: {detail}")
            )
    if stage.get("require_verification"):
        blockers.extend(_verification_blockers(config, state, run_dir, "verify"))
    if stage.get("require_red_verification"):
        blockers.extend(_verification_blockers(config, state, run_dir, "red"))
    if stage.get("approval") and not approve:
        blockers.append(
            _blocker(
                "approval_required",
                "该阶段需要人工审批，请使用 advance --approve <审批人>",
            )
        )
    return _dedupe_blockers(blockers)


def _run_commands(
    commands: list[dict[str, Any]],
    root: Path,
    default_timeout: float,
) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    for spec in commands:
        command = spec["cmd"]
        timeout = spec.get("timeout", default_timeout)
        cwd = root
        if spec.get("cwd"):
            cwd = (root / Path(spec["cwd"])).resolve()
        started = time.monotonic()
        stdout = ""
        stderr = ""
        exit_code: int | None = None
        error: str | None = None
        timed_out = False
        try:
            completed = subprocess.run(
                command,
                cwd=str(cwd),
                shell=isinstance(command, str),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout,
            )
            exit_code = completed.returncode
            stdout = completed.stdout or ""
            stderr = completed.stderr or ""
        except subprocess.TimeoutExpired as exc:
            timed_out = True
            error = f"超时（{timeout}s）"
            stdout = _decode_timeout_output(exc.stdout)
            stderr = _decode_timeout_output(exc.stderr)
        except OSError as exc:
            error = str(exc)
        duration_ms = int((time.monotonic() - started) * 1000)
        result = {
            "name": spec["name"],
            "cmd": command,
            "cwd": spec.get("cwd"),
            "exit_code": exit_code,
            "timed_out": timed_out,
            "error": error,
            "duration_ms": duration_ms,
            "ok": exit_code == 0 and not timed_out and error is None,
            "output_tail": _output_tail(stdout, stderr),
        }
        results.append(result)
    return results


def _decode_timeout_output(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return str(value)


def _output_tail(stdout: str, stderr: str) -> str:
    parts = []
    if stdout.strip():
        parts.append(stdout.rstrip())
    if stderr.strip():
        parts.append("[stderr]\n" + stderr.rstrip())
    joined = "\n".join(parts)
    lines = joined.splitlines()
    tail = "\n".join(lines[-OUTPUT_TAIL_LINES:])
    return tail[-OUTPUT_TAIL_CHARS:]


# ---------------------------------------------------------------------------
# Rendering and logging
# ---------------------------------------------------------------------------


def _append_to_section(text: str, section: str, line: str) -> str:
    lines = text.splitlines()
    start: int | None = None
    level = 2
    for index, current in enumerate(lines):
        match = re.match(r"^(#{1,6})\s+(.+?)\s*$", current)
        if match and _normalize_heading(match.group(2)) == _normalize_heading(section):
            start = index
            level = len(match.group(1))
            break
    if start is None:
        return text.rstrip() + f"\n\n## {section}\n{line}\n"
    end = len(lines)
    for index in range(start + 1, len(lines)):
        match = re.match(r"^(#{1,6})\s+", lines[index])
        if match and len(match.group(1)) <= level:
            end = index
            break
    insert_at = end
    while insert_at > start + 1 and not lines[insert_at - 1].strip():
        insert_at -= 1
    lines.insert(insert_at, line)
    return "\n".join(lines) + "\n"


def _append_build_log(run_dir: Path, section: str, line: str) -> None:
    path = run_dir / "build-log.md"
    if not path.is_file():
        return
    text = path.read_text(encoding="utf-8")
    _atomic_write(path, _append_to_section(text, section, line))


def _render_template(text: str, title: str, slug: str, created_at: str) -> str:
    replacements = {
        "{{title}}": title,
        "{{slug}}": slug,
        "{{created_at}}": created_at,
    }
    for key, value in replacements.items():
        text = text.replace(key, value)
    return text.rstrip() + "\n"


def _render_tasks(
    run_dir: Path, state: dict[str, Any], config: dict[str, Any]
) -> None:
    tasks = state.get("tasks", [])
    stats = _task_stats(tasks)
    stage = _find_stage(config, state.get("current_stage"))
    if state.get("status") == "done":
        stage_label = "已收尾（待归档）"
    elif state.get("status") == "archived":
        stage_label = "已归档"
    elif stage:
        stage_label = f"{stage['title']}（{stage['id']}）"
    else:
        stage_label = "未知阶段"
    lines = [
        f"# 实施任务清单：{state.get('title', '')}",
        "",
        "> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。",
        "",
        "## 任务清单",
        "",
        f"阶段：{stage_label} ｜ 完成 {stats['done']}/{stats['total']}"
        f"（取消 {stats['cancelled']}，阻塞 {stats['blocked']}）",
        "",
        "| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |",
        "|---|---|---|---|---|---|---|",
    ]
    for task in tasks:
        lines.append(
            "| {id} | {title} | {deps} | {status} | {acceptance} | {verify} | {evidence} |".format(
                id=_cell(task.get("id")),
                title=_cell(task.get("title")),
                deps=_cell(", ".join(task.get("deps", [])) or "-"),
                status=STATUS_LABELS.get(task.get("status"), task.get("status", "")),
                acceptance=_cell(task.get("acceptance")),
                verify=_cell(task.get("verify")),
                evidence=_cell(_clip(task.get("evidence", ""), 120)),
            )
        )
    if not tasks:
        lines.append("| - | 暂无任务 | - | - | - | - | - |")
    details: list[str] = []
    for task in tasks:
        description = str(task.get("description") or "").strip()
        files = task.get("files") or []
        reason = str(task.get("status_reason") or "").strip()
        if not description and not files and not reason:
            continue
        details.extend([f"### {task.get('id')} · {task.get('title')}", ""])
        if description:
            details.extend([f"- 描述: {description}", ""])
        if files:
            details.extend([f"- 文件: {', '.join(str(item) for item in files)}", ""])
        if reason:
            details.extend([f"- 状态理由: {reason}", ""])
    if details:
        lines.extend(["", "## 任务详情", ""])
        lines.extend(details)
    lines.append("")
    _atomic_write(run_dir / "tasks.md", "\n".join(lines))


def _cell(value: Any) -> str:
    text = str(value if value not in (None, "") else "-")
    return text.replace("|", "\\|").replace("\n", "<br>").strip() or "-"


# ---------------------------------------------------------------------------
# Artifact bootstrapping
# ---------------------------------------------------------------------------


def _artifact_specs(config: dict[str, Any]) -> dict[Path, dict[str, Any]]:
    specs: dict[Path, dict[str, Any]] = {}
    for stage in config["stages"]:
        for artifact in stage.get("artifacts", []):
            relative = Path(artifact["path"])
            spec = specs.setdefault(
                relative,
                {
                    "path": relative,
                    "sections": [],
                    "template": None,
                    "verdict_fields": [],
                    "stage_title": stage["title"],
                },
            )
            for section in artifact.get("sections", []):
                if section not in spec["sections"]:
                    spec["sections"].append(section)
            if not spec["template"] and artifact.get("template"):
                spec["template"] = artifact["template"]
        for verdict in stage.get("verdicts", []):
            relative = Path(verdict["path"])
            spec = specs.setdefault(
                relative,
                {
                    "path": relative,
                    "sections": [],
                    "template": None,
                    "verdict_fields": [],
                    "stage_title": stage["title"],
                },
            )
            for field in verdict["fields"]:
                if field not in spec["verdict_fields"]:
                    spec["verdict_fields"].append(field)
    return specs


def _auto_stub(spec: dict[str, Any], title: str, slug: str, created_at: str) -> str:
    sections = list(spec["sections"])
    if spec["verdict_fields"] and _normalize_heading("评审结论") not in [
        _normalize_heading(item) for item in sections
    ]:
        sections.append("评审结论")
    lines = [
        f"# {spec['stage_title']}：{title}",
        "",
        f"> 运行 {slug} · 创建于 {created_at}",
        "",
    ]
    for section in sections:
        lines.extend([f"## {section}", ""])
        if _normalize_heading(section) == _normalize_heading("评审结论"):
            for field in spec["verdict_fields"]:
                lines.append(f"- {field}: 待填写")
        else:
            lines.append("待填写")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def _bootstrap_artifacts(
    run_dir: Path,
    config: dict[str, Any],
    title: str,
    slug: str,
    created_at: str,
    project_templates: Path,
) -> None:
    specs = _artifact_specs(config)
    for relative, spec in specs.items():
        if relative == Path("tasks.md"):
            continue
        path = run_dir / relative
        if path.exists():
            continue
        template_name = spec.get("template")
        if template_name:
            project_template = project_templates / Path(template_name)
            template_path = (
                project_template if project_template.is_file() else TEMPLATES_DIR / Path(template_name)
            )
            if not template_path.is_file():
                raise WorkflowError(
                    f"模板不存在: {template_path}（阶段「{spec['stage_title']}」引用 {template_name}）"
                )
            text = _render_template(
                template_path.read_text(encoding="utf-8"), title, slug, created_at
            )
        else:
            text = _auto_stub(spec, title, slug, created_at)
        _atomic_write(path, text)


# ---------------------------------------------------------------------------
# Human-readable status
# ---------------------------------------------------------------------------


def _run_payload(
    config: dict[str, Any],
    run_dir: Path,
    state: dict[str, Any],
    blockers: list[dict[str, str]] | None = None,
) -> dict[str, Any]:
    current_stage = _find_stage(config, state.get("current_stage"))
    stage_payload = None
    if current_stage and state.get("status") == "active":
        stage_index = _stage_index(config, current_stage["id"])
        stage_payload = {
            "id": current_stage["id"],
            "title": current_stage["title"],
            "index": stage_index + 1,
            "total": len(config["stages"]),
        }
    tasks = state.get("tasks", [])
    return {
        "slug": state.get("slug"),
        "title": state.get("title"),
        "status": state.get("status"),
        "stage": stage_payload,
        "completed_stages": [
            {
                key: value
                for key, value in completed.items()
                if key in ("id", "title", "completed_at", "approved_by")
            }
            for completed in state.get("completed_stages", [])
        ],
        "blockers": blockers or [],
        "tasks": _task_stats(tasks),
        "verification": _verification_summary(config, state, run_dir, "verify"),
        "red_verification": _verification_summary(config, state, run_dir, "red"),
        "created_at": state.get("created_at"),
        "updated_at": state.get("updated_at"),
        "completed_at": state.get("completed_at"),
        "run_dir": str(run_dir),
    }


def _next_action(payload: dict[str, Any]) -> str:
    if payload["status"] == "done":
        return "运行 archive 归档本次交付"
    if payload["status"] == "archived":
        return "本次运行已归档，无需继续操作"
    codes = {blocker["code"] for blocker in payload["blockers"]}
    if not codes:
        return "运行 advance 完成当前阶段"
    if "approval_required" in codes:
        return "获得人工批准后运行 advance --approve <审批人>"
    verification_codes = {
        "no_verification_commands",
        "verification_missing",
        "verification_stale",
        "verification_failed",
    }
    red_codes = {
        "red_verification_missing",
        "red_verification_stale",
        "red_verification_failed",
    }
    if codes & red_codes:
        return "补齐并运行先行测试（verify --red），确认观察到预期失败"
    if codes & verification_codes:
        return "配置并运行 verify，修复失败项后重试 advance"
    if codes & {"unfinished_tasks", "min_tasks"}:
        return "完善任务清单并完成所有任务后重试 advance"
    return "修正上述产物或评审问题后重试 advance"


def _print_status(payload: dict[str, Any]) -> None:
    stage = payload["stage"]
    if payload["status"] == "done":
        stage_text = f"已完成（{len(payload['completed_stages'])} 个阶段）"
    elif payload["status"] == "archived":
        stage_text = "已归档"
    elif stage:
        stage_text = f"{stage['index']}/{stage['total']} {stage['title']}（{stage['id']}）"
    else:
        stage_text = "未知"
    tasks = payload["tasks"]
    verification = payload["verification"]
    if not verification["configured"]:
        verification_text = "未配置命令"
    elif not verification["exists"]:
        verification_text = "未运行"
    elif verification["stale"]:
        verification_text = f"已过期（{verification.get('stale_reason') or '需重跑'}）"
    elif verification["ok"]:
        verification_text = f"PASS（{verification['at']}）"
    else:
        verification_text = "FAIL"
    print(f"运行: {payload['slug']} · {payload['title']}")
    print(f"状态: {payload['status']} ｜ 阶段: {stage_text}")
    print(
        "任务: 完成 {done}/{total}，进行中 {doing}，待办 {todo}，阻塞 {blocked}，取消 {cancelled}".format(
            **tasks
        )
    )
    print(f"验证: {verification_text}")
    if payload["red_verification"]["configured"]:
        red = payload["red_verification"]
        red_text = (
            "PASS"
            if red["ok"]
            else ("已过期" if red["stale"] else ("FAIL" if red["exists"] else "未运行"))
        )
        print(f"先行测试: {red_text}")
    if payload["blockers"]:
        print(f"门禁: {len(payload['blockers'])} 项未满足")
        for blocker in payload["blockers"]:
            print(f"- [{blocker['code']}] {blocker['message']}")
    else:
        print("门禁: 通过" if payload["status"] == "active" else "门禁: -")
    print(f"下一步: {_next_action(payload)}")


def _print_blockers(blockers: list[dict[str, str]]) -> None:
    print(f"未满足阶段门禁（{len(blockers)} 项）：", file=sys.stderr)
    for blocker in blockers:
        print(f"- [{blocker['code']}] {blocker['message']}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------


def cmd_init(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    slug = args.slug.strip()
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", slug):
        raise WorkflowError("运行标识必须是小写字母、数字或连字符，长度 1-64")
    title = args.title.strip()
    if not title:
        raise WorkflowError("标题不能为空")
    workflow_dir = root / WORKFLOW_DIR
    config_path = workflow_dir / "config.json"
    config_created = False
    if not config_path.is_file():
        if not DEFAULT_CONFIG_PATH.is_file():
            raise WorkflowError(f"默认配置缺失: {DEFAULT_CONFIG_PATH}")
        config_text = DEFAULT_CONFIG_PATH.read_text(encoding="utf-8")
        config = json.loads(config_text)
        _validate_config(config)
        _atomic_write(config_path, config_text.rstrip() + "\n")
        config_created = True
    config = _load_config(root)

    run_dir = _active_dir(root) / slug
    if run_dir.exists():
        raise WorkflowError(f"运行已存在: {slug}")
    run_dir.mkdir(parents=True)
    try:
        (run_dir / "evidence").mkdir()
        created_at = _now()
        first_stage = config["stages"][0]
        state = {
            "schema_version": SCHEMA_VERSION,
            "slug": slug,
            "title": title,
            "status": "active",
            "current_stage": first_stage["id"],
            "created_at": created_at,
            "updated_at": created_at,
            "completed_at": None,
            "completed_stages": [],
            "history": [{"at": created_at, "event": "init", "stage": first_stage["id"]}],
            "tasks": [],
            "verification": None,
            "red_verification": None,
        }
        _bootstrap_artifacts(
            run_dir,
            config,
            title,
            slug,
            created_at,
            workflow_dir / "templates",
        )
        _write_state(run_dir, state)
        _render_tasks(run_dir, state, config)
        _append_build_log(
            run_dir,
            "阶段流转",
            f"- {created_at} · 初始化运行，当前阶段「{first_stage['title']}」",
        )
    except Exception:
        shutil.rmtree(run_dir, ignore_errors=True)
        raise
    print(f"已初始化运行: {slug}")
    print(f"标题: {title}")
    print(f"运行目录: {run_dir}")
    print(f"当前阶段: 1/{len(config['stages'])} {first_stage['title']}（{first_stage['id']}）")
    if config_created:
        print(f"配置: {config_path}")
    print("下一步: 填写产物后运行 advance")
    return 0


def cmd_status(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    blockers: list[dict[str, str]] = []
    stage = _find_stage(config, state.get("current_stage"))
    if state.get("status") == "active" and stage:
        blockers = _stage_blockers(run_dir, state, config, stage)
    payload = _run_payload(config, run_dir, state, blockers)
    if args.json:
        _print_json(payload)
    else:
        _print_status(payload)
    return 0


def cmd_check(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    if state.get("status") == "done":
        payload = _run_payload(config, run_dir, state, [])
        if args.json:
            _print_json(payload)
        else:
            print("运行已完成，可运行 archive 归档")
        return 0
    if state.get("status") == "archived":
        payload = _run_payload(config, run_dir, state, [])
        if args.json:
            _print_json(payload)
        else:
            print("运行已归档")
        return 0
    stage = _find_stage(config, state.get("current_stage"))
    if stage is None:
        raise WorkflowError(f"当前阶段不在配置中: {state.get('current_stage')}")
    blockers = _stage_blockers(run_dir, state, config, stage)
    payload = _run_payload(config, run_dir, state, blockers)
    if args.json:
        _print_json(payload)
    elif blockers:
        print(f"当前阶段: {stage['title']}（{stage['id']}）")
        _print_blockers(blockers)
        print(f"下一步: {_next_action(payload)}")
    else:
        print(f"当前阶段: {stage['title']}（{stage['id']}）门禁通过，可运行 advance")
    return 0 if not blockers else 1


def cmd_advance(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    _ensure_active(state)
    stage = _find_stage(config, state.get("current_stage"))
    if stage is None:
        raise WorkflowError(f"当前阶段不在配置中: {state.get('current_stage')}")
    approve = args.approve.strip() if args.approve else None
    if approve == "":
        approve = None
    blockers = _stage_blockers(run_dir, state, config, stage, approve)
    if blockers:
        _print_blockers(blockers)
        return 1
    index = _stage_index(config, stage["id"])
    next_stage = config["stages"][index + 1] if index + 1 < len(config["stages"]) else None
    timestamp = _now()
    completed = {
        "id": stage["id"],
        "title": stage["title"],
        "completed_at": timestamp,
    }
    if stage.get("approval"):
        completed["approved_by"] = approve
    state.setdefault("completed_stages", []).append(completed)
    state.setdefault("history", []).append(
        {
            "at": timestamp,
            "event": "advance",
            "from": stage["id"],
            "to": next_stage["id"] if next_stage else None,
            "approved_by": completed.get("approved_by"),
        }
    )
    if next_stage:
        state["current_stage"] = next_stage["id"]
        message = f"已完成阶段: {stage['title']}（{stage['id']}）"
        next_message = f"当前阶段: {next_stage['title']}（{next_stage['id']}）"
    else:
        state["current_stage"] = None
        state["status"] = "done"
        state["completed_at"] = timestamp
        message = f"已完成阶段: {stage['title']}（{stage['id']}）"
        next_message = "运行已完成，下一步: archive"
    _write_state(run_dir, state)
    _render_tasks(run_dir, state, config)
    target = f"「{next_stage['title']}」" if next_stage else "完成"
    _append_build_log(
        run_dir,
        "阶段流转",
        f"- {timestamp} · 完成「{stage['title']}」→ {target}",
    )
    print(message)
    print(next_message)
    return 0


def cmd_rewind(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    if state.get("status") == "archived":
        raise WorkflowError("该运行已归档，不能回退")
    target = _find_stage(config, args.stage)
    if target is None:
        raise WorkflowError(f"未知阶段: {args.stage}")
    target_index = _stage_index(config, target["id"])
    if state.get("status") == "done":
        current_index = len(config["stages"])
    else:
        current = _find_stage(config, state.get("current_stage"))
        if current is None:
            raise WorkflowError(f"当前阶段不在配置中: {state.get('current_stage')}")
        current_index = _stage_index(config, current["id"])
    if target_index >= current_index:
        raise WorkflowError("只能回退到当前阶段之前的阶段")
    reason = args.reason.strip() if args.reason else ""
    if not reason:
        raise WorkflowError("回退阶段需要 --reason 说明理由")
    timestamp = _now()
    kept = []
    for completed in state.get("completed_stages", []):
        try:
            completed_index = _stage_index(config, completed.get("id"))
        except WorkflowError:
            continue
        if completed_index < target_index:
            kept.append(completed)
    state["completed_stages"] = kept
    state["status"] = "active"
    state["current_stage"] = target["id"]
    state["completed_at"] = None
    state.setdefault("history", []).append(
        {
            "at": timestamp,
            "event": "rewind",
            "to": target["id"],
            "reason": reason,
        }
    )
    _mark_verification_stale(state)
    red_record = state.get("red_verification")
    if isinstance(red_record, dict):
        red_record["stale"] = True
    _write_state(run_dir, state)
    _render_tasks(run_dir, state, config)
    _append_build_log(
        run_dir,
        "阶段流转",
        f"- {timestamp} · 回退到「{target['title']}」：{reason}",
    )
    print(f"已回退到阶段: {target['id']}（{target['title']}）")
    print(f"原因: {reason}")
    return 0


def _load_active_run(
    args: argparse.Namespace,
) -> tuple[dict[str, Any], Path, dict[str, Any]]:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    _ensure_active(state)
    return config, run_dir, state


def _task_sync(
    config: dict[str, Any],
    run_dir: Path,
    state: dict[str, Any],
    log_line: str | None = None,
) -> None:
    _write_state(run_dir, state)
    _render_tasks(run_dir, state, config)
    if log_line:
        _append_build_log(run_dir, "施工记录", log_line)


def cmd_task_add(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    title = (args.title or "").strip()
    acceptance = (args.acceptance or "").strip()
    verify = (args.verify or "").strip()
    if not title or not acceptance or not verify:
        raise WorkflowError("添加任务需要 --title、--acceptance、--verify")
    deps = _parse_csv(args.deps)
    files = _parse_csv(args.files)
    _validate_dependencies(state.get("tasks", []), deps)
    tasks = state.setdefault("tasks", [])
    next_number = 1
    for task in tasks:
        match = re.fullmatch(r"T(\d+)", task.get("id", ""))
        if match:
            next_number = max(next_number, int(match.group(1)) + 1)
    task_id = f"T{next_number}"
    timestamp = _now()
    task = {
        "id": task_id,
        "title": title,
        "description": (args.description or "").strip(),
        "acceptance": acceptance,
        "verify": verify,
        "files": files,
        "deps": deps,
        "status": "todo",
        "evidence": "",
        "status_reason": "",
        "created_at": timestamp,
        "started_at": None,
        "completed_at": None,
    }
    tasks.append(task)
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 新增任务 {task_id}「{title}」",
    )
    print(f"已添加任务 {task_id}: {title}")
    return 0


def cmd_task_update(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    if task.get("status") in ("done", "cancelled"):
        raise WorkflowError("只能更新未完成任务；如需继续请先 reopen")
    updates: dict[str, Any] = {}
    for field, value in (
        ("title", args.title),
        ("description", args.description),
        ("acceptance", args.acceptance),
        ("verify", args.verify),
    ):
        if value is not None:
            stripped = value.strip()
            if field in ("title", "acceptance", "verify") and not stripped:
                raise WorkflowError(f"{field} 不能为空")
            updates[field] = stripped
    if args.deps is not None:
        deps = _parse_csv(args.deps)
        _validate_dependencies(state.get("tasks", []), deps, task["id"])
        updates["deps"] = deps
    if args.files is not None:
        updates["files"] = _parse_csv(args.files)
    if not updates:
        raise WorkflowError("没有提供需要更新的字段")
    task.update(updates)
    timestamp = _now()
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 更新任务 {task['id']}（{', '.join(sorted(updates))}）",
    )
    print(f"已更新任务 {task['id']}")
    return 0


def cmd_task_start(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    if task.get("status") == "blocked":
        raise WorkflowError("任务处于阻塞状态，请先 unblock")
    if task.get("status") != "todo":
        raise WorkflowError(f"只能开始待办任务（当前: {task.get('status')}）")
    tasks = state.get("tasks", [])
    for dependency_id in task.get("deps", []):
        dependency = _find_task(tasks, dependency_id)
        if dependency.get("status") not in ("done", "cancelled"):
            raise WorkflowError(
                f"依赖未完成: {dependency_id}（当前: {dependency.get('status')}）"
            )
    timestamp = _now()
    task["status"] = "doing"
    task["started_at"] = timestamp
    task["status_reason"] = ""
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 开始任务 {task['id']}「{task['title']}」",
    )
    print(f"已开始任务 {task['id']}")
    return 0


def cmd_task_done(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    if task.get("status") != "doing":
        raise WorkflowError(f"只能完成进行中的任务（当前: {task.get('status')}）")
    evidence = (args.evidence or "").strip()
    if not evidence:
        raise WorkflowError("完成任务需要 --evidence 提供验证证据")
    timestamp = _now()
    task["status"] = "done"
    task["evidence"] = evidence
    task["completed_at"] = timestamp
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 完成任务 {task['id']} · 证据: {_clip(evidence)}",
    )
    print(f"已完成任务 {task['id']}")
    return 0


def cmd_task_block(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    reason = (args.reason or "").strip()
    if not reason:
        raise WorkflowError("阻塞任务需要 --reason 说明理由")
    if task.get("status") not in ("todo", "doing"):
        raise WorkflowError(f"只能阻塞待办或进行中的任务（当前: {task.get('status')}）")
    timestamp = _now()
    task["status"] = "blocked"
    task["status_reason"] = reason
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 阻塞任务 {task['id']}：{reason}",
    )
    print(f"已阻塞任务 {task['id']}")
    return 0


def cmd_task_unblock(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    if task.get("status") != "blocked":
        raise WorkflowError(f"只能解除阻塞状态的任务（当前: {task.get('status')}）")
    timestamp = _now()
    task["status"] = "todo"
    task["status_reason"] = ""
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 解除阻塞 {task['id']}",
    )
    print(f"已解除阻塞 {task['id']}")
    return 0


def cmd_task_reopen(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    reason = (args.reason or "").strip()
    if not reason:
        raise WorkflowError("重新打开任务需要 --reason 说明理由")
    if task.get("status") not in ("done", "cancelled"):
        raise WorkflowError(f"只能重新打开已完成或已取消的任务（当前: {task.get('status')}）")
    timestamp = _now()
    task["status"] = "todo"
    task["evidence"] = ""
    task["status_reason"] = ""
    task["started_at"] = None
    task["completed_at"] = None
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 重新打开任务 {task['id']}：{reason}",
    )
    print(f"已重新打开任务 {task['id']}")
    return 0


def cmd_task_drop(args: argparse.Namespace) -> int:
    config, run_dir, state = _load_active_run(args)
    task = _find_task(state.get("tasks", []), args.task_id)
    reason = (args.reason or "").strip()
    if not reason:
        raise WorkflowError("移除任务需要 --reason 说明理由")
    if task.get("status") not in ("todo", "doing", "blocked"):
        raise WorkflowError(f"只能移除未完成任务（当前: {task.get('status')}）")
    timestamp = _now()
    task["status"] = "cancelled"
    task["status_reason"] = reason
    task["completed_at"] = timestamp
    _mark_verification_stale(state)
    _task_sync(
        config,
        run_dir,
        state,
        f"- {timestamp} · 取消任务 {task['id']}：{reason}",
    )
    print(f"已取消任务 {task['id']}")
    return 0


def cmd_task_list(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    tasks = state.get("tasks", [])
    if args.json:
        _print_json(
            {
                "slug": state.get("slug"),
                "title": state.get("title"),
                "status": state.get("status"),
                "tasks": tasks,
            }
        )
        return 0
    print(f"任务: {state.get('slug')} · {state.get('title')}")
    if not tasks:
        print("暂无任务")
        return 0
    print("ID  状态    依赖          标题")
    for task in tasks:
        print(
            f"{task['id']:<4} {STATUS_LABELS.get(task['status'], task['status']):<6} "
            f"{','.join(task.get('deps', [])) or '-':<12} {task['title']}"
        )
    return 0


def cmd_render(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    _render_tasks(run_dir, state, config)
    print(f"已生成: {run_dir / 'tasks.md'}")
    return 0


def cmd_verify(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    _ensure_active(state)
    red = bool(args.red)
    commands = config.get("red_commands" if red else "commands", [])
    if not commands:
        if red:
            raise WorkflowError(
                "未配置先行测试命令：请在 .workflow/config.json 的 red_commands 中添加命令"
            )
        raise WorkflowError(
            "未配置验证命令：请在 .workflow/config.json 的 commands 中添加至少一个命令"
        )
    default_timeout = config.get("timeout_seconds", DEFAULT_TIMEOUT)
    results = _run_commands(commands, root, default_timeout)
    if red:
        overall = any(
            result["exit_code"] not in (0, None) and not result["timed_out"]
            for result in results
        )
        for result in results:
            result["ok"] = (
                result["exit_code"] not in (0, None) and not result["timed_out"]
            )
    else:
        overall = bool(results) and len(results) == len(commands) and all(
            result["ok"] for result in results
        )
    timestamp = _now()
    evidence_relative = Path("evidence") / ("red.json" if red else "verify.json")
    evidence = {
        "schema_version": SCHEMA_VERSION,
        "kind": "red" if red else "verify",
        "at": timestamp,
        "ok": overall,
        "command_fingerprint": _command_fingerprint(commands),
        "commands": results,
    }
    _atomic_write(run_dir / evidence_relative, json.dumps(evidence, ensure_ascii=False, indent=2) + "\n")
    record = {
        "at": timestamp,
        "ok": overall,
        "stale": False,
        "evidence": evidence_relative.as_posix(),
        "command_fingerprint": _command_fingerprint(commands),
        "summary": {
            "total": len(commands),
            "passed": sum(1 for result in results if result["ok"]),
        },
    }
    state["red_verification" if red else "verification"] = record
    _write_state(run_dir, state)
    _append_build_log(
        run_dir,
        "验证结果",
        f"- {timestamp} · {'先行测试' if red else '验证'} "
        f"{'PASS' if overall else 'FAIL'} · {record['summary']['passed']}/{len(commands)} · "
        f"证据 {evidence_relative.as_posix()}",
    )

    if red:
        print(f"先行测试验证: {'PASS' if overall else 'FAIL'}")
        if overall:
            print("已观察到预期失败。")
        else:
            all_passed = all(result["exit_code"] == 0 for result in results)
            if all_passed:
                print("所有命令均通过，未出现预期失败。")
            else:
                print("未观察到有效的预期失败，请检查命令或输出。")
    else:
        print(f"验证结果: {'PASS' if overall else 'FAIL'}")
    for result in results:
        print(
            f"- {result['name']}: exit={result['exit_code']} "
            f"({result['duration_ms']} ms){' [timeout]' if result['timed_out'] else ''}"
        )
    print(f"证据: {evidence_relative.as_posix()}")
    if not overall and not red:
        for result in results:
            if not result["ok"]:
                print(f"\n[{result['name']}] 输出尾部：\n{result['output_tail']}")
    return 0 if overall else 1


def cmd_archive(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    run_dir, state = _resolve_run(root, args.run)
    if state.get("status") == "archived":
        raise WorkflowError("该运行已归档")
    if state.get("status") != "done":
        raise WorkflowError("只有已完成的运行可以归档；请先完成 writeback 阶段")
    integrity = _all_artifact_blockers(config, run_dir)
    integrity.extend(_verification_blockers(config, state, run_dir, "verify"))
    integrity = _dedupe_blockers(integrity)
    if integrity:
        print(f"归档前完整性检查失败（{len(integrity)} 项）：", file=sys.stderr)
        for blocker in integrity:
            print(f"- [{blocker['code']}] {blocker['message']}", file=sys.stderr)
        return 1
    archive_root = _archive_dir(root)
    archive_root.mkdir(parents=True, exist_ok=True)
    timestamp = _now()
    destination = archive_root / f"{_stamp()}-{state['slug']}"
    suffix = 1
    while destination.exists():
        suffix += 1
        destination = archive_root / f"{_stamp()}-{state['slug']}-{suffix}"
    tasks = _task_stats(state.get("tasks", []))
    verification = _verification_summary(config, state, run_dir, "verify")
    build_log_path = run_dir / "build-log.md"
    summary = ""
    if build_log_path.is_file():
        sections = _section_map(build_log_path.read_text(encoding="utf-8"))
        summary = _strip_comments(sections.get("交付摘要", "")).strip()
    if not summary:
        summary = "详见 build-log.md"
    artifacts = []
    for path in sorted(run_dir.rglob("*")):
        if path.is_file():
            relative = path.relative_to(run_dir)
            if relative.as_posix() in ("ARCHIVE.md",):
                continue
            artifacts.append(relative.as_posix())
    archive_document = "\n".join(
        [
            f"# 归档：{state.get('title')}",
            "",
            f"- 运行: {state.get('slug')}",
            f"- 创建时间: {state.get('created_at')}",
            f"- 完成时间: {state.get('completed_at')}",
            f"- 归档时间: {timestamp}",
            f"- 阶段: {len(state.get('completed_stages', []))}/{len(config['stages'])}",
            f"- 任务: 完成 {tasks['done']}/{tasks['total']}，取消 {tasks['cancelled']}",
            f"- 验证: {'PASS' if verification['ok'] else '未通过/未记录'}"
            f"（{verification.get('at') or '-'}）",
            "",
            "## 交付摘要",
            "",
            summary,
            "",
            "## 产物",
            "",
            *[f"- {item}" for item in artifacts],
            "",
        ]
    )
    _atomic_write(run_dir / "ARCHIVE.md", archive_document)
    shutil.move(str(run_dir), str(destination))
    archived_state = _read_state(destination)
    archived_state["status"] = "archived"
    archived_state["archived_at"] = timestamp
    archived_state.setdefault("history", []).append(
        {"at": timestamp, "event": "archive", "path": destination.name}
    )
    _write_state(destination, archived_state)
    index_path = archive_root / "INDEX.md"
    if index_path.is_file():
        index_text = index_path.read_text(encoding="utf-8")
    else:
        index_text = (
            "# 归档索引\n\n"
            "| 归档时间 | 运行 | 标题 | 任务 | 验证 | 路径 |\n"
            "|---|---|---|---|---|---|\n"
        )
    row = (
        f"| {timestamp} | {state.get('slug')} | "
        f"{_cell(state.get('title'))} | {tasks['done']}/{tasks['total']} | "
        f"{'PASS' if verification['ok'] else '-'} | "
        f"[{destination.name}]({destination.name}/) |\n"
    )
    _atomic_write(index_path, index_text.rstrip() + "\n" + row)
    print(f"已归档: {destination}")
    print(f"索引: {index_path}")
    return 0


def cmd_list(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    runs: list[dict[str, Any]] = []
    for base, archived in ((_active_dir(root), False), (_archive_dir(root), True)):
        for run_dir, state in _iter_run_states(base):
            stage = _find_stage(config, state.get("current_stage"))
            tasks = _task_stats(state.get("tasks", []))
            runs.append(
                {
                    "slug": state.get("slug"),
                    "title": state.get("title"),
                    "status": state.get("status"),
                    "stage": stage["id"] if stage else None,
                    "stage_title": stage["title"] if stage else None,
                    "progress": f"{len(state.get('completed_stages', []))}/{len(config['stages'])}",
                    "tasks": tasks,
                    "updated_at": state.get("updated_at"),
                    "archived_at": state.get("archived_at"),
                    "archived": archived,
                    "path": str(run_dir),
                }
            )
    runs.sort(key=lambda item: item.get("updated_at") or "", reverse=True)
    if args.json:
        _print_json({"runs": runs})
        return 0
    if not runs:
        print("暂无运行")
        return 0
    for run in runs:
        print(
            f"{run['slug']:<16} {run['status']:<9} {run['progress']:<5} "
            f"任务 {run['tasks']['done']}/{run['tasks']['total']}  {run['title']}"
        )
    return 0


def cmd_config_show(args: argparse.Namespace) -> int:
    root = _resolve_root(args.root)
    config = _load_config(root)
    path = root / WORKFLOW_DIR / "config.json"
    if args.json:
        _print_json(config)
        return 0
    print(f"配置: {path}")
    print(f"阶段: {len(config['stages'])}")
    print(f"验证命令: {len(config.get('commands', []))}")
    print(f"先行测试命令: {len(config.get('red_commands', []))}")
    for index, stage in enumerate(config["stages"], start=1):
        print(f"- {index}. {stage['title']}（{stage['id']}）")
    return 0


# ---------------------------------------------------------------------------
# Parser
# ---------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="workflow.py",
        description="配置驱动的分阶段交付工作流：方案、任务、评审、测试先行、施工、验证、归档。",
        allow_abbrev=False,
    )
    parser.add_argument("--root", help="工作区根目录，默认从当前目录向上查找 .workflow")
    parser.add_argument("--run", help="运行标识（多个进行中运行时必填）")
    subparsers = parser.add_subparsers(dest="command", required=True)

    init = subparsers.add_parser("init", help="初始化一次运行并生成产物模板")
    init.add_argument("slug", help="运行标识（小写字母、数字、连字符）")
    init.add_argument("--title", required=True, help="本次交付标题")
    init.set_defaults(func=cmd_init)

    status = subparsers.add_parser("status", help="查看当前阶段、门禁与任务状态")
    status.add_argument("--json", action="store_true", help="输出 JSON")
    status.set_defaults(func=cmd_status)

    check = subparsers.add_parser("check", help="只检查门禁，不推进阶段")
    check.add_argument("--json", action="store_true", help="输出 JSON")
    check.set_defaults(func=cmd_check)

    advance = subparsers.add_parser("advance", help="通过当前阶段门禁并进入下一阶段")
    advance.add_argument("--approve", help="需要人工审批的阶段填写审批人")
    advance.set_defaults(func=cmd_advance)

    rewind = subparsers.add_parser("rewind", help="回退到较早阶段并记录原因")
    rewind.add_argument("stage", help="目标阶段 id")
    rewind.add_argument("--reason", help="回退原因")
    rewind.set_defaults(func=cmd_rewind)

    task = subparsers.add_parser("task", help="管理任务清单")
    task_subparsers = task.add_subparsers(dest="task_command", required=True)

    task_add = task_subparsers.add_parser("add", help="添加任务")
    task_add.add_argument("--title")
    task_add.add_argument("--description")
    task_add.add_argument("--acceptance")
    task_add.add_argument("--verify")
    task_add.add_argument("--deps", help="逗号分隔的依赖任务 ID")
    task_add.add_argument("--files", help="逗号分隔的预期涉及文件")
    task_add.set_defaults(func=cmd_task_add)

    task_update = task_subparsers.add_parser("update", help="更新未完成任务")
    task_update.add_argument("task_id")
    task_update.add_argument("--title")
    task_update.add_argument("--description")
    task_update.add_argument("--acceptance")
    task_update.add_argument("--verify")
    task_update.add_argument("--deps")
    task_update.add_argument("--files")
    task_update.set_defaults(func=cmd_task_update)

    task_start = task_subparsers.add_parser("start", help="开始任务")
    task_start.add_argument("task_id")
    task_start.set_defaults(func=cmd_task_start)

    task_done = task_subparsers.add_parser("done", help="完成任务并记录证据")
    task_done.add_argument("task_id")
    task_done.add_argument("--evidence")
    task_done.set_defaults(func=cmd_task_done)

    task_block = task_subparsers.add_parser("block", help="阻塞任务")
    task_block.add_argument("task_id")
    task_block.add_argument("--reason")
    task_block.set_defaults(func=cmd_task_block)

    task_unblock = task_subparsers.add_parser("unblock", help="解除任务阻塞")
    task_unblock.add_argument("task_id")
    task_unblock.set_defaults(func=cmd_task_unblock)

    task_reopen = task_subparsers.add_parser("reopen", help="重新打开已完成/取消的任务")
    task_reopen.add_argument("task_id")
    task_reopen.add_argument("--reason")
    task_reopen.set_defaults(func=cmd_task_reopen)

    task_drop = task_subparsers.add_parser("drop", help="取消任务并记录理由")
    task_drop.add_argument("task_id")
    task_drop.add_argument("--reason")
    task_drop.set_defaults(func=cmd_task_drop)

    task_list = task_subparsers.add_parser("list", help="列出任务")
    task_list.add_argument("--json", action="store_true", help="输出 JSON")
    task_list.set_defaults(func=cmd_task_list)

    render = subparsers.add_parser("render", help="重新生成 tasks.md")
    render.set_defaults(func=cmd_render)

    verify = subparsers.add_parser("verify", help="运行验证命令或先行测试命令")
    verify.add_argument("--red", action="store_true", help="运行 red_commands 并要求失败")
    verify.set_defaults(func=cmd_verify)

    archive = subparsers.add_parser("archive", help="归档已完成的运行")
    archive.set_defaults(func=cmd_archive)

    list_runs = subparsers.add_parser("list", help="列出进行中和已归档的运行")
    list_runs.add_argument("--json", action="store_true", help="输出 JSON")
    list_runs.set_defaults(func=cmd_list)

    config_parser = subparsers.add_parser("config", help="查看工作流配置")
    config_subparsers = config_parser.add_subparsers(dest="config_command", required=True)
    config_show = config_subparsers.add_parser("show", help="显示配置摘要")
    config_show.add_argument("--json", action="store_true", help="输出完整 JSON")
    config_show.set_defaults(func=cmd_config_show)

    return parser


def _normalize_global_options(argv: list[str]) -> list[str]:
    globals_: list[str] = []
    rest: list[str] = []
    index = 0
    while index < len(argv):
        item = argv[index]
        if item in ("--root", "--run") and index + 1 < len(argv):
            globals_.extend([item, argv[index + 1]])
            index += 2
            continue
        if item.startswith("--root=") or item.startswith("--run="):
            globals_.append(item)
            index += 1
            continue
        rest.append(item)
        index += 1
    return globals_ + rest


def main(argv: list[str] | None = None) -> int:
    _configure_stdio()
    parser = build_parser()
    raw_args = list(sys.argv[1:] if argv is None else argv)
    args = parser.parse_args(_normalize_global_options(raw_args))
    try:
        return int(args.func(args))
    except WorkflowError as exc:
        print(f"错误: {exc}", file=sys.stderr)
        return 1
    except Exception as exc:  # pragma: no cover - defensive CLI boundary
        if os.environ.get("STAGED_DELIVERY_DEBUG") == "1":
            raise
        print(f"内部错误: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
