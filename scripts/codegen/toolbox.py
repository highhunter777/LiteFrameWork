# -*- coding: utf-8 -*-
"""Generator toolbox - the unified entry over scripts/codegen/*.

Usage:
  python scripts/codegen/toolbox.py                    # list tools
  python scripts/codegen/toolbox.py <tool> [args...]   # run one tool

Tools (form/operation details: the generator toolbox design doc, Docs design
center; per-tool why/notes stay in each script's header):
  build-hash    buildHash generator; --check = build gate (Join handshake red line)
  proto         battle.proto -> Assets/LiteNet/Proto/Generated/Battle.cs
  luban         table chain regen (C#/binary/lua/LuaKeys); alias of Luban/gen.bat
  sync-code     [StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)
  ui-page       #uiform + UiPageManifest + Contracts -> UiPageCatalog.g.cs
  ui-api        UiApiSpec.json -> UIBindIndex/UIBindBase/shim/dispatch/manifest/参考表
  refresh-hash  refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile

Direct per-script invocation stays valid (CI/gate callers keep their exact
commands); this entry only forwards arguments and the child exit code.
"""

import os
import subprocess
import sys

CODEGEN_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, CODEGEN_DIR)
sys.path.insert(0, os.path.dirname(CODEGEN_DIR))
from _codegen import fail, run_tool
from _common import normalize_exit

TOOL = 'toolbox'

# scripts/README convention: script paths resolve relative to this directory,
# so the toolbox works from any CWD. All tools are Python (2026-10-11 migration)
# and run under the SAME interpreter that runs this entry.
TOOLS = {
    'build-hash':   ('gen-build-hash.py', '[--check | --check-shared]',      'buildHash generator; --check is the build gate (Join handshake red line)'),
    'proto':        ('gen_proto.py',      '--protoc <protoc.exe>',           'battle.proto -> Generated/Battle.cs (protoc must match runtime version)'),
    'luban':        ('gen_luban.py',      '[--python <python>]',             'table chain regen (C#/binary/lua/LuaKeys); alias of Luban/gen.bat'),
    'sync-code':    ('gen_sync_code.py',  '',                                '[StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)'),
    'ui-page':      ('gen-ui-page.py',    '[--check]',                        '#uiform + UiPageManifest + Contracts -> UiPageCatalog.g.cs'),
    'ui-api':       ('gen-ui-api.py',     '[--check]',                        'UiApiSpec.json -> UIBindIndex/UIBindBase/shim/dispatch/manifest/参考表'),
    'refresh-hash': ('refresh-hash.py',   '[--skip-server-build] [--skip-unity-recompile] [--allow-incomplete]', 'refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile'),
}


def show_index():
    print('Generator toolbox (scripts/codegen/) - unified entry:')
    print('usage: python scripts/codegen/toolbox.py <tool> [args...]')
    for name, (script, usage, summary) in TOOLS.items():
        tail = f' {usage}' if usage else ''
        print(f'  {name:<13} toolbox.py {name}{tail}')
        print(f'  {"":<13} {summary}')
    print('Direct per-script invocation stays valid; this entry only forwards args.')


def main():
    argv = sys.argv[1:]
    if not argv or argv[0] in {'-h', '--help', 'help'}:
        show_index()
        return 0

    tool, tool_args = argv[0], argv[1:]
    entry = TOOLS.get(tool)
    if entry is None:
        fail(TOOL, f"unknown tool '{tool}' - known tools: {', '.join(TOOLS)}")
        show_index()
        return 2

    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), entry[0])
    if not os.path.isfile(script):
        fail(TOOL, f'tool script missing: {entry[0]}')
        return 1

    result = subprocess.run([sys.executable, script, *tool_args])
    return normalize_exit('codegen/toolbox', result.returncode)


if __name__ == '__main__':
    run_tool(TOOL, main)
