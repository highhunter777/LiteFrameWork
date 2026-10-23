# -*- coding: utf-8 -*-
"""Manifest v2 loading and structural validation for the toolchain platform."""

import json
import os
import re
from pathlib import Path

from _common import repo_root

KINDS = {"page", "window", "action", "hud", "cli", "gate", "build", "content", "docs"}
HANDLER_KINDS = {"page", "window", "action"}
COMMAND_KINDS = {"cli", "gate", "build", "content"}
PLANES = {"editor", "runtime", "external", "none"}
RUNTIME_REQUIRES = {"play", "player"}
REQUIRES = {"play", "player", "unity-batch"}
PURPOSES = {"debug", "dev", "delivery"}
WRITE_SCOPES = {"none", "source", "generated", "asset", "distribution"}
OUTPUT_KINDS = {"generated", "source", "data", "asset", "binary", "report", "distribution"}
INTERACTIVE_KINDS = {"page", "window"}
SURFACE_KINDS = {"ci-workflow", "hook-config", "scheduled-task", "path"}
REQUIRED_TOOL_FIELDS = ("id", "title", "category", "kind", "plane", "purpose", "summary", "docs")
COMMAND_PATH_PREFIXES = ("scripts/", "Server/", "Assets/", "Tests/")
ARG_TYPES = {"path", "file", "bool", "int", "string", "enum"}
ARG_NAME_PATTERN = re.compile(r"^[a-z][a-z0-9-]*$")


class ManifestError(Exception):
    """Raised when the manifest cannot be loaded or is structurally unusable."""


def default_manifest(root=None):
    return (Path(root) if root else repo_root()) / "scripts" / "gate" / "toolchain.json"


def load_manifest(path):
    path = Path(path)
    if not path.is_file():
        raise ManifestError(f"manifest not found: {path}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        raise ManifestError(f"manifest parse failed: {path}: {exc}")
    if not isinstance(data, dict):
        raise ManifestError(f"manifest root must be an object: {path}")
    return data


def _is_repo_relative(value):
    return isinstance(value, str) and value and not os.path.isabs(value)


def _check_path(root, relative, field, errors, owner):
    if not _is_repo_relative(relative):
        errors.append(f"{owner}: {field} must be a repo-relative path")
        return
    if not (Path(root) / relative).exists():
        errors.append(f"{owner}: {field} path missing: {relative}")


def _iter_command_paths(command):
    for token in command:
        if isinstance(token, str) and token.startswith(COMMAND_PATH_PREFIXES):
            yield token


def _default_matches_type(value, arg_type):
    if arg_type == "bool":
        return isinstance(value, bool)
    if arg_type == "int":
        return isinstance(value, int) and not isinstance(value, bool)
    return isinstance(value, str)


def _validate_args(owner, specs, kind, errors):
    if not isinstance(specs, list):
        errors.append(f"{owner}: args must be an array")
        return
    if kind not in COMMAND_KINDS:
        errors.append(f"{owner}: args only valid for command tools")
    names = set()
    for index, spec in enumerate(specs):
        arg_owner = f"{owner}.args[{index}]"
        if not isinstance(spec, dict):
            errors.append(f"{arg_owner} must be an object")
            continue
        name = spec.get("name")
        if not isinstance(name, str) or not ARG_NAME_PATTERN.match(name):
            errors.append(f"{arg_owner}: name must be kebab-case")
        elif name in names:
            errors.append(f"{arg_owner}: duplicate name: {name}")
        else:
            names.add(name)
        arg_type = spec.get("type")
        if arg_type not in ARG_TYPES:
            errors.append(f"{arg_owner}: unsupported type: {arg_type}")
        if "required" in spec and not isinstance(spec["required"], bool):
            errors.append(f"{arg_owner}: required must be a boolean")
        if "description" in spec and not isinstance(spec["description"], str):
            errors.append(f"{arg_owner}: description must be a string")
        values = spec.get("values")
        if arg_type == "enum":
            valid_values = (
                isinstance(values, list)
                and values
                and all(isinstance(value, str) and value for value in values)
            )
            if not valid_values:
                errors.append(f"{arg_owner}: enum requires a non-empty values array")
        elif values is not None:
            errors.append(f"{arg_owner}: values only valid for enum")
        if "default" in spec:
            if spec.get("required") is True:
                errors.append(f"{arg_owner}: required argument cannot declare a default")
            if arg_type in ARG_TYPES and not _default_matches_type(spec["default"], arg_type):
                errors.append(f"{arg_owner}: default does not match type {arg_type}")
            if arg_type == "enum" and isinstance(values, list) and spec["default"] not in values:
                errors.append(f"{arg_owner}: default must be one of the enum values")


def _validate_plane(owner, tool, kind, errors):
    """Plane/requires contract: which side must be alive for the tool to take effect."""
    plane = tool.get("plane")
    requires = tool.get("requires", [])
    if plane not in PLANES:
        errors.append(f"{owner}: unsupported plane: {plane}")
        return
    if not isinstance(requires, list) or any(item not in REQUIRES for item in requires):
        errors.append(f"{owner}: requires must be a subset of {sorted(REQUIRES)}")
        return
    if plane == "none" and kind != "docs":
        errors.append(f"{owner}: plane none is only valid for kind docs")
    if kind == "docs" and plane != "none":
        errors.append(f"{owner}: kind docs requires plane none")
    if plane == "editor" and requires:
        errors.append(f"{owner}: plane editor cannot declare requires")
    if plane == "runtime" and (not requires or any(item not in RUNTIME_REQUIRES for item in requires)):
        errors.append(f"{owner}: plane runtime requires a non-empty subset of {sorted(RUNTIME_REQUIRES)}")
    if plane == "external" and not tool.get("command") and kind != "page":
        errors.append(f"{owner}: plane external needs a command (or a hub page driving an external environment)")
    if kind == "hud" and (plane != "runtime" or not set(requires) & RUNTIME_REQUIRES):
        errors.append(f"{owner}: kind hud requires plane runtime with play/player")


def validate_manifest(manifest, root):
    """Return (errors, warnings) for structure, references and command paths."""
    errors = []
    warnings = []
    root = Path(root)

    if manifest.get("schemaVersion") != 6:
        errors.append("schemaVersion must be 6")

    categories = manifest.get("categories")
    tools = manifest.get("tools")
    surfaces = manifest.get("surfaces")
    if not isinstance(categories, list):
        errors.append("categories must be an array")
        categories = []
    if not isinstance(tools, list):
        errors.append("tools must be an array")
        tools = []
    if not isinstance(surfaces, list):
        errors.append("surfaces must be an array")
        surfaces = []

    category_ids = set()
    for index, category in enumerate(categories):
        owner = f"categories[{index}]"
        if not isinstance(category, dict):
            errors.append(f"{owner} must be an object")
            continue
        cid = category.get("id")
        title = category.get("title")
        if not cid or not isinstance(cid, str):
            errors.append(f"{owner}: id missing")
        elif cid in category_ids:
            errors.append(f"{owner}: duplicate category id: {cid}")
        else:
            category_ids.add(cid)
        if not title or not isinstance(title, str):
            errors.append(f"{owner}: title missing")
        if "order" in category and not isinstance(category["order"], (int, float)):
            errors.append(f"{owner}: order must be a number")
        if "docs" in category:
            _check_path(root, category["docs"], "docs", errors, owner)
        allow_docs = category.get("allowDocs", [])
        if not isinstance(allow_docs, list):
            errors.append(f"{owner}: allowDocs must be an array")
        else:
            for doc in allow_docs:
                _check_path(root, doc, "allowDocs", errors, owner)

    tool_ids = set()
    handlers = set()
    menus = set()
    for index, tool in enumerate(tools):
        owner = f"tools[{index}]"
        if not isinstance(tool, dict):
            errors.append(f"{owner} must be an object")
            continue
        for field in REQUIRED_TOOL_FIELDS:
            if not tool.get(field):
                errors.append(f"{owner}: {field} missing")
        tool_id = tool.get("id")
        if tool_id:
            owner = f"tool {tool_id}"
            if tool_id in tool_ids:
                errors.append(f"{owner}: duplicate id")
            tool_ids.add(tool_id)
        category = tool.get("category")
        if category and category not in category_ids:
            errors.append(f"{owner}: undeclared category: {category}")
        kind = tool.get("kind")
        if kind not in KINDS:
            errors.append(f"{owner}: unsupported kind: {kind}")
        if kind in HANDLER_KINDS:
            handler = tool.get("handler")
            if not handler:
                errors.append(f"{owner}: handler required for kind {kind}")
            elif handler in handlers:
                errors.append(f"{owner}: duplicate handler: {handler}")
            else:
                handlers.add(handler)
        if kind in COMMAND_KINDS:
            command = tool.get("command")
            if not isinstance(command, list) or not command or not all(isinstance(x, str) for x in command):
                errors.append(f"{owner}: command must be a non-empty string array")
            else:
                for relative in _iter_command_paths(command):
                    if not (root / relative).exists():
                        errors.append(f"{owner}: command path missing: {relative}")
        _validate_plane(owner, tool, kind, errors)
        if tool.get("purpose") not in PURPOSES:
            errors.append(f"{owner}: unsupported purpose: {tool.get('purpose')}")
        docs = tool.get("docs")
        if docs:
            _check_path(root, docs, "docs", errors, owner)
        menu = tool.get("menu")
        if menu:
            if menu in menus:
                errors.append(f"{owner}: duplicate menu: {menu}")
            menus.add(menu)
        writes = tool.get("writes", [])
        if not isinstance(writes, list) or any(scope not in WRITE_SCOPES for scope in writes):
            errors.append(f"{owner}: writes must be a subset of {sorted(WRITE_SCOPES)}")
        if "destructive" in tool and not isinstance(tool["destructive"], bool):
            errors.append(f"{owner}: destructive must be a boolean")
        if "order" in tool and not isinstance(tool["order"], (int, float)):
            errors.append(f"{owner}: order must be a number")
        outputs = tool.get("outputs", [])
        if not isinstance(outputs, list):
            errors.append(f"{owner}: outputs must be an array")
        else:
            for output in outputs:
                if not isinstance(output, dict) or not _is_repo_relative(output.get("path")):
                    errors.append(f"{owner}: output path must be repo-relative")
                    continue
                if output.get("kind") not in OUTPUT_KINDS:
                    errors.append(f"{owner}: output kind must be one of {sorted(OUTPUT_KINDS)}")
            writes_project = any(scope != "none" for scope in (writes if isinstance(writes, list) else []))
            if writes_project and kind not in INTERACTIVE_KINDS and not outputs:
                errors.append(f"{owner}: non-interactive writer must declare outputs (artifact forms)")
        safety = tool.get("safety")
        if safety is not None:
            if not isinstance(safety, dict):
                errors.append(f"{owner}: safety must be an object")
            else:
                if safety.get("dryRun", "unsupported") not in {"supported", "unsupported"}:
                    errors.append(f"{owner}: safety.dryRun must be supported/unsupported")
                if safety.get("undo", "none") not in {"file", "asset", "none"}:
                    errors.append(f"{owner}: safety.undo must be file/asset/none")
                if safety.get("confirmation", "none") not in {"none", "required"}:
                    errors.append(f"{owner}: safety.confirmation must be none/required")
                if safety.get("dryRun") == "supported" and not outputs:
                    errors.append(f"{owner}: dry-run supported requires outputs")
                if "dryRunArg" in safety and (
                    not isinstance(safety["dryRunArg"], str) or not safety["dryRunArg"].startswith("-")
                ):
                    errors.append(f"{owner}: safety.dryRunArg must be a command-line flag")
        args_schema = tool.get("args")
        if args_schema is not None:
            _validate_args(owner, args_schema, kind, errors)

    surface_ids = set()
    for index, surface in enumerate(surfaces):
        owner = f"surfaces[{index}]"
        if not isinstance(surface, dict):
            errors.append(f"{owner} must be an object")
            continue
        sid = surface.get("id")
        if not sid:
            errors.append(f"{owner}: id missing")
        elif sid in surface_ids:
            errors.append(f"{owner}: duplicate id: {sid}")
        else:
            surface_ids.add(sid)
        kind = surface.get("kind")
        if kind not in SURFACE_KINDS:
            errors.append(f"{owner}: unsupported kind: {kind}")
        if "required" in surface and not isinstance(surface["required"], bool):
            errors.append(f"{owner}: required must be a boolean")
        if kind in {"ci-workflow", "hook-config", "path"}:
            _check_path(root, surface.get("path"), "path", errors, owner)
        if kind == "scheduled-task":
            if not surface.get("name"):
                errors.append(f"{owner}: scheduled-task requires name")
            if not surface.get("script"):
                errors.append(f"{owner}: scheduled-task requires script")
            else:
                _check_path(root, surface["script"], "script", errors, owner)
            if surface.get("runner") not in {"pwsh", "powershell", "python"}:
                errors.append(f"{owner}: scheduled-task runner must be pwsh, powershell or python")

    return errors, warnings


def find_tool(manifest, tool_id):
    for tool in manifest.get("tools", []):
        if tool.get("id") == tool_id:
            return tool
    return None


def categories_by_id(manifest):
    return {category.get("id"): category for category in manifest.get("categories", [])}


def sorted_tools(manifest, category_id=None):
    tools = [tool for tool in manifest.get("tools", []) if category_id is None or tool.get("category") == category_id]
    return sorted(tools, key=lambda tool: (tool.get("order", 1000), tool.get("title", "")))
