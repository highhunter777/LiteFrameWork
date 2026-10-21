"""Scan for GUID references that resolve to no asset on disk.

Counts a GUID as present if some .meta under Assets/, Packages/ or
Library/PackageCache claims it; collects references from serialized assets under
Assets/ plus all of ProjectSettings/.

Kept out of Temp/ on purpose: Unity wipes Temp/ on every editor launch, and this
needs to survive to be re-run after a restart.
"""
import argparse
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GUID_RE = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
SCAN_EXT = (".unity", ".prefab", ".asset", ".mat", ".controller", ".anim",
            ".playable", ".overrideController", ".renderTexture", ".shadergraph")


def collect_present():
    present = {}
    for base in ("Assets", "Packages", "Library/PackageCache"):
        for dirpath, _dirs, files in os.walk(os.path.join(ROOT, base)):
            for f in files:
                if not f.endswith(".meta"):
                    continue
                p = os.path.join(dirpath, f)
                try:
                    with open(p, "rb") as fh:
                        m = GUID_RE.search(fh.read(400))
                except OSError:
                    continue
                if m:
                    present[m.group(1).decode()] = os.path.relpath(p, ROOT)
    return present


def collect_missing(present):
    missing = {}
    for base in ("Assets", "ProjectSettings"):
        for dirpath, _dirs, files in os.walk(os.path.join(ROOT, base)):
            for f in files:
                if base == "ProjectSettings" or f.endswith(SCAN_EXT):
                    p = os.path.join(dirpath, f)
                    try:
                        with open(p, "rb") as fh:
                            data = fh.read()
                    except OSError:
                        continue
                    for m in GUID_RE.finditer(data):
                        g = m.group(1).decode()
                        if g.startswith("0000000000000000") or g in present:
                            continue
                        missing.setdefault(g, set()).add(os.path.relpath(p, ROOT))
    return missing


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Scan Assets/ProjectSettings for GUID references with no owning asset.")
    parser.parse_args(argv)

    present = collect_present()
    missing = collect_missing(present)
    print("[quality/guids] assets with a guid: %d" % len(present))
    print("[quality/guids] UNRESOLVED GUID references: %d" % len(missing))
    for g in sorted(missing):
        owners = sorted(missing[g])
        print("  %s  <- %s" % (
            g, ", ".join(os.path.basename(o) for o in owners[:3])))
    return 0


if __name__ == "__main__":
    sys.exit(main())