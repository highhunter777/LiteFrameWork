# -*- coding: utf-8 -*-
# Generate LiteNet protocol code from battle.proto. The generated file is
# checked in (Assets/LiteNet/Core/Proto/Generated/Battle.cs) so the runtime keeps
# zero toolchain dependency.
#
# protoc source: nuget package Google.Protobuf.Tools (tools/windows_x64/protoc.exe);
# the protoc version MUST match the runtime Google.Protobuf package version.
#
# Toolbox entry:  python scripts/codegen/toolbox.py proto --protoc <protoc.exe>
# Direct entry:   python scripts/codegen/gen_proto.py --protoc <protoc.exe>
# Output:         Assets/LiteNet/Core/Proto/Generated/Battle.cs (generated file, DO NOT EDIT)
# Rerun when:     battle.proto changed (then rerun build-hash: Proto/ is in its source set)

import argparse
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import info, repo_root, run_tool

TOOL = 'proto'
PROTO_REL = 'Assets/LiteNet/Core/Proto/battle.proto'
NUGET_HINT = 'nuget Google.Protobuf.Tools, tools/windows_x64/protoc.exe'


def main():
    parser = argparse.ArgumentParser(prog='gen_proto.py', description='Generate LiteNet protocol code from battle.proto')
    parser.add_argument('--protoc', required=False, default='',
                        help=f'path to protoc.exe ({NUGET_HINT})')
    args = parser.parse_args()

    root = repo_root()
    proto_dir = os.path.join(root, 'Assets', 'LiteNet', 'Proto')
    out_dir = os.path.join(proto_dir, 'Generated')
    proto_file = os.path.join(proto_dir, 'battle.proto')

    if not args.protoc:
        raise ValueError(f'usage: gen_proto.py --protoc <protoc.exe> ({NUGET_HINT})')
    if not os.path.isfile(proto_file):
        raise ValueError(f'battle.proto not found: {proto_dir}')
    protoc = shutil.which(args.protoc) or (args.protoc if os.path.isfile(args.protoc) else None)
    if not protoc:
        raise ValueError(f'protoc not found: {args.protoc} ({NUGET_HINT})')

    result = subprocess.run([protoc, f'--proto_path={proto_dir}', f'--csharp_out={out_dir}', proto_file])
    if result.returncode != 0:
        raise ValueError(f'protoc generation failed (exit {result.returncode})')
    info(TOOL, 'generated: Assets/LiteNet/Core/Proto/Generated/Battle.cs')
    return 0


if __name__ == '__main__':
    run_tool(TOOL, main)
