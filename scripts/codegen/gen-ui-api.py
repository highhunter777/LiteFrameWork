#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate the UI controlled-API surfaces from one spec.

Input:
  Assets/LiteGame/Editor/UITools/UIApi/UIApiSpec.json

Usage:
  python scripts/codegen/toolbox.py ui-api [--check]     # supported entry
  python scripts/codegen/gen-ui-api.py [--check]         # deprecated (prints [deprecated])
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from _common import repo_root  # noqa: E402
from _codegen import fail as codegen_fail, info, input_digest, run_tool  # noqa: E402
from _codegen import signature as artifact_signature  # noqa: E402

ROOT = Path(repo_root())
TOOL = "ui-api"
SPEC_PATH = ROOT / "Assets" / "LiteGame" / "Editor" / "UITools" / "UIApi" / "UIApiSpec.json"
OUT_INDEX = ROOT / "Packages" / "com.litegame.framework" / "Runtime" / "Services" / "UI" / "Bind" / "Generated" / "UIBindIndex.Api.g.cs"
OUT_BASE = ROOT / "Packages" / "com.litegame.framework" / "Runtime" / "Services" / "UI" / "Bind" / "Generated" / "UIBindBase.Api.g.cs"
OUT_MANIFEST = ROOT / "Packages" / "com.litegame.framework" / "Runtime" / "Services" / "UI" / "Bind" / "Generated" / "UIApiManifest.g.cs"


def _rel(path):
    """Repo-relative POSIX path for a declared constant."""
    return Path(path).resolve().relative_to(ROOT).as_posix()


def path_report():
    """Declared input/outputs derived from SPEC_PATH / OUT_*."""
    entries = [{"path": _rel(SPEC_PATH), "kind": "input"}]
    for output in (OUT_INDEX, OUT_BASE, OUT_MANIFEST):
        entries.append({"path": _rel(output), "kind": "output"})
    return entries

_HEADER = None


def header():
    """The uniform signature block; the spec is this generator's only input.
    Computed lazily so `--help`/import never reads or requires the spec."""
    global _HEADER
    if _HEADER is None:
        spec_rel = _rel(SPEC_PATH)
        _HEADER = "\n".join(artifact_signature("ui-api", spec_rel, input_digest([spec_rel])))
    return _HEADER


KINDS = {
    "string", "text", "bool", "float", "int", "count",
    "formatArgs", "strings", "callbackFloat", "callbackBool", "callbackInt",
    "passthrough",
}


def fail(message):
    """Expected-failure face: one [codegen/ui-api] FAILED line + exit 1."""
    raise SystemExit(codegen_fail(TOOL, message))


def load_spec():
    if not SPEC_PATH.is_file():
        fail("spec missing: " + str(SPEC_PATH))
    with open(SPEC_PATH, "r", encoding="utf-8") as handle:
        spec = json.load(handle)

    categories = {c["id"] for c in spec["categories"]}
    spec_names = set()
    for method in spec["methods"]:
        if not method.get("params"):
            fail("method without params: " + method["name"])
        for api in method.get("api", []):
            name = api["name"]
            if name in spec_names:
                fail("duplicate api name: " + name)
            spec_names.add(name)
            if api["category"] not in categories:
                fail("unknown category '" + api["category"] + "' on " + name)
            for entry in api["call"]:
                if entry["kind"] not in KINDS:
                    fail("unknown kind '" + entry["kind"] + "' on " + name)
                if (entry["kind"] not in ("formatArgs", "passthrough")
                        and not entry["kind"].startswith("callback")
                        and not entry.get("field")):
                    fail("call entry without field on " + name)
                if entry["kind"] == "passthrough" and not api.get("manual"):
                    fail("passthrough is only valid on manual entries: " + name)
            extras = int(api.get("extraArgs", 0))
            formats = [e for e in api["call"] if e["kind"] == "formatArgs"]
            if formats and extras <= 0:
                fail("formatArgs needs extraArgs: " + name)

    for forward in spec["baseForwards"]:
        if "shape" not in forward and not forward.get("forward"):
            fail("base forward without target: " + forward["name"])
    return spec


def xml(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def param_text(param, with_default):
    prefix = "params " if param.get("params") else ""
    text = prefix + param["type"] + " " + param["name"]
    if with_default and param.get("default"):
        text += " = " + param["default"]
    return text


def signature(item, with_defaults):
    generic = "<" + item["generic"] + ">" if item.get("generic") else ""
    params = ", ".join(param_text(p, with_defaults) for p in item.get("params", []))
    constraint = ""
    if item.get("constraint"):
        constraint = " where " + item["generic"] + " : " + item["constraint"]
    return item["returns"] + " " + item["name"] + generic + "(" + params + ")" + constraint


def render_index(spec):
    body = []
    for method in spec["methods"]:
        if method.get("index", True) is False:
            continue
        body.append("        /// <summary>" + xml(method["summary"]) + "</summary>")
        body.append("        public partial " + signature(method, True) + ";")
        body.append("")
    if body:
        body.pop()
    lines = [
        header(),
        "using System;",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace LiteFramework.UI",
        "{",
        "    /// <summary>UIBindIndex 的受控 API 声明（生成；实现体在 UIBindIndex.cs）。</summary>",
        "    public sealed partial class UIBindIndex",
        "    {",
    ]
    lines.extend(body)
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


def render_base(spec):
    body = []
    for forward in spec["baseForwards"]:
        body.append("        /// <summary>" + xml(forward["summary"]) + "</summary>")
        if forward.get("shape") == "bindButtonClick":
            params = ", ".join(p["name"] for p in forward["params"])
            body.append("        " + forward["access"] + " " + signature(forward, True))
            body.append("        {")
            body.append("            _index.BindButtonClick(" + params + ");")
            body.append("            return () => _index.UnbindButtonClick(name);")
            body.append("        }")
        else:
            args = ", ".join(p["name"] for p in forward["params"])
            body.append(
                "        " + forward["access"] + " " + signature(forward, True)
                + " => _index." + forward["forward"] + "(" + args + ");"
            )
        body.append("")
    if body:
        body.pop()
    lines = [
        header(),
        "using System;",
        "using UnityEngine;",
        "",
        "namespace LiteFramework.UI",
        "{",
        "    /// <summary>UIBindBase 的受控 API 转发（生成；生命周期回调与索引装配在 UIBindBase.cs）。</summary>",
        "    public abstract partial class UIBindBase",
        "    {",
    ]
    lines.extend(body)
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


def pascal(name):
    return name[0].upper() + name[1:]


def render_manifest(spec):
    spec_names = [api["name"] for method in spec["methods"] for api in method.get("api", [])]
    generated_names = [pascal(name) for name in spec_names]
    manual_names = [api["name"] for method in spec["methods"] for api in method.get("api", []) if api.get("manual")]
    index_names = [m["name"] for m in spec["methods"] if m.get("index", True) is not False]

    def block(name, values):
        rows = ["        public static IReadOnlyList<string> " + name + " { get; } = new[]", "        {"]
        rows.extend('            "' + value + '",' for value in values)
        rows.extend(["        };", ""])
        return rows

    lines = [
        header(),
        "using System.Collections.Generic;",
        "",
        "namespace LiteFramework.UI",
        "{",
        "    /// <summary>UI API 清单（生成）：版本与门面名清单，供能力声明与守卫测试比对。</summary>",
        "    public static class UIApiManifest",
        "    {",
        "        /// <summary>Bridge API 版本（spec apiVersion；新增 API 时随之递增）。</summary>",
        "        public const int ApiVersion = " + str(spec["apiVersion"]) + ";",
        "",
    ]
    lines.extend(block("SpecMethods", spec_names))
    lines.extend(block("GeneratedMethods", generated_names))
    lines.extend(block("ManualMethods", manual_names))
    lines.extend(block("IndexMethods", index_names))
    if lines[-1] == "":
        lines.pop()
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


def write_or_check(path, text, check):
    if check:
        current = path.read_text(encoding="utf-8") if path.is_file() else ""
        if current != text:
            info(TOOL, "CHECK FAILED: " + str(path) + " 过旧；重跑 python scripts/codegen/toolbox.py ui-api")
            return False
        return True
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")
    info(TOOL, "wrote " + str(path))
    return True


def main(argv=None):
    parser = argparse.ArgumentParser(description="Generate UI API surfaces from UIApiSpec.json")
    parser.add_argument("--check", action="store_true", help="compare generated bytes with checked-in files")
    args = parser.parse_args(argv)

    spec = load_spec()
    index_text = render_index(spec)
    base_text = render_base(spec)
    manifest_text = render_manifest(spec)

    ok = True
    ok = write_or_check(OUT_INDEX, index_text, args.check) and ok
    ok = write_or_check(OUT_BASE, base_text, args.check) and ok
    ok = write_or_check(OUT_MANIFEST, manifest_text, args.check) and ok

    if args.check:
        if ok:
            total = sum(len(m.get("api", [])) for m in spec["methods"])
            info(TOOL, "CHECK OK: " + str(len(spec["methods"])) + " methods / " + str(total) + " spec entries")
            return 0
        return 1
    return 0


if __name__ == "__main__":
    run_tool(TOOL, main)
