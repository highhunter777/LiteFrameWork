#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Read a page's bind surface straight from a Unity prefab YAML.

Single source of the bind surface: scope rules (Clone_, nested BindRoot, prefab
template roots), nodePath spelling, type/kind/required derivation and the
controls/text/styles projections all live here (页面契约已退役，本文件是唯一实现).

Public API:
    read_page(prefab_path) -> PageReadResult(controls, text, styles, diagnostics)
    iter_page_controls(prefab_path) -> list[dict]   (bindName/nodePath/type/kind/required)
    iter_page_text(prefab_path)     -> list[dict]   (nodePath/key/args)
    iter_page_styles(prefab_path)   -> list[dict]   (nodePath/role)

Script guid resolution order: repo Assets/** + Packages/** .cs.meta ->
scripts/codegen/ui_builtin_component_types.json (measured built-in/TMP map) ->
PrefabReadError. Library/PackageCache is never needed for guid->type; it is only
used, when present, to re-derive class bases (the committed map is the fallback).
"""

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

BIND_NODE_META = "Assets/LiteCodeGen/Runtime/BindNode.cs.meta"
BIND_ROOT_META = "Assets/LiteCodeGen/Runtime/BindRoot.cs.meta"
TEXT_KEY_REF_META = "Packages/com.litegame.framework/Runtime/Services/UI/Localization/LTextKeyRef.cs.meta"
TEXT_STYLE_REF_META = "Packages/com.litegame.framework/Runtime/Services/UI/Style/UITextStyleRef.cs.meta"

BUILTIN_MAP_PATH = Path(__file__).resolve().with_name("ui_builtin_component_types.json")
BUILTIN_MAP_REGEN = ("python scripts/codegen/_prefab_page_reader.py "
                     "--builtin-map-out scripts/codegen/ui_builtin_component_types.json")
BUILTIN_CONTENT_ROOTS = (
    "Assets/LiteGame/Modules/UI/Content",
    "Assets/LiteGame/Modules/Battle/Content",
)

# UITextRole -> 样式角色拼写（词表单源；改枚举须同批改本表）。
TEXT_ROLE_NAMES = {
    0: "Title",
    1: "Body",
    2: "Caption",
    3: "Label",
    4: "Emphasis",
}
TMP_TEXT_TYPE = "TMPro.TMP_Text"


# Mirrors BindNode.AutoTypeCandidates (Assets/LiteCodeGen/Runtime/BindNode.cs).
AUTO_TYPE_CANDIDATES = (
    "UnityEngine.UI.ScrollRect",
    "UnityEngine.UI.InputField",
    "TMPro.TMP_InputField",
    "TMP.TextMeshProUGUI",
    "TMPro.TextMeshProUGUI",
    "TMPro.TextMeshPro",
    "UnityEngine.UI.Dropdown",
    "UnityEngine.UI.Button",
    "UnityEngine.UI.Text",
    "UnityEngine.UI.RawImage",
    "UnityEngine.UI.Toggle",
    "UnityEngine.UI.Slider",
    "UnityEngine.UI.Scrollbar",
    "UnityEngine.UI.Image",
    "UnityEngine.UI.ToggleGroup",
    "Rigidbody",
    "Rigidbody2D",
    "BoxCollider2D",
    "BoxCollider",
    "CircleCollider2D",
    "SphereCollider",
    "MeshCollider",
    "Collider",
    "Collider2D",
    "Animator",
    "Canvas",
    "Camera",
    "MeshRenderer",
    "SpriteRenderer",
    "ParticleSystem",
    "ParticleSystemRenderer",
)

# 命令型控件判据：基类命中即 Command（含 UGUI Selectable/ScrollRect 与我们自己的列表/控件基类）。
COMMAND_BASE_TYPES = frozenset((
    "UnityEngine.UI.Selectable",
    "UnityEngine.UI.ScrollRect",
    "LiteFramework.UI.VirtualList",
    "LiteFramework.UI.SimpleListView",
    "LiteFramework.UI.UIWidget",
    "LiteFramework.UI.ProgressBar",
    "LiteFramework.UI.Stepper",
    "LiteFramework.UI.UIDropdownWrap",
))

# Component class ids that can win BindNode auto-detection without a script guid.
CLASS_ID_TYPES = {
    4: "UnityEngine.Transform",
    20: "UnityEngine.Camera",
    23: "UnityEngine.MeshRenderer",
    50: "UnityEngine.Rigidbody2D",
    54: "UnityEngine.Rigidbody",
    58: "UnityEngine.CircleCollider2D",
    61: "UnityEngine.BoxCollider2D",
    64: "UnityEngine.MeshCollider",
    65: "UnityEngine.BoxCollider",
    95: "UnityEngine.Animator",
    135: "UnityEngine.SphereCollider",
    198: "UnityEngine.ParticleSystem",
    199: "UnityEngine.ParticleSystemRenderer",
    212: "UnityEngine.SpriteRenderer",
    222: "UnityEngine.CanvasRenderer",
    223: "UnityEngine.Canvas",
    224: "UnityEngine.RectTransform",
    225: "UnityEngine.CanvasGroup",
}

# Engine components above cannot be Selectable subclasses and have no C# source.
NON_COMMAND_ENGINE_TYPES = frozenset(CLASS_ID_TYPES.values()) | frozenset((
    "UnityEngine.GameObject",
    "UnityEngine.MonoBehaviour",
))

_HEADER_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$")
_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)
_NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w.]*)", re.MULTILINE)

TERMINAL_BASES = frozenset((
    "UnityEngine.MonoBehaviour",
    "UnityEngine.Behaviour",
    "UnityEngine.Component",
    "UnityEngine.Object",
    "UnityEngine.ScriptableObject",
    "System.Object",
))


class PrefabReadError(RuntimeError):
    """The prefab cannot be read without guessing (unsupported YAML / unresolved type)."""


# ---------------------------------------------------------------------------
# Unity YAML subset parser (documents, block maps/sequences, flow maps/lists).
# ---------------------------------------------------------------------------

@dataclass
class UnityYamlDoc:
    class_id: int
    file_id: int
    type_name: str
    body: dict
    stripped: bool = False


def _flow_state(text):
    """Return (bracket depth, open quote) for a logical line."""
    depth = 0
    quote = None
    i = 0
    while i < len(text):
        ch = text[i]
        if quote == '"':
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                quote = None
        elif quote == "'":
            if ch == "'":
                if i + 1 < len(text) and text[i + 1] == "'":
                    i += 1
                else:
                    quote = None
        elif ch in "\"'":
            quote = ch
        elif ch in "{[":
            depth += 1
        elif ch in "}]":
            depth -= 1
        i += 1
    return depth, quote


def _logical_lines(raw_lines, source):
    """Join YAML flow collections that Unity wraps across physical lines."""
    out = []
    buf = None
    indent = 0
    for lineno, raw in enumerate(raw_lines, 1):
        if raw.endswith("\r"):
            raw = raw[:-1]
        stripped = raw.lstrip(" ")
        if not stripped:
            continue
        if buf is None:
            indent = len(raw) - len(stripped)
            buf = stripped
        else:
            buf += " " + stripped
        depth, quote = _flow_state(buf)
        if depth > 0 or quote is not None:
            continue
        if depth < 0:
            raise PrefabReadError("%s:%d: unbalanced flow collection" % (source, lineno))
        out.append((indent, buf))
        buf = None
    if buf is not None:
        raise PrefabReadError("%s: unterminated flow collection" % source)
    return out


def _split_key(content):
    quote = None
    i = 0
    while i < len(content):
        ch = content[i]
        if quote == '"':
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                quote = None
        elif quote == "'":
            if ch == "'":
                if i + 1 < len(content) and content[i + 1] == "'":
                    i += 1
                else:
                    quote = None
        elif ch in "\"'":
            quote = ch
        elif ch == ":":
            return content[:i], ":", content[i + 1:]
        i += 1
    return content, "", ""


def _split_top_level(content, separator):
    parts = []
    start = 0
    depth = 0
    quote = None
    i = 0
    while i < len(content):
        ch = content[i]
        if quote == '"':
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                quote = None
        elif quote == "'":
            if ch == "'":
                if i + 1 < len(content) and content[i + 1] == "'":
                    i += 1
                else:
                    quote = None
        elif ch in "\"'":
            quote = ch
        elif ch in "{[":
            depth += 1
        elif ch in "}]":
            depth -= 1
        elif ch == separator and depth == 0:
            parts.append(content[start:i])
            start = i + 1
        i += 1
    parts.append(content[start:])
    return parts


_DOUBLE_ESCAPES = {
    "0": "\0", "a": "\a", "b": "\b", "t": "\t", "n": "\n", "v": "\v",
    "f": "\f", "r": "\r", "e": "\x1b", " ": " ", '"': '"', "/": "/",
    "\\": "\\", "N": "\x85", "_": "\xa0", "L": "\u2028", "P": "\u2029",
}


def _decode_double_quoted(content, source):
    out = []
    i = 0
    while i < len(content):
        ch = content[i]
        if ch != "\\":
            out.append(ch)
            i += 1
            continue
        i += 1
        if i >= len(content):
            raise PrefabReadError("%s: trailing backslash in quoted scalar" % source)
        esc = content[i]
        if esc in _DOUBLE_ESCAPES:
            out.append(_DOUBLE_ESCAPES[esc])
            i += 1
        elif esc in "xuU":
            width = {"x": 2, "u": 4, "U": 8}[esc]
            digits = content[i + 1:i + 1 + width]
            if len(digits) != width or not re.fullmatch(r"[0-9A-Fa-f]+", digits):
                raise PrefabReadError("%s: bad \\%s escape" % (source, esc))
            out.append(chr(int(digits, 16)))
            i += 1 + width
        else:
            raise PrefabReadError("%s: unsupported escape \\%s" % (source, esc))
    return "".join(out)


def _parse_flow_map(content, source):
    inner = content[1:-1].strip()
    result = {}
    if not inner:
        return result
    for part in _split_top_level(inner, ","):
        part = part.strip()
        if not part:
            continue
        key, sep, value = _split_key(part)
        if not sep:
            raise PrefabReadError("%s: malformed flow mapping %r" % (source, part))
        result[key.strip()] = _parse_scalar(value.strip(), source)
    return result


def _parse_flow_seq(content, source):
    inner = content[1:-1].strip()
    if not inner:
        return []
    return [_parse_scalar(part.strip(), source) for part in _split_top_level(inner, ",") if part.strip()]


def _parse_scalar(content, source):
    content = content.strip()
    if content.startswith("{") and content.endswith("}"):
        return _parse_flow_map(content, source)
    if content.startswith("[") and content.endswith("]"):
        return _parse_flow_seq(content, source)
    if content.startswith('"'):
        if not content.endswith('"') or len(content) < 2:
            raise PrefabReadError("%s: unterminated double-quoted scalar %r" % (source, content))
        return _decode_double_quoted(content[1:-1], source)
    if content.startswith("'"):
        if not content.endswith("'") or len(content) < 2:
            raise PrefabReadError("%s: unterminated single-quoted scalar %r" % (source, content))
        return content[1:-1].replace("''", "'")
    return content


def _parse_mapping_entry(content, source):
    key, sep, rest = _split_key(content)
    key = key.strip()
    if not sep or not key or key[0] in "-{[#&*!|\"'" or key[0].isspace():
        raise PrefabReadError("%s: expected 'key: value', got %r" % (source, content))
    return key, rest.strip()


def _looks_like_mapping_entry(content):
    if content[:1] in "{[\"'":
        return False
    key, sep, _rest = _split_key(content)
    return bool(sep) and bool(key.strip())


def _parse_map(lines, start, indent, source):
    result = {}
    i = start
    while i < len(lines):
        line_indent, content = lines[i]
        if line_indent < indent:
            break
        if line_indent > indent:
            raise PrefabReadError("%s: unexpected indentation near %r" % (source, content))
        if content.startswith("- "):
            break
        key, rest = _parse_mapping_entry(content, source)
        if rest:
            result[key] = _parse_scalar(rest, source)
            i += 1
            continue
        j = i + 1
        if j < len(lines) and lines[j][0] == indent and lines[j][1].startswith("- "):
            value, i = _parse_seq(lines, j, indent, source)
        elif j < len(lines) and lines[j][0] > indent:
            value, i = _parse_block(lines, j, lines[j][0], source)
        else:
            value = ""
            i = j
        result[key] = value
    return result, i


def _parse_map_item(lines, start, item_indent, first, source):
    result = {}
    key, rest = _parse_mapping_entry(first, source)
    j = start
    if rest:
        result[key] = _parse_scalar(rest, source)
        j += 1
    elif j + 1 < len(lines) and lines[j + 1][0] > item_indent:
        value, j = _parse_block(lines, j + 1, lines[j + 1][0], source)
        result[key] = value
    else:
        result[key] = ""
        j += 1
    while j < len(lines):
        line_indent, content = lines[j]
        if line_indent != item_indent or content.startswith("- "):
            break
        key, rest = _parse_mapping_entry(content, source)
        if rest:
            result[key] = _parse_scalar(rest, source)
            j += 1
            continue
        if j + 1 < len(lines) and lines[j + 1][0] > item_indent:
            value, j = _parse_block(lines, j + 1, lines[j + 1][0], source)
            result[key] = value
        else:
            result[key] = ""
            j += 1
    return result, j


def _parse_seq(lines, start, indent, source):
    result = []
    i = start
    while i < len(lines):
        line_indent, content = lines[i]
        if line_indent != indent or not content.startswith("- "):
            break
        rest = content[2:]
        item_indent = indent + 2
        while rest.startswith(" "):
            rest = rest[1:]
            item_indent += 1
        if not rest:
            if i + 1 < len(lines) and lines[i + 1][0] > indent:
                value, i = _parse_block(lines, i + 1, lines[i + 1][0], source)
            else:
                value, i = "", i + 1
        elif _looks_like_mapping_entry(rest):
            value, i = _parse_map_item(lines, i, item_indent, rest, source)
        else:
            value = _parse_scalar(rest, source)
            i += 1
        result.append(value)
    return result, i


def _parse_block(lines, start, indent, source):
    if start >= len(lines):
        return "", start
    if lines[start][1].startswith("- "):
        return _parse_seq(lines, start, indent, source)
    return _parse_map(lines, start, indent, source)


def parse_unity_yaml_text(text, source="<text>"):
    docs = []
    header = None
    body_lines = []

    def flush():
        if header is None:
            return
        logical = _logical_lines(body_lines, source)
        if not logical:
            raise PrefabReadError("%s: document !u!%d &%d has no body"
                                  % (source, header[0], header[1]))
        parsed, cursor = _parse_map(logical, 0, logical[0][0], source)
        if cursor != len(logical) or len(parsed) != 1:
            raise PrefabReadError("%s: document !u!%d &%d has an unexpected body shape"
                                  % (source, header[0], header[1]))
        type_name, body = next(iter(parsed.items()))
        docs.append(UnityYamlDoc(header[0], header[1], type_name,
                                 body if isinstance(body, dict) else {},
                                 bool(header[2])))

    for raw in text.splitlines():
        match = _HEADER_RE.match(raw)
        if match:
            flush()
            header = (int(match.group(1)), int(match.group(2)), match.group(3))
            body_lines = []
        elif header is not None:
            body_lines.append(raw)
    flush()
    return docs


def parse_prefab(path):
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            text = handle.read()
    except OSError as exc:
        raise PrefabReadError("cannot read prefab %s: %s" % (path, exc)) from exc
    return parse_unity_yaml_text(text, source=str(path))


# ---------------------------------------------------------------------------
# Script guid -> C# type index (meta scan; sources read lazily).
# ---------------------------------------------------------------------------

@dataclass
class _TypeInfo:
    namespace: str
    bases: list
    kind: str


def _find_repo_root(start):
    here = Path(start).resolve()
    if here.is_file():
        here = here.parent
    for candidate in [here] + list(here.parents):
        if (candidate / "Assets").is_dir() and (candidate / "Packages").is_dir():
            return candidate
    return Path(__file__).resolve().parents[2]


# ---------------------------------------------------------------------------
# Committed built-in/TMP component map (guid -> type name + measured bases).
# Regenerated on a machine that has Library/PackageCache; CI reads it only.
# ---------------------------------------------------------------------------

def _load_builtin_map(path):
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            data = json.load(handle)
    except OSError as exc:
        raise PrefabReadError("cannot read built-in component map %s: %s" % (path, exc)) from exc
    except ValueError as exc:
        raise PrefabReadError("invalid JSON in built-in component map %s: %s" % (path, exc)) from exc
    components = data.get("components")
    bases = data.get("bases")
    if not isinstance(components, dict) or not isinstance(bases, dict):
        raise PrefabReadError("%s: 'components' and 'bases' must be objects" % path)
    for guid, type_name in components.items():
        if not re.fullmatch(r"[0-9a-f]{32}", guid):
            raise PrefabReadError("%s: bad component guid %r" % (path, guid))
        if not isinstance(type_name, str) or not type_name:
            raise PrefabReadError("%s: guid %s has no type name" % (path, guid))
        if type_name not in bases:
            raise PrefabReadError("%s: type %s has no bases entry" % (path, type_name))
    for type_name, parents in bases.items():
        if not isinstance(parents, list) or not all(isinstance(p, str) and p for p in parents):
            raise PrefabReadError("%s: bad bases list for %s" % (path, type_name))
        for parent in parents:
            if parent not in bases and parent not in TERMINAL_BASES \
                    and parent not in COMMAND_BASE_TYPES:
                raise PrefabReadError(
                    "%s: base %s of %s is not covered (regenerate the map)"
                    % (path, parent, type_name))
    return components, bases


def collect_builtin_component_types(repo_root=None, content_roots=None, index=None):
    """Measure built-in/TMP component guids used by UI/Battle content prefabs.

    Needs Library/PackageCache (the only place those .cs.meta files live).
    Returns {"components": {guid: type}, "bases": {type: [base...]},
             "package_roots": [...]}.
    """
    root = Path(repo_root).resolve() if repo_root else _find_repo_root(__file__)
    active_index = index or ScriptTypeIndex(root, include_package_cache=True, load_builtin=False)
    if not (root / "Library" / "PackageCache").is_dir():
        raise PrefabReadError(
            "Library/PackageCache missing: cannot measure built-in component types")
    roots = [root / rel for rel in (content_roots or BUILTIN_CONTENT_ROOTS)]
    script_re = re.compile(r"m_Script:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32})")
    guids = set()
    for content_root in roots:
        if not content_root.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(content_root):
            dirnames.sort()
            for filename in sorted(filenames):
                if not filename.endswith(".prefab"):
                    continue
                with open(os.path.join(dirpath, filename), "r",
                          encoding="utf-8", errors="replace") as handle:
                    guids.update(script_re.findall(handle.read()))
    package_cache = (root / "Library" / "PackageCache").resolve()
    components = {}
    package_roots = set()
    for guid in sorted(guids):
        source_path = active_index._guid_to_source.get(guid)
        if not source_path:
            continue
        resolved = Path(source_path).resolve()
        try:
            resolved.relative_to(package_cache)
        except ValueError:
            continue
        components[guid] = active_index.type_name_of_source(source_path)
        package_roots.add(resolved.relative_to(root).parts[2])
    bases = {}
    stack = list(components.values())
    while stack:
        type_name = stack.pop()
        if type_name in bases:
            continue
        parents = active_index.base_types(type_name)
        bases[type_name] = parents
        for parent in parents:
            if parent not in bases:
                stack.append(parent)
    return {
        "components": components,
        "bases": bases,
        "package_roots": sorted(package_roots),
    }


def write_builtin_map(path, repo_root=None):
    """Regenerate the committed map (dev machine with Library/PackageCache)."""
    measured = collect_builtin_component_types(repo_root)
    payload = {
        "_source": "measured from Library/PackageCache .cs.meta for the component guids "
                   "referenced by Assets/LiteGame/Modules/UI/Content/** and "
                   "Assets/LiteGame/Modules/Battle/Content/** prefabs",
        "_packages": measured["package_roots"],
        "_regenerate": BUILTIN_MAP_REGEN,
        "_note": "components = script guid -> C# type; bases = measured class bases "
                 "(interfaces excluded). _prefab_page_reader.py resolves repo .cs.meta "
                 "first, then this map, then fails; no default is guessed.",
        "components": dict(sorted(measured["components"].items())),
        "bases": {name: measured["bases"][name] for name in sorted(measured["bases"])},
    }
    target = Path(path)
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                      encoding="utf-8", newline="\n")
    return target


class ScriptTypeIndex:
    """guid -> fully-qualified C# type name, and class-base resolution.

    Repo sources (Assets/** + Packages/**) are the first resolution step; the
    committed built-in map covers Unity ugui / TMP components whose .cs.meta
    lives in Library/PackageCache on a normal checkout. PackageCache, when
    present, is only a source for re-deriving bases -- never for guid->type.
    """

    def __init__(self, repo_root=None, builtin_map_path=None, include_package_cache=True,
                 load_builtin=True):
        self.repo_root = Path(repo_root) if repo_root else _find_repo_root(__file__)
        self._guid_to_source = {}
        self._external_guids = set()
        self._source_info = {}
        self._full_to_source = {}
        self._simple_to_sources = {}
        if load_builtin:
            self._builtin_guid_to_type, self._builtin_bases = _load_builtin_map(
                Path(builtin_map_path) if builtin_map_path else BUILTIN_MAP_PATH)
        else:
            self._builtin_guid_to_type, self._builtin_bases = {}, {}
        self._builtin_simple_to_full = {}
        for full_name in self._builtin_bases:
            simple = full_name.rsplit(".", 1)[-1]
            if simple in self._builtin_simple_to_full \
                    and self._builtin_simple_to_full[simple] != full_name:
                self._builtin_simple_to_full[simple] = None
            else:
                self._builtin_simple_to_full.setdefault(simple, full_name)
        self._scan(include_package_cache)

    def _scan(self, include_package_cache):
        roots = [
            (self.repo_root / "Assets", False),
            (self.repo_root / "Packages", False),
            (self.repo_root / "Library" / "PackageCache", True),
        ]
        for root, external in roots:
            if not root.is_dir():
                continue
            if external and not include_package_cache:
                continue
            for dirpath, dirnames, filenames in os.walk(root):
                dirnames[:] = sorted(d for d in dirnames if d != "Samples~")
                for filename in sorted(filenames):
                    if not filename.endswith(".cs.meta"):
                        continue
                    meta_path = os.path.join(dirpath, filename)
                    try:
                        with open(meta_path, "r", encoding="utf-8", errors="replace") as handle:
                            head = handle.read(512)
                    except OSError:
                        continue
                    match = _GUID_RE.search(head)
                    if not match:
                        continue
                    source_path = meta_path[:-5]
                    guid = match.group(1)
                    self._guid_to_source.setdefault(guid, source_path)
                    if external and self._guid_to_source.get(guid) == source_path:
                        self._external_guids.add(guid)
                    self._simple_to_sources.setdefault(Path(source_path).stem, []).append(source_path)

    def _info(self, source_path):
        cached = self._source_info.get(source_path)
        if cached is not None:
            return cached
        try:
            with open(source_path, "r", encoding="utf-8-sig", errors="replace") as handle:
                text = handle.read()
        except OSError as exc:
            raise PrefabReadError("cannot read script %s: %s" % (source_path, exc)) from exc
        ns_match = _NAMESPACE_RE.search(text)
        namespace = ns_match.group(1) if ns_match else ""
        stem = Path(source_path).stem
        decl = re.search(
            r"(?m)^\s*(?:\[[^\]]*\]\s*)*"
            r"(?:(?:public|internal|private|protected|sealed|abstract|static|partial|new)\s+)*"
            r"class\s+%s\b([^\{]*)\{" % re.escape(stem), text)
        kind = "class"
        if not decl:
            if re.search(r"(?m)^\s*(?:(?:public|internal|private|protected|partial)\s+)*"
                         r"interface\s+%s\b" % re.escape(stem), text):
                kind = "interface"
            elif re.search(r"(?m)^\s*(?:(?:public|internal|private|protected|partial)\s+)*"
                           r"struct\s+%s\b" % re.escape(stem), text):
                kind = "struct"
        bases = []
        if decl:
            tail = decl.group(1)
            colon = _find_base_colon(tail)
            if colon >= 0:
                for raw in _split_top_level(tail[colon + 1:], ","):
                    raw = raw.strip()
                    if not raw:
                        continue
                    raw = re.sub(r"<.*>", "", raw, flags=re.DOTALL).strip()
                    raw = raw.split()[-1] if raw.split() else ""
                    if raw:
                        bases.append(raw)
        info = _TypeInfo(namespace, bases, kind)
        self._source_info[source_path] = info
        full_name = ("%s.%s" % (namespace, stem)) if namespace else stem
        self._full_to_source.setdefault(full_name, source_path)
        return info

    def type_name_of_source(self, source_path):
        """Fully-qualified type name for one .cs source (no guid involved)."""
        info = self._info(source_path)
        stem = Path(source_path).stem
        return ("%s.%s" % (info.namespace, stem)) if info.namespace else stem

    def full_name(self, guid):
        source_path = self._guid_to_source.get(guid)
        if source_path and guid not in self._external_guids:
            return self.type_name_of_source(source_path)
        builtin = self._builtin_guid_to_type.get(guid)
        if builtin:
            return builtin
        raise PrefabReadError(
            "unresolved m_Script guid %s (no repo .cs.meta and no entry in %s)"
            % (guid, BUILTIN_MAP_PATH.name))

    def _source_for_full(self, full_name):
        source_path = self._full_to_source.get(full_name)
        if source_path:
            return source_path
        simple = full_name.rsplit(".", 1)[-1]
        namespace = full_name[:-len(simple) - 1] if "." in full_name else None
        for candidate in self._simple_to_sources.get(simple, []):
            info = self._info(candidate)
            if (namespace and info.namespace == namespace) or not namespace:
                self._full_to_source.setdefault(full_name, candidate)
                return candidate
        return None

    def resolve_simple(self, simple, prefer_namespace=None):
        candidates = self._simple_to_sources.get(simple, [])
        if not candidates:
            return self._builtin_simple_to_full.get(simple)
        ordered = candidates
        if prefer_namespace:
            same_ns = [p for p in candidates if self._info(p).namespace == prefer_namespace]
            ordered = same_ns + [p for p in candidates if p not in same_ns]
        path = ordered[0]
        info = self._info(path)
        return ("%s.%s" % (info.namespace, simple)) if info.namespace else simple

    def base_types(self, full_name):
        source_path = self._source_for_full(full_name)
        if not source_path:
            return list(self._builtin_bases.get(full_name, ()))
        info = self._info(source_path)
        resolved = []
        for base in info.bases:
            if "." in base:
                full = base
            else:
                full = self.resolve_simple(base, info.namespace)
            if not full:
                continue
            base_source = self._source_for_full(full)
            if base_source and self._info(base_source).kind != "class":
                continue
            resolved.append(full)
        return resolved

    def is_command_type(self, full_name):
        if full_name in COMMAND_BASE_TYPES:
            return True
        if full_name in NON_COMMAND_ENGINE_TYPES:
            return False
        if self._source_for_full(full_name) is None and "." not in full_name:
            full_name = self.resolve_simple(full_name) or full_name
        if self._source_for_full(full_name) is None and full_name not in self._builtin_bases:
            raise PrefabReadError(
                "cannot prove kind of %s: class source unavailable" % full_name)
        visited = set()
        stack = [full_name]
        while stack:
            current = stack.pop()
            for base in self.base_types(current):
                if base in COMMAND_BASE_TYPES:
                    return True
                if base not in visited and base not in TERMINAL_BASES:
                    visited.add(base)
                    stack.append(base)
        return False

    def is_type_or_subclass(self, full_name, base_name):
        """True when full_name is base_name or derives from it (measured chain)."""
        if full_name == base_name:
            return True
        visited = set()
        stack = [full_name]
        while stack:
            current = stack.pop()
            for base in self.base_types(current):
                if base == base_name:
                    return True
                if base not in visited and base not in TERMINAL_BASES:
                    visited.add(base)
                    stack.append(base)
        return False


def _find_base_colon(tail):
    """Index of the base-list colon in a class declaration tail, -1 if none."""
    depth = 0
    quote = None
    i = 0
    while i < len(tail):
        ch = tail[i]
        if quote == '"':
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                quote = None
        elif quote == "'":
            if ch == "'":
                quote = None
        elif ch in "\"'":
            quote = ch
        elif ch in "([<":
            depth += 1
        elif ch in ")]>":
            depth -= 1
        elif ch == ":" and depth == 0:
            return i
        i += 1
    return -1


# ---------------------------------------------------------------------------
# Prefab -> control surface
# ---------------------------------------------------------------------------

def _ref_id(value):
    if isinstance(value, dict):
        value = value.get("fileID")
    if value is None or value == "":
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _ref_guid(value):
    return value.get("guid") if isinstance(value, dict) else None


@dataclass
class _Scope:
    clone: bool = False
    nested_bind_root: bool = False


@dataclass
class PageReadResult:
    controls: list
    text: list
    styles: list
    diagnostics: dict


class _PrefabModel:
    def __init__(self, path):
        self.path = path
        self.docs = parse_prefab(path)
        self.doc_by_id = {doc.file_id: doc for doc in self.docs}
        self.game_objects = {}
        self.transforms = {}
        self._build_indices()

    def _build_indices(self):
        for doc in self.docs:
            if doc.type_name == "GameObject":
                components = []
                raw = doc.body.get("m_Component") or []
                if isinstance(raw, dict):
                    raw = [raw]
                for item in raw:
                    if isinstance(item, dict):
                        ref = item.get("component", item)
                        component_id = _ref_id(ref)
                        if component_id is not None:
                            components.append(component_id)
                self.game_objects[doc.file_id] = {
                    "name": str(doc.body.get("m_Name", "")),
                    "components": components,
                    "is_active": str(doc.body.get("m_IsActive", "1")) != "0",
                }
            elif doc.class_id in (4, 224):
                self.transforms[doc.file_id] = {
                    "game_object": _ref_id(doc.body.get("m_GameObject")),
                    "father": _ref_id(doc.body.get("m_Father")),
                    "children": [cid for cid in
                                 (_ref_id(item) for item in (doc.body.get("m_Children") or []))
                                 if cid is not None],
                    "stripped": doc.stripped,
                }

    def root_transform(self):
        roots = [fid for fid, tr in self.transforms.items()
                 if tr["father"] in (None, 0) and tr["game_object"] is not None]
        if len(roots) != 1:
            raise PrefabReadError("%s: expected exactly one root transform, found %d"
                                  % (self.path, len(roots)))
        return roots[0]

    def components(self, game_object_id):
        result = []
        for component_id in self.game_objects[game_object_id]["components"]:
            doc = self.doc_by_id.get(component_id)
            if doc is not None:
                result.append(doc)
        return result

    def has_script(self, game_object_id, script_guid):
        return any(doc.type_name == "MonoBehaviour" and _ref_guid(doc.body.get("m_Script")) == script_guid
                   for doc in self.components(game_object_id))

    def first_script(self, game_object_id, script_guid):
        for doc in self.components(game_object_id):
            if doc.type_name == "MonoBehaviour" and _ref_guid(doc.body.get("m_Script")) == script_guid:
                return doc
        return None


def _read_guid(meta_path):
    try:
        with open(meta_path, "r", encoding="utf-8", errors="replace") as handle:
            match = _GUID_RE.search(handle.read(512))
    except OSError as exc:
        raise PrefabReadError("cannot read %s: %s" % (meta_path, exc)) from exc
    if not match:
        raise PrefabReadError("no guid in %s" % meta_path)
    return match.group(1)


class PageReader:
    def __init__(self, repo_root=None, type_index=None, include_package_cache=True):
        self.repo_root = Path(repo_root) if repo_root else _find_repo_root(__file__)
        self.type_index = type_index or ScriptTypeIndex(
            self.repo_root, include_package_cache=include_package_cache)
        self.bind_node_guid = _read_guid(self.repo_root / BIND_NODE_META)
        self.bind_root_guid = _read_guid(self.repo_root / BIND_ROOT_META)
        self.text_key_ref_guid = _read_guid(self.repo_root / TEXT_KEY_REF_META)
        self.text_style_ref_guid = _read_guid(self.repo_root / TEXT_STYLE_REF_META)

    def _component_type(self, doc):
        if doc.type_name == "MonoBehaviour":
            return self.type_index.full_name(_ref_guid(doc.body.get("m_Script")))
        return CLASS_ID_TYPES.get(doc.class_id)

    def _component_types(self, model, game_object_id):
        types = []
        for doc in model.components(game_object_id):
            type_name = self._component_type(doc)
            if type_name is None:
                raise PrefabReadError(
                    "unsupported component class id %d (fileID %d) on BindNode node"
                    % (doc.class_id, doc.file_id))
            types.append(type_name)
        return types

    def _has_text_component(self, model, game_object_id):
        """Mirrors CollectText's TMP_Text / UnityEngine.UI.Text requirement."""
        for doc in model.components(game_object_id):
            type_name = self._component_type(doc)
            if type_name is None:
                continue
            if type_name == "UnityEngine.UI.Text":
                return True
            if self.type_index.is_type_or_subclass(type_name, TMP_TEXT_TYPE):
                return True
        return False

    def _collect_text(self, model, game_object_id, path, out, stats):
        text_doc = model.first_script(game_object_id, self.text_key_ref_guid)
        if text_doc is None:
            return
        key = text_doc.body.get("Key")
        key = "" if key is None else str(key)
        if not key.strip():
            raise PrefabReadError("LTextKeyRef.Key is empty (%s)" % (path or "<root>"))
        if not self._has_text_component(model, game_object_id):
            raise PrefabReadError(
                "LTextKeyRef node has no TMP_Text/Text component (%s)" % (path or "<root>"))
        out.append({"nodePath": path, "key": key, "args": 0})
        stats["text_refs"] += 1

    def _collect_style(self, model, game_object_id, path, out, stats):
        style_doc = model.first_script(game_object_id, self.text_style_ref_guid)
        if style_doc is None:
            return
        raw_role = style_doc.body.get("Role", "1")
        try:
            role = int(str(raw_role).strip())
        except (TypeError, ValueError) as exc:
            raise PrefabReadError(
                "UITextStyleRef.Role is not an enum value: %r (%s)" % (raw_role, path or "<root>")) from exc
        mapped = TEXT_ROLE_NAMES.get(role)
        if mapped is None:
            raise PrefabReadError(
                "UITextStyleRef role cannot be mapped: %d (%s)" % (role, path or "<root>"))
        out.append({"nodePath": path, "role": mapped})
        stats["style_refs"] += 1

    def _resolve_type(self, model, game_object_id, bind_doc):
        custom = str(bind_doc.body.get("CustomTypeName") or "").strip()
        if custom in ("GameObject", "UnityEngine.GameObject"):
            return "UnityEngine.GameObject"
        types = self._component_types(model, game_object_id)
        if custom:
            for type_name in types:
                if type_name == custom:
                    return type_name
            short = custom.rsplit(".", 1)[-1] if "." in custom else None
            if short:
                for type_name in types:
                    if type_name.rsplit(".", 1)[-1] == short:
                        return type_name
            raise PrefabReadError(
                "BindNode CustomTypeName %r cannot be resolved from the prefab" % custom)
        for candidate in AUTO_TYPE_CANDIDATES:
            for type_name in types:
                if type_name is None:
                    continue
                if type_name == candidate:
                    return type_name
                if "." not in candidate and type_name.rsplit(".", 1)[-1] == candidate:
                    return type_name
        for type_name in types:
            if type_name == "UnityEngine.RectTransform":
                return type_name
        for type_name in types:
            if type_name == "UnityEngine.Transform":
                return type_name
        raise PrefabReadError("BindNode node has no resolvable target type")

    def _walk(self, model, transform_id, parent_path, scope, controls, text, styles, stats):
        transform = model.transforms.get(transform_id)
        if transform is None or transform["game_object"] is None:
            stats["opaque_nodes_skipped"] += 1
            return
        game_object = model.game_objects[transform["game_object"]]
        name = game_object["name"]
        is_root = parent_path is None
        clone = scope.clone or (not is_root and name.startswith("Clone_"))
        nested = scope.nested_bind_root or (
            not is_root and model.has_script(transform["game_object"], self.bind_root_guid))
        path = "" if is_root else (name if parent_path == "" else parent_path + "/" + name)
        if not clone and not nested:
            bind_doc = model.first_script(transform["game_object"], self.bind_node_guid)
            if bind_doc is not None:
                stats["bind_nodes"] += 1
                bind_name = str(bind_doc.body.get("BindName") or "").strip()
                if bind_name:
                    type_name = self._resolve_type(model, transform["game_object"], bind_doc)
                    command = self.type_index.is_command_type(type_name)
                    controls.append({
                        "bindName": bind_name,
                        "nodePath": path,
                        "type": type_name,
                        "kind": "Command" if command else "None",
                        "required": command,
                    })
                    stats["controls_collected"] += 1
                    if str(bind_doc.body.get("m_Enabled", "1")) == "0":
                        stats["disabled_bind_nodes"] += 1
                    if not game_object["is_active"]:
                        stats["inactive_game_objects"] += 1
                else:
                    stats["empty_bind_names"] += 1
            self._collect_text(model, transform["game_object"], path, text, stats)
            self._collect_style(model, transform["game_object"], path, styles, stats)
        else:
            stats["clone_scoped_nodes_skipped" if clone else "nested_bind_root_nodes_skipped"] += 1
        for child_id in transform["children"]:
            self._walk(model, child_id, path, _Scope(clone, nested), controls, text, styles, stats)

    def read(self, prefab_path):
        model = _PrefabModel(prefab_path)
        stats = {
            "bind_nodes": 0,
            "controls_collected": 0,
            "text_refs": 0,
            "style_refs": 0,
            "empty_bind_names": 0,
            "disabled_bind_nodes": 0,
            "disabled_components": 0,
            "inactive_game_objects": 0,
            "clone_scoped_nodes_skipped": 0,
            "nested_bind_root_nodes_skipped": 0,
            "opaque_nodes_skipped": 0,
            "prefab_instances": 0,
        }
        for doc in model.docs:
            if doc.class_id == 1001:
                stats["prefab_instances"] += 1
            if "m_Enabled" in doc.body and str(doc.body.get("m_Enabled")) == "0":
                stats["disabled_components"] += 1
        controls, text, styles = [], [], []
        self._walk(model, model.root_transform(), None, _Scope(), controls, text, styles, stats)
        bind_names = [item["bindName"] for item in controls]
        if len(set(bind_names)) != len(bind_names):
            duplicates = sorted({name for name in bind_names if bind_names.count(name) > 1})
            raise PrefabReadError("duplicate BindName(s): %s" % ", ".join(duplicates))
        dfs_paths = [item["nodePath"] for item in controls]
        _sort_by_node_path(controls)
        _sort_by_node_path(text)
        _sort_by_node_path(styles)
        stats["dfs_order_changed"] = dfs_paths != [item["nodePath"] for item in controls]
        stats["controls"] = len(controls)
        stats["text"] = len(text)
        stats["styles"] = len(styles)
        return PageReadResult(controls, text, styles, stats)


def _sort_by_node_path(items):
    """string.CompareOrdinal order: UTF-16 code units, big-endian byte compare."""
    items.sort(key=lambda item: item["nodePath"].encode("utf-16-be", "surrogatepass"))


_DEFAULT_READER = None


def _default_reader():
    global _DEFAULT_READER
    if _DEFAULT_READER is None:
        _DEFAULT_READER = PageReader()
    return _DEFAULT_READER


def read_page(prefab_path, reader=None):
    """Return controls + text + styles（各自按 nodePath 升序）。"""
    return (reader or _default_reader()).read(prefab_path)


def iter_page_controls(prefab_path, reader=None):
    """Return the page's controls（nodePath 升序）。"""
    return read_page(prefab_path, reader).controls


def iter_page_text(prefab_path, reader=None):
    """Return the page's text bindings（nodePath 升序）。"""
    return read_page(prefab_path, reader).text


def iter_page_styles(prefab_path, reader=None):
    """Return the page's style bindings（nodePath 升序）。"""
    return read_page(prefab_path, reader).styles


def main(argv=None):
    parser = argparse.ArgumentParser(description="Read a page's bind surface from a Unity prefab.")
    parser.add_argument("prefab", nargs="?", help="page prefab path")
    parser.add_argument("--json", action="store_true", help="print controls as JSON")
    parser.add_argument("--builtin-map-out", default="",
                        help="regenerate the committed built-in component map (needs Library/PackageCache)")
    args = parser.parse_args(argv)
    if args.builtin_map_out:
        target = write_builtin_map(args.builtin_map_out)
        print("wrote %s" % target)
        return 0
    if not args.prefab:
        parser.error("a prefab path is required (or pass --builtin-map-out)")
    result = _default_reader().read(args.prefab)
    if args.json:
        print(json.dumps(result.controls, ensure_ascii=False, indent=2))
    else:
        for control in result.controls:
            print("%-10s %-24s %-36s %s" % (
                control["kind"], control["bindName"], control["type"], control["nodePath"]))
        for text in result.text:
            print("text       %-24s %-36s %s" % (text["key"], text["nodePath"], text["args"]))
        for style in result.styles:
            print("style      %-24s %s" % (style["role"], style["nodePath"]))
    print("controls=%d text=%d styles=%d diagnostics=%s"
          % (len(result.controls), len(result.text), len(result.styles), result.diagnostics),
          file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
