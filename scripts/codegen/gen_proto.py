# -*- coding: utf-8 -*-
# Generate LiteNet protocol code from battle.proto. The generated file is
# checked in (Packages/com.litegame.net/Core/Proto/Generated/Battle.cs) so the runtime keeps
# zero toolchain dependency.
#
# protoc source: nuget package Google.Protobuf.Tools (tools/windows_x64/protoc.exe);
# the protoc version MUST match the runtime Google.Protobuf package version.
#
# Toolbox entry:  python scripts/codegen/toolbox.py proto --protoc <protoc.exe>
#                 python scripts/codegen/toolbox.py proto --check [--protoc <protoc.exe>]
# Direct entry:   python scripts/codegen/gen_proto.py --check [--protoc <protoc.exe>]
#                 python scripts/codegen/gen_proto.py --protoc <protoc.exe>
# Output:         Packages/com.litegame.net/Core/Proto/Generated/Battle.cs (generated file, DO NOT EDIT)
#                 Packages/com.litegame.net/Core/Proto/Generated/Battle.cs.input (freshness stamp)
# Rerun when:     battle.proto changed (then rerun build-hash: Proto/ is in its source set)

import argparse
import os
import posixpath
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import info, input_digest, normalize_eol, repo_root, run_tool

TOOL = 'proto'
PROTO_REL = 'Packages/com.litegame.net/Core/Proto/battle.proto'
PROTO_DIR_REL = posixpath.dirname(PROTO_REL)
PROTO_OUT_REL = posixpath.join(PROTO_DIR_REL, 'Generated', 'Battle.cs')
# Freshness stamp next to the protoc output; it is NOT in the BuildHash closure
# (collect only takes .cs). --check reads it when protoc is unavailable.
SIDECAR_REL = posixpath.join(posixpath.dirname(PROTO_OUT_REL), 'Battle.cs.input')
NUGET_HINT = 'nuget Google.Protobuf.Tools, tools/windows_x64/protoc.exe'


def path_report():
    """Declared input/outputs (repo-relative POSIX) derived from PROTO_REL."""
    return [
        {'path': PROTO_REL, 'kind': 'input'},
        {'path': PROTO_OUT_REL, 'kind': 'output'},
        {'path': SIDECAR_REL, 'kind': 'output'},
    ]


def _abs(root, rel):
    return os.path.join(str(root), *rel.split('/'))


def sidecar_stamp(root):
    """One-line stamp: honest about the line-ending normalized input digest."""
    return f'{posixpath.basename(PROTO_REL)} sha256:{input_digest([PROTO_REL], root)}'


def write_sidecar(root):
    path = _abs(root, SIDECAR_REL)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(sidecar_stamp(root) + '\n')


def resolve_protoc(value):
    protoc = shutil.which(value) or (value if os.path.isfile(value) else None)
    if not protoc:
        raise ValueError(f'protoc not found: {value} ({NUGET_HINT})')
    return protoc


def check_regeneration(root, protoc):
    """Regenerate in a temp dir and compare the EOL-normalized text (never the
    working-tree bytes: battle.proto is CRLF in the workspace and LF in the blob,
    so only normalized text is a cross-machine-stable judgment)."""
    proto_dir = _abs(root, PROTO_DIR_REL)
    proto_file = _abs(root, PROTO_REL)
    battle_file = _abs(root, PROTO_OUT_REL)
    with tempfile.TemporaryDirectory(prefix='proto-check-') as temp:
        result = subprocess.run(
            [protoc, f'--proto_path={proto_dir}', f'--csharp_out={temp}', proto_file],
            capture_output=True, text=True, encoding='utf-8', errors='replace')
        if result.returncode != 0:
            raise ValueError(f'protoc generation failed (exit {result.returncode}): {result.stderr.strip()}')
        regenerated = os.path.join(temp, 'Battle.cs')
        if not os.path.isfile(regenerated):
            raise ValueError(f'protoc produced no Battle.cs in the temp directory: {regenerated}')
        with open(regenerated, 'r', encoding='utf-8-sig', newline='') as f:
            fresh = normalize_eol(f.read())
    with open(battle_file, 'r', encoding='utf-8-sig', newline='') as f:
        checked_in = normalize_eol(f.read())
    if fresh != checked_in:
        info(TOOL, f'STALE: {PROTO_OUT_REL} differs from a fresh protoc run '
                   '(rerun toolbox.py proto --protoc <protoc.exe>)')
        return 1
    return 0


def check(root, protoc=''):
    """Freshness gate (read-only): 0 = up to date, 1 = stale.

    Always verifies Battle.cs exists and the sidecar stamp matches the current
    .proto digest; with a protoc path it additionally regenerates into a temp
    directory and compares the normalized text. Never writes the repo."""
    root = str(root)
    battle_file = _abs(root, PROTO_OUT_REL)
    if not os.path.isfile(battle_file):
        info(TOOL, f'STALE: {PROTO_OUT_REL} missing (run toolbox.py proto --protoc <protoc.exe>)')
        return 1

    sidecar = _abs(root, SIDECAR_REL)
    expected = sidecar_stamp(root)
    if not os.path.isfile(sidecar):
        info(TOOL, f'STALE: {SIDECAR_REL} missing (run toolbox.py proto --protoc <protoc.exe>)')
        return 1
    with open(sidecar, 'r', encoding='utf-8') as f:
        recorded = f.read().strip()
    if recorded != expected:
        info(TOOL, f'STALE: {SIDECAR_REL} records {recorded!r}, expected {expected!r} '
                   '(battle.proto changed; rerun toolbox.py proto --protoc <protoc.exe>)')
        return 1

    if protoc:
        code = check_regeneration(root, resolve_protoc(protoc))
        if code != 0:
            return code
        info(TOOL, 'CHECK OK: stamp matches and a fresh protoc run is identical')
        return 0
    info(TOOL, 'CHECK OK: sidecar stamp matches battle.proto '
               '(protoc unavailable; pass --protoc for regeneration comparison)')
    return 0


def main():
    parser = argparse.ArgumentParser(prog='gen_proto.py', description='Generate LiteNet protocol code from battle.proto')
    parser.add_argument('--protoc', required=False, default='',
                        help=f'path to protoc.exe ({NUGET_HINT})')
    parser.add_argument('--check', action='store_true',
                        help='verify Battle.cs freshness (stamp mode without --protoc; read-only)')
    args = parser.parse_args()

    root = repo_root()
    if args.check:
        return check(root, args.protoc)

    proto_dir = _abs(root, PROTO_DIR_REL)
    out_dir = _abs(root, posixpath.dirname(PROTO_OUT_REL))
    proto_file = _abs(root, PROTO_REL)

    if not args.protoc:
        raise ValueError(f'usage: gen_proto.py --protoc <protoc.exe> ({NUGET_HINT})')
    if not os.path.isfile(proto_file):
        raise ValueError(f'battle.proto not found: {proto_file}')
    protoc = resolve_protoc(args.protoc)

    os.makedirs(out_dir, exist_ok=True)
    result = subprocess.run([protoc, f'--proto_path={proto_dir}', f'--csharp_out={out_dir}', proto_file])
    if result.returncode != 0:
        raise ValueError(f'protoc generation failed (exit {result.returncode})')
    if not os.path.isfile(_abs(root, PROTO_OUT_REL)):
        raise ValueError(f'protoc did not produce {PROTO_OUT_REL}')
    write_sidecar(root)
    info(TOOL, f'generated: {PROTO_OUT_REL} (stamp: {SIDECAR_REL})')
    return 0


if __name__ == '__main__':
    run_tool(TOOL, main)
