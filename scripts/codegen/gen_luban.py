# -*- coding: utf-8 -*-
# Regenerate the Luban table chain (C#/binary/lua/LuaKeys) and publish only
# changed files. Luban clears its output directory, so generation runs OUTSIDE
# Assets (TestResults staging) and publish copies only declared file types,
# preserving existing asmdef/meta files under Assets.
#
# Toolbox entry:  python scripts/codegen/toolbox.py luban
# Direct entry:   python scripts/codegen/gen_luban.py [--python <python>]
# Shim alias:     Luban/gen.bat (delegates here)
# Outputs:        Assets/GameData/Generated/*.cs, Assets/GameData/Config/*.bytes,
#                 Assets/LiteGame/Lua/Cfg/*.lua, Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs
# Rerun when:     table source (Luban/Data/*.xlsx) or schema changed - then rerun
#                 build-hash (table data is in its source set); Unity import is separate.

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import info, repo_root, run_tool

TOOL = 'luban'

# Staged -> published destinations (extension-scoped; only these file types publish).
PUBLISH_TARGETS = [
    ('code',      os.path.join('Assets', 'GameData', 'Generated'),          '.cs'),
    ('bin',       os.path.join('Assets', 'GameData', 'Config'),             '.bytes'),
    ('lua',       os.path.join('Assets', 'LiteGame', 'Lua', 'Cfg'),         '.lua'),
    ('lua-keys',  os.path.join('Assets', 'LiteGame', 'LuaBridge', 'Generated'), '.cs'),
]


def sha256_b64(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()


def run_checked(cmd):
    result = subprocess.run(cmd)
    if result.returncode != 0:
        raise ValueError(f'command failed (exit {result.returncode}): {" ".join(str(c) for c in cmd)}')


def parse_table_layout(cs_path):
    # Single source: TableLayout.Files entries are "Domain/table" (domain-relative, no extension).
    # The generator parses the runtime constant instead of keeping its own copy (修改指南 §1.2).
    import re
    with open(cs_path, encoding='utf-8') as f:
        text = f.read()
    match = re.search(r'public static readonly string\[\] Files\s*=\s*\{(.*?)\};', text, re.S)
    if not match:
        raise ValueError(f'TableLayout.Files not found: {cs_path}')
    domain_by_table = {}
    for entry in re.findall(r'"([^"]+)"', match.group(1)):
        parts = entry.split('/')
        if len(parts) != 2:
            raise ValueError(f'TableLayout entry must be "Domain/table": {entry}')
        domain_by_table[parts[1]] = parts[0]
    if not domain_by_table:
        raise ValueError('TableLayout.Files is empty')
    return domain_by_table


def reorganize_bin_stage(stage_dir, domain_by_table):
    # Move staged *.bytes into Domain/ subdirs before publish (publish preserves subpaths);
    # fail-fast on a staged table missing from TableLayout.Files.
    for name in sorted(os.listdir(stage_dir)):
        if not name.endswith('.bytes'):
            continue
        domain = domain_by_table.get(name[:-len('.bytes')])
        if domain is None:
            raise ValueError(f'staged table not in TableLayout.Files: {name}')
        target_dir = os.path.join(stage_dir, domain)
        os.makedirs(target_dir, exist_ok=True)
        shutil.move(os.path.join(stage_dir, name), os.path.join(target_dir, name))


def staged_relatives(source_dir, extension):
    return {
        os.path.relpath(os.path.join(dirpath, name), source_dir).replace('\\', '/')
        for dirpath, _dirnames, filenames in os.walk(source_dir)
        for name in filenames if name.endswith(extension)
    }


def prune_stale(target_dir, extension, staged):
    # Delete published files the pipeline no longer produces (layout moves leave stale flat files).
    for dirpath, _dirnames, filenames in os.walk(target_dir):
        for name in sorted(filenames):
            if not name.endswith(extension):
                continue
            relative = os.path.relpath(os.path.join(dirpath, name), target_dir).replace('\\', '/')
            if relative not in staged:
                os.remove(os.path.join(dirpath, name))
                info(TOOL, f'prune stale: {relative}')


def publish(source_dir, target_dir, extension):
    # Preserve an existing text file's line endings; new text uses LF. Binary stays exact.
    for dirpath, _dirnames, filenames in os.walk(source_dir):
        for name in sorted(filenames):
            if not name.endswith(extension):
                continue
            source = os.path.join(dirpath, name)
            relative = os.path.relpath(source, source_dir)
            target = os.path.join(target_dir, relative)
            if extension != '.bytes':
                with open(source, 'r', encoding='utf-8-sig', newline='') as f:
                    text = f.read().replace('\r\n', '\n').replace('\r', '\n')
                if os.path.isfile(target) and '\r\n' in open(target, 'r', encoding='utf-8-sig', newline='').read():
                    text = text.replace('\n', '\r\n')
                with open(source, 'w', encoding='utf-8', newline='') as f:
                    f.write(text)
            if os.path.isfile(target) and sha256_b64(source) == sha256_b64(target):
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(source, target)
            info(TOOL, f'publish: {relative}')


def main():
    parser = argparse.ArgumentParser(prog='gen_luban.py', description='Regenerate the Luban table chain and publish changed files')
    parser.add_argument('--python', default=sys.executable if getattr(sys, 'frozen', False) is False else 'python',
                        help='python used for gen_lua_keys.py (default: this interpreter)')
    args = parser.parse_args()

    root = repo_root()
    luban_root = os.path.join(root, 'Luban')
    luban_exe = os.path.join(luban_root, 'LubanGenerater', 'Luban', 'Luban.exe')
    config_path = os.path.join(luban_root, 'luban.conf')
    stage_root = os.path.join(root, 'TestResults', 'luban-generation', uuid.uuid4().hex)

    if not os.path.isfile(luban_exe):
        raise ValueError(f'Luban.exe not found: {luban_exe}')

    # Luban clears its output directory. Generate outside Assets so existing asmdef/meta
    # and unrelated generated assets remain intact; publish only declared file types.
    for stage in PUBLISH_TARGETS:
        os.makedirs(os.path.join(stage_root, stage[0]), exist_ok=True)
    run_checked([luban_exe, '-t', 'client', '-c', 'cs-bin', '-d', 'bin', '--conf', config_path,
                 '-x', f'outputCodeDir={os.path.join(stage_root, "code")}',
                 '-x', f'outputDataDir={os.path.join(stage_root, "bin")}'])
    run_checked([luban_exe, '-t', 'client', '-d', 'lua', '--conf', config_path,
                 '-x', f'outputDataDir={os.path.join(stage_root, "lua")}'])
    run_checked([args.python, os.path.join(luban_root, 'gen_lua_keys.py'),
                 '--output', os.path.join(stage_root, 'lua-keys', 'LuaKeys.g.cs')])

    domain_by_table = parse_table_layout(os.path.join(
        root, 'Assets', 'LiteClient', 'Adapters', 'Serialization.Luban', 'TableLayout.cs'))
    reorganize_bin_stage(os.path.join(stage_root, 'bin'), domain_by_table)

    for stage, target_rel, extension in PUBLISH_TARGETS:
        source_dir = os.path.join(stage_root, stage)
        target_dir = os.path.join(root, target_rel)
        publish(source_dir, target_dir, extension)
        if extension == '.bytes':
            prune_stale(target_dir, extension, staged_relatives(source_dir, extension))
    info(TOOL, 'completed; Unity import and validation are separate.')
    return 0


if __name__ == '__main__':
    run_tool(TOOL, main)
