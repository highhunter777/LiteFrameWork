# @oneshot - one-off pipeline patch replay; not part of the regular toolchain.
"""Replay the com.unity.pipeline 2022.3 compatibility patches.

Implements patch 1 of Docs/UnityPipeline本地补丁与运维.md §2: rewrite the five
CodeAnalysis plugin metas from upstream's Unity 6 shape (serializedVersion 3,
map-style platformData) to 2022.3's native shape (serializedVersion 2,
list-style platformData).

Why it cannot be a one-line edit: 2022.3 misreads the v3 map-style
platformData as "disabled for every platform" (GetCompatibleWithEditor()=False),
so the Roslyn DLLs never load and IlInterpreter cannot resolve UnityPipeline.*.
The whole platformData block has to be rewritten, not just the version number.

Patches 2-8 (directory moves and the #if source shims) are small and are
documented in §2 rather than scripted here; this file covers the one that is
mechanical enough to get wrong.

Usage (from the project root):
    python scripts/pipeline/patch_p1_plugin_metas.py --pristine <dir>

`--pristine` must point at an untouched copy of Runtime/Plugins/CodeAnalysis
from the upstream tarball, because the GUID and defineConstraints are read from
there -- the in-tree metas are already rewritten by a previous run.
"""
import argparse
import io
import os
import re
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
from _common import repo_root  # noqa: E402

ROOT = str(repo_root())
PKG = os.path.join(ROOT, "Packages", "com.unity.pipeline")
CODE_REL = os.path.join("Runtime", "Plugins", "CodeAnalysis")

GUID_RE = re.compile(r"guid:\s*([0-9a-fA-F]{32})")
# List items under a mapping key are indented two spaces in Unity's own output,
# so the pattern has to tolerate leading whitespace. Requiring the dash at
# column 0 silently matches nothing and falls through to the empty-constraints
# branch, which emits "defineConstraints:" followed by a bare "  []" -- invalid,
# and Unity rejects the whole meta with "Parser Failure ... Expect ':'".
CONSTRAINTS_RE = re.compile(r"^([ ]*)defineConstraints:[ ]*\n((?:\1- .*\n)*)", re.M)

# Editor plus the standalone players the package ships for.
STANDALONE = ("Standalone", "StandaloneWindows", "StandaloneWindows64",
              "StandaloneLinux64", "StandaloneOSX")


def read_constraints(path):
    """Return the defineConstraints block verbatim, or None if the key is absent."""
    src = io.open(path, encoding="utf-8", newline="").read()
    m = CONSTRAINTS_RE.search(src)
    if not m:
        return None
    body = m.group(2)
    return body if body.strip() else ""


def read_guid(path):
    m = GUID_RE.search(io.open(path, encoding="utf-8", errors="replace").read(512))
    return m.group(1) if m else None


def build(guid, constraints):
    out = [
        "fileFormatVersion: 2",
        "guid: %s" % guid,
        "PluginImporter:",
        "  externalObjects: {}",
        "  serializedVersion: 2",
        "  iconMap: {}",
        "  executionOrder: {}",
    ]
    if constraints:
        out.append("  defineConstraints:")
        out.append(constraints.rstrip("\n"))
    else:
        out.append("  defineConstraints: []")
    out += [
        "  isPreloaded: 0",
        "  isOverridable: 1",
        "  isExplicitlyReferenced: 0",
        "  validateReferences: 1",
        "  platformData:",
        "  - first:",
        "      Any:",
        "    second:",
        "      enabled: 0",
        "      settings: {}",
        "  - first:",
        "      Editor: Editor",
        "    second:",
        "      enabled: 1",
        "      settings:",
        "        CPU: AnyCPU",
        "        DefaultValueInitialized: true",
        "        OS: AnyOS",
    ]
    for plat in STANDALONE:
        out += [
            "  - first:",
            "      %s: %s" % (plat, plat),
            "    second:",
            "      enabled: 1",
            "      settings:",
            "        CPU: AnyCPU",
        ]
    out += ["  userData:", "  assetBundleName:", "  assetBundleVariant:", ""]
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--pristine", required=True,
                    help="dir holding untouched CodeAnalysis/*.dll.meta "
                         "(extracted from the upstream tarball)")
    args = ap.parse_args(argv)

    code = os.path.join(PKG, CODE_REL)
    if not os.path.isdir(code):
        print("ABORT: %s not found (package not extracted?)" % code)
        return 1

    metas = sorted(f for f in os.listdir(code) if f.endswith(".dll.meta"))
    if not metas:
        print("ABORT: no *.dll.meta in %s" % code)
        return 1

    n = 0
    for f in metas:
        src = os.path.join(args.pristine, f)
        if not os.path.exists(src):
            print("  MISS pristine source for %s -- skipped" % f)
            continue
        guid = read_guid(src)
        if not guid:
            print("  MISS guid in pristine %s -- skipped" % f)
            continue
        constraints = read_constraints(src)
        io.open(os.path.join(code, f), "w", encoding="utf-8", newline="\n").write(
            build(guid, constraints))
        shown = (constraints or "").strip() or "(none)"
        print("  ok %-58s guid=%s constraints=%s" % (f, guid[:8], shown))
        n += 1

    print("P1: rewrote %d/%d metas to v2" % (n, len(metas)))
    return 0 if n == len(metas) else 1


if __name__ == "__main__":
    sys.exit(main())