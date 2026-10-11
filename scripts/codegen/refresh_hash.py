# -*- coding: utf-8 -*-
# buildHash refresh orchestration.
#
# Why this exists: buildHash is the Join handshake version red line
# (ServerHost.cs: `join.BuildHash != ServerBuildHash` -> reject). It covers the
# three things that must be byte-identical on both ends: Sim decision source
# (LiteSim/Core/Scripts|Systems), protocol (LiteNet/Proto|Protocol) and the
# table data on both ends.
#
# So after touching Sim/protocol/tables you must run all three:
#   1. rerun the generator (regen BuildHash.g.cs constant)
#   2. rebuild RoomServer (ServerBuildHash is the same constant)
#   3. recompile Unity (client bakes the new constant into its assembly)
# Miss any one -> join is rejected; and the symptom surfaces 10 seconds later
# as a Join timeout, far away from the real cause (forgot to rebuild).
# This script fuses the three into one command and prints a two-end reconcile,
# so "forgot a step" cannot stay silent.
#
# Design constraint (Hot-update design doc section 5): buildHash is a strict
# gate, it must NOT be deleted or loosened. This script only refreshes and
# reconciles; it provides no bypass switch.
#
# Toolbox entry:  python scripts/codegen/toolbox.py refresh-hash [--skip-server-build] [--skip-unity-recompile]
# Direct entry:   python scripts/codegen/refresh_hash.py [--config <path>] [--skip-server-build] [--skip-unity-recompile]

import argparse
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import fail, info, repo_root, run_tool, warn

TOOL = 'refresh-hash'


def main():
    parser = argparse.ArgumentParser(prog='refresh_hash.py', description='buildHash refresh orchestration (gen -> server build -> Unity recompile -> reconcile)')
    parser.add_argument('--config', default='Config/roomserver.json')
    parser.add_argument('--skip-server-build', action='store_true')
    parser.add_argument('--skip-unity-recompile', action='store_true')
    args = parser.parse_args()

    root = repo_root()

    # -- 1. rerun the buildHash generator (shared + meta targets, P1④) -----------
    info(TOOL, 'step 1/4 rerun buildHash generator (shared constant + meta build identity)')
    gen_script = os.path.join(root, 'scripts', 'codegen', 'gen-build-hash.py')
    if subprocess.run([sys.executable, gen_script]).returncode != 0:
        fail(TOOL, 'gen-build-hash.py failed')
        return 1

    client_hash_file = os.path.join(root, 'Assets', 'LiteNet', 'Protocol', 'BuildHash.g.cs')
    with open(client_hash_file, 'r', encoding='utf-8-sig') as f:
        m = re.search(r'"([0-9a-f]{16})"', f.read())
    client_hash = m.group(1)
    info(TOOL, f'client constant client={client_hash}')

    # -- 2. rebuild RoomServer (ServerBuildHash is the same constant) ------------
    if not args.skip_server_build:
        info(TOOL, 'step 2/4 rebuild RoomServer')
        env = dict(os.environ)
        env['DOTNET_CLI_HOME'] = os.path.join(root, '.dotnet-cli')
        env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        if subprocess.run(['dotnet', 'build', os.path.join(root, 'Server', 'RoomServer', 'RoomServer.csproj'),
                           '-v:q', '--nologo'], env=env).returncode != 0:
            fail(TOOL, 'RoomServer build failed')
            return 1
    else:
        info(TOOL, 'step 2/4 skip RoomServer build (--skip-server-build)')

    # -- 3. trigger Unity recompile (client bakes the new constant) --------------
    if not args.skip_unity_recompile:
        info(TOOL, 'step 3/4 trigger Unity recompile')
        port_file = os.path.join(root, 'Library', 'Pipeline', '.unity-pipeline-port')
        if not os.path.isfile(port_file):
            warn(TOOL, 'editor not running (no Pipeline port) - recompile Unity manually before joining')
        else:
            subprocess.run(['unity', 'command', 'recompile', '--project-path', root],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            waited = 0
            while True:
                time.sleep(3)
                waited += 3
                probe = subprocess.run(['unity', 'command', 'recompile_status', '--project-path', root],
                                       capture_output=True, text=True)
                status = (probe.stdout or '') + (probe.stderr or '')
                if not re.search(r'"status":"(compiling|triggered)"', status) or waited >= 180:
                    break
            if '"compilationFailed":true' in status:
                fail(TOOL, f'Unity compile failed: {status.strip()}')
                return 1
            info(TOOL, 'Unity recompile done')
    else:
        info(TOOL, 'step 3/4 skip Unity recompile (--skip-unity-recompile)')

    # -- 4. two-end reconcile (really start the server, read its self-reported hash)
    info(TOOL, 'step 4/4 two-end hash reconcile')
    server_hash = None
    try:
        # Start the server, read its self-reported hash, let --duration retire it
        # (no process kill needed).
        out = subprocess.run(
            ['dotnet', 'run', '--project', os.path.join(root, 'Server', 'RoomServer', 'RoomServer.csproj'),
             '--no-build', '--', '--config', os.path.join(root, args.config), '--duration', '1500', '--quiet'],
            capture_output=True, text=True, timeout=600)
        text = (out.stdout or '') + '\n' + (out.stderr or '')
        m = re.search(r'buildHash=([0-9a-f]{16})', text)
        if m:
            server_hash = m.group(1)
    except Exception as exc:  # noqa: BLE001 - probe failure degrades to a warning, not a tool failure
        warn(TOOL, f'server hash probe incomplete: {exc}')

    if server_hash is None:
        warn(TOOL, 'could not read server hash (server not up / port busy) - start it manually to confirm')
        info(TOOL, f'client constant = {client_hash}')
        return 0

    info(TOOL, f'client = {client_hash}')
    info(TOOL, f'server = {server_hash}')
    if client_hash == server_hash:
        info(TOOL, 'OK: both ends match - join will be accepted')
        return 0

    fail(TOOL, 'MISMATCH: join will be rejected (ServerHost buildHash red line)')
    return 1


if __name__ == '__main__':
    run_tool(TOOL, main)
