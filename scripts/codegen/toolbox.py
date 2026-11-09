# -*- coding: utf-8 -*-
"""Generator toolbox - the unified entry over scripts/codegen/*.

Usage:
  python scripts/codegen/toolbox.py                    # list tools
  python scripts/codegen/toolbox.py <tool> [args...]   # run one tool
  python scripts/codegen/toolbox.py all [--check | --protoc <protoc.exe>]
                                                       # whole chain, dependency order
  python scripts/codegen/toolbox.py doctor             # declared path health check
  python scripts/codegen/toolbox.py trig [--check]     # Unity-editor SimTrig table (not in all)

Tools (form/operation details: the generator toolbox design doc, Docs design
center; per-tool why/notes stay in each script's header):
  build-hash    buildHash generator; --check = build gate (Join handshake red line)
  proto         battle.proto -> Packages/com.litegame.net/Core/Proto/Generated/Battle.cs
  luban         table chain regen (C#/binary); alias of Luban/gen.bat
  sync-code     [StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)
  ui-page       #uiform + UIPageManifest + Contracts -> UIPageCatalog.g.cs
  ui-api        UIApiSpec.json -> UIBindIndex/UIBindBase/shim/dispatch/manifest/参考表
  refresh-hash  refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile

`all` runs ALL_ORDER (luban -> ui-page -> ui-api -> sync-code -> proto ->
build-hash, the single source of the order: build-hash's source set contains the
earlier artifacts, so it must run last). Generation mode requires --protoc and
fails fast BEFORE any tool runs without it; --check forwards --check to every
step, never needs protoc and never writes the repository. The first failing step
stops the chain; the exit code is normalised to 1 (usage errors are 2).

`doctor` asks every generator for its declared inputs/outputs via path_report(),
then checks that inputs exist and output parent directories exist, layered by git
tracking state: a missing path is red (tracked in git, or untracked but not
ignored) unless git deliberately ignores it (third-party / large local assets
restored locally), which is only a notice. The Unity-side declarers
(SimTrigTableGenerator.OutputPath and the BakeTargets path constants, including
the MapTarget inline scene/output paths) are parsed with the same layering. It
also prints the executable call-site inventory from
scripts/tests/fixtures/codegen-call-sites.json for manual cross-checking (when the
fixture is missing it prints the unclassified list instead).

Direct per-script invocation (python scripts/codegen/<script>.py) is deprecated:
it still runs, prints one [deprecated] hint on stderr and points back here; the
toolbox (and only the toolbox) is the supported entry, and every generated
artifact's regen hint names `toolbox.py <tool>`. Luban/gen.bat stays a compat
alias of `toolbox.py luban`.
"""

import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys

CODEGEN_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, CODEGEN_DIR)
sys.path.insert(0, os.path.dirname(CODEGEN_DIR))
from _codegen import fail, info, load_module, repo_root, run_tool, warn
from _common import normalize_exit

TOOL = 'toolbox'

# scripts/README convention: script paths resolve relative to this directory,
# so the toolbox works from any CWD. All tools are Python (2026-10-11 migration)
# and run under the SAME interpreter that runs this entry.
TOOLS = {
    'build-hash':   ('gen-build-hash.py', '[--check | --check-shared]',      'buildHash generator; --check is the build gate (Join handshake red line)'),
    'proto':        ('gen_proto.py',      '--protoc <protoc.exe>',           'battle.proto -> Generated/Battle.cs (protoc must match runtime version)'),
    'luban':        ('gen_luban.py',      '[--python <python>]',             'table chain regen (C#/binary); alias of Luban/gen.bat'),
    'sync-code':    ('gen_sync_code.py',  '',                                '[StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)'),
    'ui-page':      ('gen-ui-page.py',    '[--check]',                        '#uiform + UIPageManifest + Contracts -> UIPageCatalog.g.cs'),
    'ui-api':       ('gen-ui-api.py',     '[--check]',                        'UIApiSpec.json -> UIBindIndex/UIBindBase/shim/dispatch/manifest/参考表'),
    'refresh-hash': ('refresh-hash.py',   '[--skip-server-build] [--skip-unity-recompile] [--allow-incomplete]', 'refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile'),
}

# Strict generation order (single source of the `all` sequence). build-hash is
# last because its source set covers the artifacts the earlier steps produce.
# trig is NOT part of `all` (it needs a live Unity editor, and it is not a gate).
ALL_ORDER = ['luban', 'ui-page', 'ui-api', 'sync-code', 'proto', 'build-hash']

# path_report providers doctor consumes. Each generator owns its declared paths
# (constants / DEFAULT_* / OUT_*); doctor must never keep a second hand copy.
DOCTOR_PROVIDERS = (
    ('build-hash', 'gen-build-hash'),
    ('proto', 'gen_proto'),
    ('luban', 'gen_luban'),
    ('sync-code', 'gen_sync_code'),
    ('ui-page', 'gen-ui-page'),
    ('ui-api', 'gen-ui-api'),
    ('refresh-hash', 'refresh-hash'),
)
LOCAL_CHAIN_REL = 'scripts/local/local-chain.py'

# Unity-side declarers: C# constants are not importable from Python, so doctor
# parses the declaring source. BakeTargets carries its paths both as consts and
# as MapTarget inline constructor arguments (e.g. TrainingGround).
UNITY_DECLARERS = (
    ('sim-trig', 'Packages/com.litegame.sim/Editor/SimTrigTableGenerator.cs'),
    ('bake-targets', 'Assets/LiteGame/Editor/Baking/Targets/BakeTargets.cs'),
)
UNITY_REPO_ROOTS = ('Assets/', 'Luban/', 'Tests/', 'Server/', 'Docs/',
                    'ProjectSettings/', 'Packages/')

CALL_SITES_FIXTURE_REL = 'scripts/tests/fixtures/codegen-call-sites.json'
CODEGEN_TOKEN = re.compile(r'(?:^|/)(?:scripts/)?codegen/([A-Za-z0-9_.\-]+\.py)')

ALL_USAGE = """usage:
  python scripts/codegen/toolbox.py all --check                  # freshness gate, every step
  python scripts/codegen/toolbox.py all --protoc <protoc.exe>    # regenerate the whole chain

Order: {order}
Generation mode requires --protoc (protoc is not vendored in the repository);
--check forwards --check to every step and never forwards --protoc.""".format(
    order=' -> '.join(ALL_ORDER))

# ---------------------------------------------------------------------------
# trig: the Unity-side SimTrig table, driven through a live editor
# ---------------------------------------------------------------------------
# The table stays where its single source lives (the runtime SimTrig constants in
# the same compile unit), so the toolbox forwards instead of reimplementing:
# generation goes through LiteGame.Toolchain.SimTrigCli.Run(false) ->
# SimTrigToolActions.Run -> ToolWriteScope (the write allowlist is the
# codegen.sim-trig outputs in scripts/gate/toolchain.json); --check is read-only
# (SimTrigTableGenerator.IsUpToDate, EOL-normalised so a CRLF working copy does
# not read as stale). trig is NOT part of ALL_ORDER and is not a gate: it needs
# a running Unity editor with the unity-pipeline server.
UNITY_PORT_REL = 'Library/Pipeline/.unity-pipeline-port'
TRIG_SOURCE_REL = 'Packages/com.litegame.sim/Scripts/Map/Trig/SimTrigTables.cs'
TRIG_CHECK_CODE = 'return LiteGame.Toolchain.SimTrigCli.IsUpToDate();'
TRIG_GENERATE_CODE = 'return LiteGame.Toolchain.SimTrigCli.Run(false);'
TRIG_EVAL_TIMEOUT = 300

TRIG_USAGE = """usage:
  python scripts/codegen/toolbox.py trig            # regenerate via the live Unity editor
  python scripts/codegen/toolbox.py trig --check    # read-only freshness check

trig drives {source} through a live Unity editor
(LiteGame.Toolchain.SimTrigCli -> SimTrigToolActions.Run -> ToolWriteScope; the
write allowlist is the codegen.sim-trig outputs of scripts/gate/toolchain.json).
It is not part of `all` and is not a gate.""".format(source=TRIG_SOURCE_REL)


def show_index():
    print('Generator toolbox (scripts/codegen/) - unified entry:')
    print('usage: python scripts/codegen/toolbox.py <tool> [args...]')
    for name, (script, usage, summary) in TOOLS.items():
        tail = f' {usage}' if usage else ''
        print(f'  {name:<13} toolbox.py {name}{tail}')
        print(f'  {"":<13} {summary}')
    print(f'  {"all":<13} toolbox.py all [--check | --protoc <protoc.exe>]')
    print(f'  {"":<13} whole chain in ALL_ORDER (strict order; generation fails fast without --protoc)')
    print(f'  {"doctor":<13} toolbox.py doctor')
    print(f'  {"":<13} declared path health check + executable call-site inventory')
    print(f'  {"trig":<13} toolbox.py trig [--check]')
    print(f'  {"":<13} SimTrig table via a live Unity editor (not part of all, not a gate)')
    print('Direct per-script invocation (python scripts/codegen/<script>.py) is deprecated:')
    print('it still runs and prints [deprecated], but you must use this toolbox.')


def _toolbox_env():
    """Transit env for forwarded children: marks the process as toolbox-launched
    so the generator's [deprecated] hint stays silent. Passed explicitly to each
    forwarded subprocess - never assigned to os.environ (importing this module
    must not leak the marker)."""
    return {**os.environ, 'CODEGEN_VIA_TOOLBOX': '1'}


# ---------------------------------------------------------------------------
# all (strict order, fail-fast without --protoc, normalised exit)
# ---------------------------------------------------------------------------

def _tool_script(name):
    """Resolve one registered tool to its script path (absolute registry entries,
    as installed by the tests' fake registry, are kept verbatim)."""
    return os.path.join(CODEGEN_DIR, TOOLS[name][0])


def run_all(argv):
    """`all [--check] [--protoc <path>]`: run ALL_ORDER, stop at the first failure."""
    argv = list(argv)
    if any(arg in ('-h', '--help') for arg in argv):
        print(ALL_USAGE)
        return 0

    check = False
    protoc = None
    index = 0
    while index < len(argv):
        arg = argv[index]
        if arg == '--check':
            check = True
        elif arg == '--protoc':
            if index + 1 >= len(argv):
                fail(TOOL, 'all: --protoc requires a protoc executable path')
                return 2
            protoc = argv[index + 1]
            index += 1
        elif arg.startswith('--protoc='):
            protoc = arg.split('=', 1)[1]
        else:
            fail(TOOL, f"all: unknown argument '{arg}'")
            print(ALL_USAGE, file=sys.stderr)
            return 2
        index += 1

    # Generation mode must fail BEFORE the first tool runs: a half-written tree
    # after a missing protoc is exactly the failure mode this order prevents.
    if not check and not protoc:
        fail(TOOL, 'all: generation mode requires --protoc <protoc.exe> '
                   '(protoc is not vendored; use `all --check` for the freshness gate)')
        print(ALL_USAGE, file=sys.stderr)
        return 2

    total = len(ALL_ORDER)
    for position, name in enumerate(ALL_ORDER, start=1):
        if name not in TOOLS:
            fail(TOOL, f"all: tool '{name}' is not registered in TOOLS")
            return 1
        script = _tool_script(name)
        if check:
            args = ['--check']
        elif name == 'proto':
            args = ['--protoc', protoc]
        else:
            args = []
        info(TOOL, f'all {position}/{total}: {name}{" --check" if check else ""}')
        result = subprocess.run([sys.executable, script, *args], env=_toolbox_env())
        if normalize_exit(TOOL, result.returncode) != 0:
            fail(TOOL, f"all: step {position}/{total} '{name}' failed; stopping")
            return 1
    info(TOOL, f'all: {total} steps OK' + (' (check)' if check else ''))
    return 0


# ---------------------------------------------------------------------------
# doctor (declared path health + call-site inventory)
# ---------------------------------------------------------------------------

def _git_tracked(root):
    """All tracked repo-relative POSIX paths, or None when git is unavailable."""
    try:
        result = subprocess.run(
            ['git', 'ls-files', '-z'], cwd=root, capture_output=True,
            text=True, encoding='utf-8', errors='replace', timeout=120)
    except (OSError, subprocess.SubprocessError):
        return None
    if result.returncode != 0:
        return None
    return {part for part in result.stdout.split('\x00') if part}


def _tracked_sets(files):
    """(files, dirs) where dirs holds every parent prefix of every tracked file,
    so directory inputs are matched without scanning the file list per path."""
    directories = set()
    for path in files:
        parts = path.split('/')
        for depth in range(1, len(parts)):
            directories.add('/'.join(parts[:depth]))
    return files, directories


def _is_tracked(rel, tracked):
    if tracked is None:
        return False
    files, directories = tracked
    rel = rel.replace('\\', '/').rstrip('/')
    return rel in files or rel in directories


def _is_ignored(rel, root):
    """True when git deliberately keeps the path out of the repository
    (third-party / large local assets); those are expected to be restored
    locally, so a missing one is a notice instead of a red finding."""
    try:
        result = subprocess.run(
            ['git', 'check-ignore', '-q', '--', rel], cwd=root,
            capture_output=True, text=True, encoding='utf-8', errors='replace',
            timeout=30)
    except (OSError, subprocess.SubprocessError):
        return False
    return result.returncode == 0


def _posix(rel):
    return str(rel).replace('\\', '/')


def _report_provider(name, entries, root, tracked):
    """Check one provider's declared entries. Returns the number of red findings."""
    inputs = [entry for entry in entries if entry.get('kind') == 'input']
    outputs = [entry for entry in entries if entry.get('kind') == 'output']
    info(TOOL, f'doctor: provider {name}: {len(inputs)} inputs, {len(outputs)} outputs')
    failures = 0
    for entry in entries:
        rel = _posix(entry['path'])
        full = os.path.join(root, *rel.split('/'))
        if entry.get('kind') == 'input':
            if os.path.exists(full):
                info(TOOL, f'doctor:   input  OK      {rel}')
            elif _is_tracked(rel, tracked):
                fail(TOOL, f'doctor:   input  MISSING {rel} (tracked in git)')
                failures += 1
            elif _is_ignored(rel, root):
                info(TOOL, f'doctor:   input  notice  {rel} '
                           f'(git-ignored; local restore expected)')
            else:
                fail(TOOL, f'doctor:   input  MISSING {rel} '
                           f'(not tracked and not git-ignored)')
                failures += 1
        else:
            parent = os.path.dirname(full)
            if os.path.isdir(parent):
                info(TOOL, f'doctor:   output OK      {rel} (parent dir present)')
            else:
                parent_rel = _posix(os.path.relpath(parent, root))
                fail(TOOL, f'doctor:   output MISSING for {rel} '
                           f'(parent dir absent: {parent_rel})')
                failures += 1
    return failures


def _unity_declared_paths(path):
    """Repo-relative asset paths declared by one Unity-side source: the SimTrig
    OutputPath constant, or every path-like string literal in BakeTargets.cs
    (consts and MapTarget inline scene/output arguments)."""
    with open(path, encoding='utf-8', errors='replace') as handle:
        text = handle.read()
    if os.path.basename(path) == 'SimTrigTableGenerator.cs':
        match = re.search(r'OutputPath\s*=\s*"([^"]+)"', text)
        return [match.group(1)] if match else []
    paths = []
    for match in re.finditer(r'"([^"\n]+)"', text):
        literal = match.group(1)
        if not literal.startswith(UNITY_REPO_ROOTS):
            continue
        leaf = literal.rstrip('/').rsplit('/', 1)[-1]
        if '.' not in leaf:
            continue
        if literal not in paths:
            paths.append(literal)
    return paths


def _report_unity(root, tracked):
    failures = 0
    for label, source_rel in UNITY_DECLARERS:
        source = os.path.join(root, *source_rel.split('/'))
        if not os.path.isfile(source):
            fail(TOOL, f'doctor: provider {label}: declaring source missing: {source_rel}')
            failures += 1
            continue
        try:
            paths = _unity_declared_paths(source)
        except OSError as exc:
            fail(TOOL, f'doctor: provider {label}: cannot read {source_rel} ({exc})')
            failures += 1
            continue
        info(TOOL, f'doctor: provider {label}: {len(paths)} declared paths ({source_rel})')
        for rel in paths:
            rel = _posix(rel)
            full = os.path.join(root, *rel.split('/'))
            if os.path.exists(full):
                info(TOOL, f'doctor:   unity  OK      {rel} ({label})')
            elif _is_tracked(rel, tracked):
                fail(TOOL, f'doctor:   unity  MISSING {rel} (tracked in git; declared by {label})')
                failures += 1
            elif _is_ignored(rel, root):
                info(TOOL, f'doctor:   unity  notice  {rel} '
                           f'(git-ignored; declared by {label})')
            else:
                fail(TOOL, f'doctor:   unity  MISSING {rel} '
                           f'(not tracked and not git-ignored; declared by {label})')
                failures += 1
    return failures


def _scan_codegen_literals(root):
    """Fallback inventory only (the classified fixture owns the guard): every
    ``(scripts/)codegen/<script>.py`` literal on the guard's scan surface."""
    targets = []
    scripts = os.path.join(root, 'scripts')
    for dirpath, dirnames, filenames in os.walk(scripts):
        dirnames[:] = sorted(name for name in dirnames if name != '__pycache__')
        for name in sorted(filenames):
            if name.endswith(('.py', '.bat')):
                targets.append(os.path.join(dirpath, name))
    luban = os.path.join(root, 'Luban')
    if os.path.isdir(luban):
        for name in sorted(os.listdir(luban)):
            if name.endswith('.bat'):
                targets.append(os.path.join(luban, name))
    for rel in ('.workflow/config.json', 'scripts/gate/toolchain.json'):
        full = os.path.join(root, *rel.split('/'))
        if os.path.isfile(full):
            targets.append(full)

    sites = set()
    for full in targets:
        relative = _posix(os.path.relpath(full, root))
        try:
            with open(full, encoding='utf-8', errors='replace') as handle:
                text = handle.read()
        except OSError:
            continue
        for line_number, line in enumerate(text.splitlines(), start=1):
            for match in CODEGEN_TOKEN.finditer(_posix(line)):
                sites.add((relative, line_number, 'scripts/codegen/' + match.group(1)))
    return sorted(sites)


def _report_call_sites(root):
    fixture = os.path.join(root, *CALL_SITES_FIXTURE_REL.split('/'))
    entries = None
    if os.path.isfile(fixture):
        try:
            with open(fixture, encoding='utf-8') as handle:
                entries = json.load(handle).get('entries', [])
        except (OSError, ValueError) as exc:
            warn(TOOL, f'doctor: call-site fixture unreadable ({exc}); '
                       f'printing the unclassified list instead')
            entries = None
    if entries is not None:
        info(TOOL, f'doctor: call sites: {len(entries)} classified entries '
                   f'({CALL_SITES_FIXTURE_REL})')
        for entry in entries:
            info(TOOL, f'doctor:   call site {entry.get("file")}:{entry.get("line")} '
                       f'{entry.get("literal")} [{entry.get("kind")}]')
        return
    unclassified = _scan_codegen_literals(root)
    info(TOOL, f'doctor: call sites: fixture missing ({CALL_SITES_FIXTURE_REL}); '
               f'{len(unclassified)} unclassified call sites')
    for relative, line_number, literal in unclassified:
        info(TOOL, f'doctor:   unclassified {relative}:{line_number} {literal}')


def _load_local_chain(root):
    path = os.path.join(root, *LOCAL_CHAIN_REL.split('/'))
    spec = importlib.util.spec_from_file_location('toolbox_doctor_local_chain', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def doctor():
    """Static health check over every generator's declared paths + call sites."""
    root = repo_root()
    tracked_files = _git_tracked(root)
    tracked = None
    if tracked_files is None:
        warn(TOOL, 'doctor: git index unavailable; tracked-path detection is skipped')
    else:
        tracked = _tracked_sets(tracked_files)

    failures = 0
    info(TOOL, 'doctor: declared paths (inputs must exist; output parent dirs must exist)')
    for name, module_name in DOCTOR_PROVIDERS:
        try:
            module = load_module(module_name)
            entries = module.path_report()
        except Exception as exc:  # noqa: BLE001 - one clean line per broken provider
            fail(TOOL, f'doctor: provider {name}: path_report unavailable ({exc})')
            failures += 1
            continue
        failures += _report_provider(name, entries, root, tracked)

    try:
        local_chain = _load_local_chain(root)
        failures += _report_provider('local-chain', local_chain.path_report(), root, tracked)
    except Exception as exc:  # noqa: BLE001 - same contract as the generators above
        fail(TOOL, f'doctor: provider local-chain: path_report unavailable ({exc})')
        failures += 1

    failures += _report_unity(root, tracked)
    _report_call_sites(root)

    if failures:
        fail(TOOL, f'doctor: {failures} path problem(s) found')
        return 1
    info(TOOL, 'doctor: OK - all declared paths are healthy')
    return 0


def _diagnostic_lines(items):
    """One readable line per eval diagnostic (dicts carry id/message/line)."""
    lines = []
    for item in items or []:
        if isinstance(item, dict):
            label = item.get('id') or item.get('severity') or 'diagnostic'
            message = item.get('message') or ''
            location = ''
            if item.get('line'):
                location = f" (line {item.get('line')}:{item.get('column', 0)})"
            lines.append(f'{label}: {message}{location}'.strip())
        else:
            lines.append(str(item))
    return lines


def _unity_eval(root, code):
    """Run one C# snippet in the live editor. Returns (ok, result).

    Every failure (no unity CLI, no live pipeline, timeout, non-JSON output,
    compilation error) prints ONE clean ``[codegen/toolbox] FAILED`` line with
    the editor diagnostics and returns ``(False, None)`` - the caller maps that
    to a non-zero exit code."""
    unity = shutil.which('unity')
    if not unity:
        fail(TOOL, 'trig: the `unity` CLI is not on PATH (it reaches the live Unity '
                   'editor; trig is not part of `all`)')
        return False, None
    port_file = os.path.join(root, *UNITY_PORT_REL.split('/'))
    if not os.path.isfile(port_file):
        fail(TOOL, f'trig: no live Unity Pipeline ({UNITY_PORT_REL} missing); open the '
                   f'Unity editor for this project, then retry (trig is not part of `all`)')
        return False, None

    command = [unity, 'command', 'eval', code,
               '--project-path', root, '--format', 'json']
    try:
        completed = subprocess.run(
            command, capture_output=True, text=True, encoding='utf-8',
            errors='replace', timeout=TRIG_EVAL_TIMEOUT)
    except (OSError, subprocess.SubprocessError) as exc:
        fail(TOOL, f'trig: cannot run `unity command eval`: {exc}')
        return False, None

    text = (completed.stdout or '').strip()
    payload = None
    if text:
        try:
            payload, _end = json.JSONDecoder().raw_decode(text)
        except ValueError:
            payload = None
    if not isinstance(payload, dict):
        tail = text or (completed.stderr or '').strip()
        fail(TOOL, f'trig: `unity command eval` returned no JSON '
                   f'(exit {completed.returncode}): {tail[:400]}')
        return False, None

    data = payload.get('data') if isinstance(payload.get('data'), dict) else {}
    eval_result = data.get('result') if isinstance(data.get('result'), dict) else {}
    if not payload.get('success') or not eval_result.get('success'):
        problems = _diagnostic_lines(eval_result.get('diagnostics'))
        problems += _diagnostic_lines(payload.get('errors'))
        if eval_result.get('error'):
            problems.append(str(eval_result['error']))
        if not problems:
            problems.append(f'exit {completed.returncode}')
        fail(TOOL, 'trig: Unity eval failed: ' + '; '.join(problems)[:800])
        return False, None
    return True, eval_result.get('result')


def run_trig(argv):
    """`trig [--check]`: SimTrig table via the live editor (see the trig block)."""
    argv = list(argv)
    if any(arg in ('-h', '--help') for arg in argv):
        print(TRIG_USAGE)
        return 0
    check = False
    for arg in argv:
        if arg == '--check':
            check = True
        else:
            fail(TOOL, f"trig: unknown argument '{arg}'")
            print(TRIG_USAGE, file=sys.stderr)
            return 2

    root = repo_root()
    ok, result = _unity_eval(root, TRIG_CHECK_CODE if check else TRIG_GENERATE_CODE)
    if not ok:
        return 1
    if check:
        if result:
            info(TOOL, f'trig: up to date ({TRIG_SOURCE_REL})')
            return 0
        fail(TOOL, f'trig: stale - {TRIG_SOURCE_REL} is missing or differs from '
                   f'SimTrigTableGenerator.BuildContent(); '
                   f'run: python scripts/codegen/toolbox.py trig')
        return 1
    report = str(result or '')
    if report.startswith('OK:'):
        info(TOOL, 'trig: ' + report)
        return 0
    fail(TOOL, ('trig: ' + report) if report else 'trig: Unity returned no report')
    return 1


def main():
    argv = sys.argv[1:]
    if not argv or argv[0] in {'-h', '--help', 'help'}:
        show_index()
        return 0

    tool, tool_args = argv[0], argv[1:]
    if tool == 'all':
        return run_all(tool_args)
    if tool == 'doctor':
        return doctor()
    if tool == 'trig':
        return run_trig(tool_args)

    entry = TOOLS.get(tool)
    if entry is None:
        fail(TOOL, f"unknown tool '{tool}' - known tools: {', '.join(TOOLS)}")
        show_index()
        return 2

    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), entry[0])
    if not os.path.isfile(script):
        fail(TOOL, f'tool script missing: {entry[0]}')
        return 1

    result = subprocess.run([sys.executable, script, *tool_args], env=_toolbox_env())
    return normalize_exit('codegen/toolbox', result.returncode)


if __name__ == '__main__':
    run_tool(TOOL, main)
