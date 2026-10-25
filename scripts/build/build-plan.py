# -*- coding: utf-8 -*-
"""Build preflight: map a git diff to the minimum rebuild set.

Read-only: never writes to the working tree. Two plans are produced:
inner loop (fast verification) and delivery (cloud/testers).
"""

import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root as _repo_root_impl  # noqa: E402

SCHEMA_VERSION = 1

ACTIONS_ORDER = [
    "editor_reload",
    "regen_proto",
    "regen_luban",
    "regen_buildhash",
    "run_authoritative_tests",
    "rebuild_content_bundle",
    "rebuild_client_player",
    "rebuild_server_room",
    "publish_server",
    "publish_meta",
    "publish_room_config",
    "restart_room",
    "none",
]

IGNORED_PREFIXES = (
    ".workflow/",
    ".git/",
    "LocalNotes/",
    "Builds/",
    "TestResults/",
    "Temp/",
    "Library/",
    "obj/",
    "bin/",
    "__pycache__/",
)

KNOWN_MISSING_SHARED = "Assets/LiteClient/Adapters/Serialization.Luban/MapTableProjection.cs"
CSProj_FILES = (
    "Server/RoomServer/RoomServer.csproj",
    "Server/MetaServer/MetaServer.csproj",
)
WILDCARD_WHITELIST = (
    "Packages/com.code-philosophy.luban/Runtime/*.cs",
    "Assets/GameData/Generated/**/*.cs",
)

AREA_RULES = {
    "shared": {
        "ring": "outer",
        "inner": ["regen_buildhash", "run_authoritative_tests"],
        "delivery": ["regen_buildhash", "rebuild_client_player", "rebuild_server_room", "publish_server"],
    },
    "proto": {
        "ring": "outer",
        "inner": ["regen_proto", "regen_buildhash", "run_authoritative_tests"],
        "delivery": ["regen_proto", "regen_buildhash", "rebuild_client_player", "rebuild_server_room", "publish_server"],
    },
    "luban": {
        "ring": "outer",
        "inner": ["regen_luban", "regen_buildhash", "run_authoritative_tests"],
        "delivery": ["regen_luban", "regen_buildhash", "rebuild_content_bundle", "rebuild_client_player",
                     "rebuild_server_room", "publish_server"],
    },
    "meta": {
        "ring": "outer",
        "inner": ["regen_buildhash", "run_authoritative_tests"],
        "delivery": ["regen_buildhash", "publish_meta"],
    },
    "server": {
        "ring": "outer",
        "inner": ["run_authoritative_tests"],
        "delivery": ["rebuild_server_room", "publish_server"],
    },
    "room_kernel": {
        "ring": "outer",
        "inner": ["editor_reload"],
        "delivery": ["rebuild_server_room", "publish_server"],
    },
    "editor_code": {
        "ring": "inner",
        "inner": ["editor_reload", "run_authoritative_tests"],
        "delivery": [],
    },
    "client_code": {
        "ring": "outer",
        "inner": ["editor_reload", "run_authoritative_tests"],
        "delivery": ["rebuild_client_player"],
    },
    "package": {
        "ring": "outer",
        "inner": ["run_authoritative_tests"],
        "delivery": ["rebuild_client_player"],
    },
    "project_settings": {
        "ring": "outer",
        "inner": ["editor_reload"],
        "delivery": ["rebuild_client_player"],
    },
    "deploy_config": {
        "ring": "inner",
        "inner": ["run_authoritative_tests"],
        "delivery": ["publish_room_config", "restart_room"],
    },
    "content": {
        "ring": "middle",
        "inner": ["editor_reload"],
        "delivery": ["rebuild_content_bundle", "rebuild_client_player"],
    },
    "test": {
        "ring": "inner",
        "inner": ["run_authoritative_tests"],
        "delivery": [],
    },
    "docs": {
        "ring": "inner",
        "inner": [],
        "delivery": [],
    },
    "scripts": {
        "ring": "inner",
        "inner": ["run_authoritative_tests"],
        "delivery": [],
    },
    "unknown": {
        "ring": "outer",
        "inner": ["run_authoritative_tests"],
        "delivery": ["regen_buildhash", "rebuild_client_player", "rebuild_server_room", "publish_server"],
    },
}


def _repo_root():
    return Path(_repo_root_impl())


def _load_build_hash():
    script = _repo_root() / "scripts" / "codegen" / "gen-build-hash.py"
    spec = importlib.util.spec_from_file_location("gen_build_hash_under_test", script)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def normalize_path(path):
    text = str(path).replace("\\", "/").strip()
    while text.startswith("./"):
        text = text[2:]
    parts = []
    for part in text.split("/"):
        if part in ("", "."):
            continue
        if part == ".." and parts and parts[-1] != "..":
            parts.pop()
            continue
        parts.append(part)
    return "/".join(parts)


def _is_shared_path(path, shared_files, shared_targets, shared_links):
    if path in shared_files or path in shared_links or path == KNOWN_MISSING_SHARED:
        return True
    if path.startswith("Assets/LiteClient/Adapters/Serialization.Luban/") and path.endswith(".cs"):
        return True
    if not path.endswith(".cs"):
        return False
    if "/Editor/" in path or path.startswith("Editor/") or "/bin/" in path or "/obj/" in path:
        return False
    return any(path.startswith(target + "/") for target in shared_targets)


def _area_for(path, shared_files, shared_targets, shared_links, meta_targets, meta_extra):
    p = path
    if _is_shared_path(p, shared_files, shared_targets, shared_links):
        return "shared"
    if p.lower().endswith(".proto"):
        return "proto"
    if p.startswith("Luban/Data/") or p.startswith("Luban/Defines/"):
        return "luban"
    if any(p == item or p.startswith(item.rstrip("/") + "/") for item in meta_targets) or p in meta_extra:
        return "meta"
    if p.startswith("Server/RoomServer/") or p.startswith("Server/MetaServer/") or p.startswith("Server/"):
        return "server"
    if p.startswith("Assets/RoomServer/"):
        return "room_kernel"
    if "/Editor/" in p and p.endswith(".cs"):
        return "editor_code"
    if p.endswith(".asmdef"):
        return "client_code"
    if p.endswith(".cs") and (
        p.startswith("Assets/LiteClient/")
        or p.startswith("Assets/LiteGame/")
        or p.startswith("Assets/LiteFramework/")
        or p.startswith("Assets/LiteView/")
        or p.startswith("Assets/LiteSim/")
        or p.startswith("Assets/LiteTesting/")
    ):
        return "client_code"
    if p.startswith("Packages/"):
        return "package"
    if p.endswith((".csproj", ".sln", ".slnx")):
        return "project_settings"
    if p.startswith("ProjectSettings/") or p.startswith("BuildProfiles/"):
        return "project_settings"
    if p == "deploy/roomserver.windows.json":
        return "deploy_config"
    if p.startswith("scripts/") or p.startswith("deploy/") or p.startswith(".github/"):
        return "scripts"
    if p.startswith("Assets/Tests/") or p.startswith("Tests/"):
        return "test"
    if p.startswith("Docs/") or (p.endswith(".md") and "/" not in p):
        return "docs"
    if p.startswith("Assets/GameData/Config/") or p.startswith("Assets/LiteGame/Lua/") \
            or p.startswith("Assets/ShareResource/") or p.startswith("Assets/Scenes/"):
        return "content"
    if p.startswith("Assets/") and not p.endswith(".cs"):
        return "content"
    return "unknown"


def _is_table_value(path):
    return path.startswith("Assets/GameData/Config/") and path.endswith(".bytes")


def _finding_for(path, area, meta_extra):
    rule = AREA_RULES[area]
    inner = list(rule["inner"])
    delivery = list(rule["delivery"])
    base = path[:-5] if path.endswith(".meta") else path
    if area == "meta" and base in meta_extra:
        delivery.extend(["rebuild_server_room", "publish_server"])
    if area == "server" and base.startswith("Server/MetaServer/"):
        delivery = ["publish_meta"]
    if area == "server" and base.startswith("Server/ReplayTool/"):
        delivery = ["none"]
    if area == "package" and base.startswith("Packages/com.code-philosophy.luban/"):
        delivery = ["regen_buildhash", "rebuild_client_player", "rebuild_server_room", "publish_server"]
    if area == "content" and _is_table_value(base):
        delivery.append("publish_server")
    if area == "project_settings" and base.endswith((".csproj", ".sln", ".slnx")):
        delivery = ["rebuild_client_player", "rebuild_server_room"]
    reason = _reason_for(base, area)
    return {
        "path": path,
        "area": area,
        "ring": rule["ring"],
        "innerActions": inner,
        "deliveryActions": delivery,
        "reason": reason,
    }


def _reason_for(path, area):
    if area == "shared":
        return "共享源集/源链接共编件：两端必须同值"
    if area == "proto":
        return "协议源：重生成并双端重建"
    if area == "luban":
        return "表源：重生成读码面并双端重建"
    if area == "meta":
        return "Meta 身份源：发布前复算 BuildHash"
    if area == "server":
        return "服务端宿主：重建并发布上传"
    if area == "room_kernel":
        return "房间内核：服务端重建；本地服档位编辑器重编译"
    if area == "editor_code":
        return "编辑器工具：不进包"
    if area == "client_code":
        return "客户端代码：重打包 Player"
    if area == "package":
        return "Unity 包面：影响客户端/服务端编译"
    if area == "project_settings":
        if path.endswith((".csproj", ".sln", ".slnx")):
            return "工程文件：两端重建"
        return "工程/构建配置：重打包 Player"
    if area == "deploy_config":
        return "部署配置：上传并重启任务，不重打包"
    if area == "content":
        if _is_table_value(path):
            return "内容（表值）：内置包重打包；服务端读同一份 bytes"
        return "内容资源：内环重载；外环内置包需重打包"
    if area == "test":
        return "测试面：跑门禁"
    if area == "docs":
        return "文档面：不需要出包"
    if area == "scripts":
        return "工具/门禁面：不需要出包（按需重跑门禁）"
    return "未识别路径：保守升级为两端重建"


def _actual_link_sets():
    shared = set()
    meta = set()
    for rel in CSProj_FILES:
        path = _repo_root() / rel
        if not path.is_file():
            continue
        for raw in parse_csproj(path.read_text(encoding="utf-8")):
            resolved = _resolve_link(raw, _repo_root())
            if "*" in resolved:
                continue
            if rel.endswith("RoomServer.csproj"):
                shared.add(resolved)
            else:
                meta.add(resolved)
    return shared, meta


def parse_csproj(text):
    links = []
    for match in re.finditer(r'<Compile\s+Include="([^"]+)"', text):
        raw = match.group(1).replace("\\", "/")
        links.append(raw)
    return links


def _resolve_link(raw, project):
    normalized = normalize_path(raw)
    while normalized.startswith("../"):
        normalized = normalized[3:]
    return normalized


def drift_for_links(links, project, registered=None, wildcards=None):
    registered_set = set(registered or ())
    if registered is None:
        build_hash = _load_build_hash()
        registered_set = set(build_hash.SHARED_LINK_FILES) | set(build_hash.META_EXTRA_FILES)
    allowed_wildcards = tuple(WILDCARD_WHITELIST if wildcards is None else wildcards)
    drift = []
    for raw in links:
        resolved = _resolve_link(raw, project)
        if "*" in resolved:
            if resolved in allowed_wildcards:
                continue
            drift.append({"path": resolved, "known": False})
            continue
        if resolved in registered_set:
            continue
        known = resolved == KNOWN_MISSING_SHARED
        drift.append({"path": resolved, "known": known})
    return drift


def classify(paths):
    build_hash = _load_build_hash()
    shared_files = {rel for rel, _ in build_hash.collect(build_hash.SHARED_TARGETS, build_hash.SHARED_LINK_FILES)}
    shared_files.add(KNOWN_MISSING_SHARED)
    shared_targets = tuple(build_hash.SHARED_TARGETS)
    actual_shared, actual_meta = _actual_link_sets()
    shared_links = set(build_hash.SHARED_LINK_FILES) | actual_shared
    meta_targets = tuple(build_hash.META_TARGETS)
    meta_extra = set(build_hash.META_EXTRA_FILES) | actual_meta
    findings = []
    seen = set()
    for raw in paths:
        normalized = normalize_path(raw)
        if not normalized:
            continue
        key = normalized.lower()
        if key in seen:
            continue
        seen.add(key)
        area_path = normalized[:-5] if normalized.endswith(".meta") else normalized
        area = _area_for(area_path, shared_files, shared_targets, shared_links, meta_targets, meta_extra)
        findings.append(_finding_for(normalized, area, meta_extra))
    findings.sort(key=lambda item: item["path"].lower())
    return findings


def _ordered_actions(actions):
    unique = set(actions)
    return [action for action in ACTIONS_ORDER if action in unique]


def _merge_actions(findings, key):
    actions = []
    for finding in findings:
        actions.extend(finding[key])
    return _ordered_actions(actions)


def _manifest_drift():
    build_hash = _load_build_hash()
    registered = {
        "Server/RoomServer/RoomServer.csproj": set(build_hash.SHARED_LINK_FILES),
        "Server/MetaServer/MetaServer.csproj": set(build_hash.META_EXTRA_FILES),
    }
    wildcards = {
        "Server/RoomServer/RoomServer.csproj": WILDCARD_WHITELIST,
        "Server/MetaServer/MetaServer.csproj": (),
    }
    drift = []
    seen = set()
    for rel in CSProj_FILES:
        path = _repo_root() / rel
        if not path.is_file():
            continue
        links = parse_csproj(path.read_text(encoding="utf-8"))
        for entry in drift_for_links(links, _repo_root(), registered=registered.get(rel),
                                     wildcards=wildcards.get(rel, ())):
            key = (entry["path"], entry["known"])
            if key in seen:
                continue
            seen.add(key)
            drift.append(entry)
    return drift


def build_plan(paths, base="", head="", ignored_count=0):
    findings = classify(paths)
    inner = _merge_actions(findings, "innerActions")
    delivery = _merge_actions(findings, "deliveryActions")
    delivery = [action for action in delivery if action != "none"]
    unknown_paths = [item["path"] for item in findings if item["area"] == "unknown"]
    drift = _manifest_drift()
    notes = []
    if not findings:
        notes.append("无改动，不需要出包")
    if any(item["path"] == KNOWN_MISSING_SHARED for item in findings):
        notes.append("已知漂移：MapTableProjection.cs 被服务端源链接但未登记 SHARED_LINK_FILES；按共享件处理，补登记另批")
    if any(item["area"] == "content" for item in findings):
        notes.append("内容仍走内置包：内环无需出包；外环交付需重打包 Player")
    if any(item["area"] == "room_kernel" for item in findings):
        notes.append("房间内核不在握手闭包：交付只重建/发布服务端，客户端不动")
    for entry in drift:
        if not entry["known"]:
            notes.append(f"未登记源链接：{entry['path']}（保守提示）")
    return {
        "schemaVersion": SCHEMA_VERSION,
        "base": base,
        "head": head,
        "innerLoop": inner,
        "delivery": delivery,
        "deliveryNeedsPlayerBuild": "rebuild_client_player" in delivery,
        "findings": findings,
        "unknownPaths": unknown_paths,
        "manifestDrift": drift,
        "ignoredCount": ignored_count,
        "notes": notes,
    }


def _run_git(args, project):
    result = subprocess.run(
        ["git", "-c", "core.quotepath=false", *args], cwd=str(project), capture_output=True, text=True,
        encoding="utf-8", errors="replace",
    )
    if result.returncode != 0:
        raise RuntimeError((result.stderr or result.stdout).strip() or f"git {' '.join(args)} failed")
    return result.stdout


def _parse_name_status(text):
    paths = []
    for line in text.splitlines():
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) >= 3:
            paths.extend([parts[1], parts[2]])
        elif len(parts) == 2:
            paths.append(parts[1])
    return paths


def _ignored(path):
    return any(path.startswith(prefix) for prefix in IGNORED_PREFIXES)


def collect_changes(base, project):
    _run_git(["rev-parse", "--verify", f"{base}^{{commit}}"], project)
    paths = []
    paths.extend(_parse_name_status(_run_git(["diff", "--name-status", f"{base}...HEAD"], project)))
    paths.extend(_parse_name_status(_run_git(["diff", "--name-status"], project)))
    paths.extend(_parse_name_status(_run_git(["diff", "--cached", "--name-status"], project)))
    paths.extend(_run_git(["ls-files", "--others", "--exclude-standard"], project).splitlines())
    kept = []
    ignored_count = 0
    for raw in paths:
        normalized = normalize_path(raw)
        if not normalized:
            continue
        if _ignored(normalized):
            ignored_count += 1
            continue
        kept.append(normalized)
    return kept, ignored_count


def _render_text(plan):
    lines = [f"[build/plan] base={plan['base']} head={plan['head']} ignored={plan['ignoredCount']}"]
    lines.append(f"内环: {', '.join(plan['innerLoop']) if plan['innerLoop'] else '无'}")
    lines.append(f"交付: {', '.join(plan['delivery']) if plan['delivery'] else '不需要出包'}")
    if plan["findings"]:
        lines.append("改动面:")
        for item in plan["findings"]:
            lines.append(f"  [{item['area']}] {item['path']} — {item['reason']}")
    if plan["manifestDrift"]:
        lines.append("源链接对账:")
        for entry in plan["manifestDrift"]:
            state = "已知例外" if entry["known"] else "未登记"
            lines.append(f"  {state}: {entry['path']}")
    for note in plan["notes"]:
        lines.append(f"note: {note}")
    return "\n".join(lines)


def main(argv=None):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass
    parser = argparse.ArgumentParser(description="Build preflight (read-only)")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--changed-file", action="append", default=[])
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--project-path", default=None)
    args = parser.parse_args(argv)

    project = Path(args.project_path).resolve() if args.project_path else _repo_root()
    if project != _repo_root().resolve():
        print(f"[build/plan] project-path must be this repository: {_repo_root()}", file=sys.stderr)
        return 1

    try:
        if args.changed_file:
            paths = []
            ignored_count = 0
            for raw in args.changed_file:
                normalized = normalize_path(raw)
                if _ignored(normalized):
                    ignored_count += 1
                    continue
                paths.append(normalized)
        else:
            paths, ignored_count = collect_changes(args.base, project)
    except RuntimeError as exc:
        print(f"[build/plan] {exc}", file=sys.stderr)
        return 1

    plan = build_plan(paths, base=args.base, head="HEAD", ignored_count=ignored_count)
    if args.json:
        print(json.dumps(plan, ensure_ascii=False, indent=2))
    else:
        print(_render_text(plan))
    return 0


if __name__ == "__main__":
    sys.exit(main())
