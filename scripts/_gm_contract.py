# -*- coding: utf-8 -*-
"""Single source for the cloud GM HTTP contract.

`Server/RoomServer/gm-contract.json` is the authority: the Python CLI builds
requests from it, and L0 statically checks the C# server routes and editor GM
page against it. No second action list is allowed on either side.
"""

import json
import re
from pathlib import Path

CONTRACT_RELATIVE = "Server/RoomServer/gm-contract.json"
SERVER_SOURCE = "Server/RoomServer/GmHttpListener.cs"
EDITOR_SOURCE_GLOB = "Assets/LiteGame/Editor/Gm/*.cs"
ROUTE_RE = re.compile(r'"/gm/([a-z0-9-]+)"')


def load(root):
    path = Path(root) / CONTRACT_RELATIVE
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict) or not isinstance(payload.get("actions"), list):
        raise ValueError(f"GM contract has no actions array: {path}")
    return payload


def actions(contract):
    return {action["name"]: action for action in contract["actions"]}


def action_spec(contract, name):
    try:
        return actions(contract)[name]
    except KeyError:
        raise ValueError(f"unknown GM action: {name}")


def check_consistency(root):
    """Return violation strings; empty list means server/editor/CLI agree."""
    root = Path(root)
    contract_path = root / CONTRACT_RELATIVE
    if not contract_path.is_file():
        return []  # minimal fixtures without a server checkout

    try:
        contract = load(root)
        declared = actions(contract)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        return [f"GM contract unreadable: {exc}"]

    violations = []
    names = set(declared)
    for name, spec in declared.items():
        if not spec.get("path") or not spec.get("kind") or "queued" not in spec:
            violations.append(f"GM contract action {name}: path/kind/queued required")
    if len(names) != len(contract["actions"]):
        violations.append("GM contract declares duplicate action names")

    server_path = root / SERVER_SOURCE
    if not server_path.is_file():
        violations.append(f"GM server source missing: {SERVER_SOURCE}")
    else:
        routes = set(ROUTE_RE.findall(server_path.read_text(encoding="utf-8", errors="replace")))
        for name in sorted(names - routes):
            violations.append(f"GM action '{name}' declared in contract but missing from {SERVER_SOURCE}")
        for name in sorted(routes - names):
            violations.append(f"GM route '/gm/{name}' exists in {SERVER_SOURCE} but is not in the contract")

    for path in sorted(root.glob(EDITOR_SOURCE_GLOB)):
        for name in ROUTE_RE.findall(path.read_text(encoding="utf-8", errors="replace")):
            if name not in names:
                violations.append(
                    f"GM editor source {path.relative_to(root).as_posix()} calls undeclared action '{name}'"
                )

    cli = root / "scripts" / "deploy" / "gm.py"
    if cli.is_file() and "_gm_contract" not in cli.read_text(encoding="utf-8", errors="replace"):
        violations.append("scripts/deploy/gm.py does not read the GM contract (_gm_contract)")

    manifest = root / "scripts" / "gate" / "toolchain.json"
    if manifest.is_file():
        try:
            payload = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            violations.append(f"toolchain manifest unreadable for GM enum check: {exc}")
        else:
            for tool in payload.get("tools", []):
                if tool.get("id") != "deploy.gm":
                    continue
                values = set()
                for arg in tool.get("args") or []:
                    if arg.get("name") == "action":
                        values = set(arg.get("values") or [])
                if values and values != names:
                    violations.append(
                        "GM contract actions != toolchain.json deploy.gm action enum "
                        f"(contract-only: {sorted(names - values)}; manifest-only: {sorted(values - names)})"
                    )
    return violations
