# -*- coding: utf-8 -*-
"""Domain-level form checks for the toolchain platform.

One domain = one manifest category. The rules are defined by the domain
baseline design doc (Docs/设计文档/架构/工具链域形态基线专项设计.md); this module
only implements the mechanical half:

  R1  same meaning -> same argument name inside a domain (synonym groups);
  R2  every tool points at the domain authority doc or an allow-listed doc;
  R3  command entries expose one output-prefix root per domain.

Findings are warnings; `toolchain.py check --strict` promotes them to errors.
"""

import re
from pathlib import Path

COMMAND_KINDS = {"cli", "gate", "build", "content"}
PREFIX_SHAPE = re.compile(r"^[a-z][a-z0-9-]*/[a-z0-9-]+$")
TOOL_CONST = re.compile(r"(?m)^TOOL\s*=\s*['\"]([^'\"]+)['\"]")
INFO_LITERAL = re.compile(r"(?:info|warn|fail)\(\s*['\"]([^'\"]+)['\"]")
PRINT_LITERAL = re.compile(r"print\(\s*f?['\"]\[([^\]\"']+)")

# canonical name -> (aliases, meaning); extend only with the baseline doc.
SYNONYMS = (
    ("dry-run", ("what-if",), "preview-only"),
)


def _tools_of(manifest, category_id):
    return [tool for tool in manifest.get("tools", []) if tool.get("category") == category_id]


def _check_arg_names(manifest):
    issues = []
    for category in manifest.get("categories", []):
        category_id = category.get("id")
        tools = _tools_of(manifest, category_id)
        for canonical, aliases, meaning in SYNONYMS:
            names = {canonical, *aliases}
            used = {}
            for tool in tools:
                for spec in tool.get("args") or []:
                    name = spec.get("name") if isinstance(spec, dict) else None
                    if name in names:
                        used.setdefault(name, []).append(tool.get("id"))
            if len(used) > 1:
                detail = ", ".join(
                    f"--{name} ({', '.join(sorted(str(item) for item in ids))})"
                    for name, ids in sorted(used.items())
                )
                issues.append((
                    "warning",
                    f"domain {category_id}: {meaning} flags are not uniform: {detail}; "
                    f"canonical name is --{canonical}",
                ))
    return issues


def _check_doc_authority(manifest):
    issues = []
    for category in manifest.get("categories", []):
        category_id = category.get("id")
        authority = category.get("docs")
        if not authority:
            issues.append((
                "warning",
                f"domain {category_id}: categories[].docs (domain authority doc) is not declared",
            ))
            continue
        allowed = {authority}
        allowed.update(category.get("allowDocs") or [])
        for tool in _tools_of(manifest, category_id):
            docs = tool.get("docs")
            if docs and docs not in allowed:
                issues.append((
                    "warning",
                    f"domain {category_id}: {tool.get('id')} docs '{docs}' is neither the "
                    f"authority '{authority}' nor allow-listed",
                ))
    return issues


def _script_token(command):
    for token in command or []:
        if isinstance(token, str) and token.startswith("scripts/") and token.endswith(".py"):
            return token
    return None


def _toolbox_map(path):
    """Read the toolbox's tool -> script map (its own single source)."""
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    return {
        name: script
        for name, script in re.findall(r"'([^']+)':\s*\(\s*'([^']+\.py)'", text)
    }


def _resolve_entry(root, command):
    """Return the script that owns the command, or None for non-script entries."""
    relative = _script_token(command)
    if not relative:
        return None
    path = Path(root) / relative
    if path.name == "toolbox.py":
        try:
            index = list(command).index(relative)
        except ValueError:
            return None
        for token in list(command)[index + 1:]:
            if isinstance(token, str) and not token.startswith("-"):
                script = _toolbox_map(path).get(token)
                return path.parent / script if script else None
        return None
    return path


def _extract_prefix(root, command):
    """Best-effort read of the `[domain/tool]` label an entry prints."""
    entry = _resolve_entry(root, command)
    if entry is None or not entry.is_file():
        return None
    text = entry.read_text(encoding="utf-8-sig", errors="replace")

    match = TOOL_CONST.search(text)
    if match:
        tool = match.group(1)
        if "/" in tool:
            return tool
        if "from _codegen import" in text:
            return f"codegen/{tool}"

    for pattern in (INFO_LITERAL, PRINT_LITERAL):
        for match in pattern.finditer(text):
            value = match.group(1).strip()
            if PREFIX_SHAPE.match(value):
                return value

    if "from _codegen import" in text:
        for match in INFO_LITERAL.finditer(text):
            value = match.group(1).strip()
            if re.fullmatch(r"[a-z0-9-]+", value):
                return f"codegen/{value}"
    return None


def _check_prefixes(manifest, root):
    issues = []
    for category in manifest.get("categories", []):
        category_id = category.get("id")
        roots = {}
        missing = []
        for tool in _tools_of(manifest, category_id):
            if tool.get("kind") not in COMMAND_KINDS:
                continue
            command = tool.get("command")
            if not _script_token(command):
                continue  # dotnet / external entries have no script prefix
            prefix = _extract_prefix(root, command)
            if prefix is None:
                missing.append(tool.get("id"))
                continue
            roots.setdefault(prefix.split("/")[0], set()).add(prefix)
        if len(roots) > 1:
            detail = "; ".join(
                f"{name}: {', '.join(sorted(prefixes))}"
                for name, prefixes in sorted(roots.items())
            )
            issues.append((
                "warning",
                f"domain {category_id}: command entries use {len(roots)} output-prefix roots ({detail}); "
                f"one domain must speak with one label",
            ))
        for tool_id in missing:
            issues.append((
                "warning",
                f"domain {category_id}: {tool_id} exposes no stable output prefix",
            ))
    return issues


def check_domains(manifest, root):
    """Return [(level, message)] for the three domain-form rules."""
    root = Path(root)
    return [
        *_check_arg_names(manifest),
        *_check_doc_authority(manifest),
        *_check_prefixes(manifest, root),
    ]
