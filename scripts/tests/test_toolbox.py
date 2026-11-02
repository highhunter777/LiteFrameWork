# -*- coding: utf-8 -*-
"""UT-10..UT-16: toolbox single entry, doctor/trig, deprecated contract, guard.

Pinned by this file (tests-first, implementation tasks T4/T5/T6/T8):

- ``toolbox.ALL_ORDER`` is the strict generation order
  ``luban -> ui-page -> ui-api -> sync-code -> proto -> build-hash`` and never
  contains ``trig``; ``all`` forwards ``--check`` to every step, fails fast
  without ``--protoc`` in generation mode, passes ``--protoc`` only to the proto
  step and stops at the first failing step (exit code normalised to 1);
- ``toolbox.py doctor`` exits 0 on a healthy tree and lists every generator
  (path_report providers) plus the executable call-site inventory;
- ``toolbox.py trig`` is not part of ``all``; without a running Unity Pipeline
  the conditional test is skipped (residual risk recorded in the build log);
- direct invocation of the 7 toolbox generators prints ``[deprecated]`` and
  points at the toolbox; invocation through the toolbox (and the
  ``Luban/gen_lua_keys.py`` helper) does not;
- every literal ``(scripts/)codegen/<script>.py`` under the scanned surface is
  classified in ``fixtures/codegen-call-sites.json``; an ``invocation`` may only
  reference ``codegen/toolbox.py`` (0 unclassified / 0 illegal invocations);
  ``contract-test`` (test_script_contracts_sb.py) and ``compat``
  (Luban/gen.bat) are the only explicit exceptions.
"""

import ast
import contextlib
import importlib.util
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
CODEGEN = ROOT / "scripts" / "codegen"
sys.path.insert(0, str(CODEGEN))
sys.path.insert(0, str(ROOT / "scripts"))

TOOLBOX_LITERAL = "scripts/codegen/toolbox.py"
EXPECTED_ORDER = ["luban", "ui-page", "ui-api", "sync-code", "proto", "build-hash"]
GENERATORS = {
    "build-hash": "gen-build-hash.py",
    "proto": "gen_proto.py",
    "luban": "gen_luban.py",
    "sync-code": "gen_sync_code.py",
    "ui-page": "gen-ui-page.py",
    "ui-api": "gen-ui-api.py",
    "refresh-hash": "refresh-hash.py",
}


def load_toolbox():
    spec = importlib.util.spec_from_file_location("toolbox_under_test", CODEGEN / "toolbox.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


toolbox = load_toolbox()


def run_script(relative, *args, timeout=120):
    return subprocess.run(
        [sys.executable, str(ROOT / relative), *args],
        cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
        errors="replace", stdin=subprocess.DEVNULL, timeout=timeout,
    )


def run_toolbox(*args, timeout=180):
    return run_script("scripts/codegen/toolbox.py", *args, timeout=timeout)


def call_main(argv):
    """Call toolbox.main() in-process, tolerating sys.exit-style implementations."""
    out, err = io.StringIO(), io.StringIO()
    with mock.patch.object(sys, "argv", ["toolbox.py", *argv]), \
         contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        try:
            code = toolbox.main()
        except SystemExit as exc:
            code = exc.code if isinstance(exc.code, int) else 1
    return code, out.getvalue(), err.getvalue()


class FakeAllRun:
    """Intercept forwarded child processes: fake scripts are recorded, never run.

    This also guards the repository: if ``all`` ever bypasses the patched
    ``TOOLS`` registry and tries to execute a real generator, the test fails
    instead of mutating the working tree.
    """

    def __init__(self, fake_tools, return_codes):
        self.fake_tools = fake_tools
        self.return_codes = return_codes
        self.calls = []

    def __call__(self, command, *args, **kwargs):
        parts = [str(part) for part in command]
        for name, (script, _usage, _summary) in self.fake_tools.items():
            if script in parts:
                index = parts.index(script)
                self.calls.append((name, parts[index + 1:]))
                return SimpleNamespace(returncode=self.return_codes.get(name, 0),
                                       stdout="", stderr="")
        raise AssertionError(f"unexpected real subprocess while testing all: {command!r}")


class FakeToolsMixin:
    """Install fake per-tool scripts so ``all`` can be driven without generation."""

    def install_fake_tools(self, temp, return_codes=None):
        marker = Path(temp) / "calls.txt"
        return_codes = return_codes or {}
        fake_tools = {}
        for name in EXPECTED_ORDER:
            script = Path(temp) / f"fake_{name.replace('-', '_')}.py"
            script.write_text(
                "import sys\n"
                f"with open({str(marker)!r}, 'a', encoding='utf-8') as handle:\n"
                f"    handle.write({name!r} + '|' + ','.join(sys.argv[1:]) + '\\n')\n"
                f"sys.exit({return_codes.get(name, 0)})\n",
                encoding="utf-8",
            )
            fake_tools[name] = (str(script), "", f"fake {name}")
        return FakeAllRun(fake_tools, return_codes), fake_tools

    def run_all(self, runner, fake_tools, argv):
        with mock.patch.object(toolbox, "TOOLS", fake_tools), \
             mock.patch.object(toolbox, "ALL_ORDER", EXPECTED_ORDER, create=True), \
             mock.patch.object(toolbox.subprocess, "run", side_effect=runner):
            return call_main(argv)


class IndexAndForwardingTests(unittest.TestCase):
    def test_index_lists_every_tool_and_states_the_deprecated_policy(self):
        result = run_toolbox()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        output = result.stdout + result.stderr
        for name in list(GENERATORS) + ["all", "doctor", "trig"]:
            self.assertIn(name, output)
        self.assertIn("deprecated", output.lower())
        self.assertNotIn("Direct per-script invocation stays valid", output)

    def test_unknown_tool_is_a_usage_error(self):
        result = run_toolbox("no-such-tool")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)


class AllOrderContractTests(FakeToolsMixin, unittest.TestCase):
    def test_all_order_constant_is_the_strict_sequence(self):
        order = getattr(toolbox, "ALL_ORDER", None)
        self.assertIsNotNone(order, "toolbox.ALL_ORDER missing (single source of the all order)")
        self.assertEqual(list(order), EXPECTED_ORDER)
        self.assertNotIn("trig", list(order))

    def test_all_without_protoc_fails_before_running_any_tool(self):
        with tempfile.TemporaryDirectory() as temp:
            runner, fake_tools = self.install_fake_tools(temp)
            code, out, err = self.run_all(runner, fake_tools, ["all"])
            self.assertIn("protoc", (out + err).lower())
            self.assertNotEqual(code, 0)
            self.assertNotEqual(code, None)
            self.assertEqual(runner.calls, [],
                             "missing --protoc must fail fast before any tool runs")

    def test_all_check_runs_every_tool_in_order(self):
        with tempfile.TemporaryDirectory() as temp:
            runner, fake_tools = self.install_fake_tools(temp)
            code, out, err = self.run_all(runner, fake_tools, ["all", "--check"])
            self.assertEqual(code, 0, out + err)
            self.assertEqual(runner.calls,
                             [(name, ["--check"]) for name in EXPECTED_ORDER])

    def test_all_generation_passes_protoc_only_to_the_proto_step(self):
        with tempfile.TemporaryDirectory() as temp:
            runner, fake_tools = self.install_fake_tools(temp)
            code, out, err = self.run_all(
                runner, fake_tools, ["all", "--protoc", "C:/tools/protoc.exe"])
            self.assertEqual(code, 0, out + err)
            self.assertEqual([name for name, _args in runner.calls], EXPECTED_ORDER)
            arguments = dict(runner.calls)
            self.assertEqual(arguments["proto"], ["--protoc", "C:/tools/protoc.exe"])
            for name in EXPECTED_ORDER:
                if name != "proto":
                    with self.subTest(tool=name):
                        self.assertNotIn("protoc", arguments[name])

    def test_all_stops_at_the_first_failing_step(self):
        with tempfile.TemporaryDirectory() as temp:
            runner, fake_tools = self.install_fake_tools(temp, {"ui-api": 1})
            code, out, err = self.run_all(runner, fake_tools, ["all", "--check"])
            self.assertEqual(code, 1, out + err)
            self.assertEqual([name for name, _args in runner.calls],
                             ["luban", "ui-page", "ui-api"])


class DoctorTests(unittest.TestCase):
    def test_doctor_is_green_and_names_every_path_provider(self):
        result = run_toolbox("doctor")
        output = result.stdout + result.stderr
        self.assertEqual(result.returncode, 0, output)
        for name in list(GENERATORS) + ["local-chain"]:
            with self.subTest(tool=name):
                self.assertIn(name, output)

    def test_doctor_prints_the_executable_call_site_inventory(self):
        result = run_toolbox("doctor")
        output = result.stdout + result.stderr
        self.assertRegex(output, r"call[\s_-]?sites?|调用点",
                         "doctor must print the executable call-site inventory")


class TrigContractTests(unittest.TestCase):
    def test_trig_cli_routes_through_the_write_scope_action(self):
        cli = ROOT / "Assets/LiteGame/Editor/Toolchain/Actions/SimTrigCli.cs"
        self.assertTrue(cli.is_file(),
                        "SimTrigCli.cs missing (public CLI entry for toolbox trig)")
        text = cli.read_text(encoding="utf-8", errors="replace")
        self.assertIn("SimTrigToolActions.Run", text,
                      "trig must reuse the ToolWriteScope action, not write directly")
        self.assertNotIn("SimTrigTableGenerator.Generate(", text.replace(" ", ""),
                         "trig must not call the generator's direct write entry")

    def test_sim_trig_generator_exposes_a_read_only_up_to_date_check(self):
        generator = ROOT / "Packages/com.litegame.sim/Editor/SimTrigTableGenerator.cs"
        self.assertTrue(generator.is_file())
        text = generator.read_text(encoding="utf-8", errors="replace")
        self.assertIn("IsUpToDate", text)

    def test_trig_check_with_a_live_unity_pipeline(self):
        port_file = ROOT / "Library" / "Pipeline" / ".unity-pipeline-port"
        if not port_file.is_file() or not shutil.which("unity"):
            self.skipTest("Unity Pipeline not running - toolbox trig needs a live editor "
                          "(residual risk recorded in the build log)")
        result = run_toolbox("trig", "--check", timeout=300)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


class DeprecatedContractTests(unittest.TestCase):
    def test_importing_the_toolbox_does_not_leak_the_transit_env(self):
        self.assertNotIn(
            "CODEGEN_VIA_TOOLBOX", os.environ,
            "toolbox must pass the transit env only to forwarded child processes")

    def test_transit_env_suppresses_the_deprecated_hint(self):
        env = os.environ.copy()
        env["CODEGEN_VIA_TOOLBOX"] = "1"
        result = subprocess.run(
            [sys.executable, str(CODEGEN / "gen-build-hash.py"), "--help"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
            errors="replace", env=env, timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("deprecated", (result.stdout + result.stderr).lower())

    def test_direct_generator_invocation_is_deprecated(self):
        for tool, filename in GENERATORS.items():
            with self.subTest(tool=tool):
                result = run_script(f"scripts/codegen/{filename}", "--help", timeout=60)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                output = (result.stdout + result.stderr).lower()
                self.assertIn("[deprecated]", output,
                              "direct invocation must print a [deprecated] hint")
                self.assertIn("toolbox.py", output)

    def test_toolbox_invocation_is_not_deprecated(self):
        for tool in GENERATORS:
            with self.subTest(tool=tool):
                result = run_toolbox(tool, "--help", timeout=60)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("deprecated", (result.stdout + result.stderr).lower())

    def test_lua_keys_helper_is_out_of_the_deprecated_scope(self):
        result = run_script("Luban/gen_lua_keys.py", "--help", timeout=60)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn("deprecated", (result.stdout + result.stderr).lower())

    def test_failure_hints_point_at_the_toolbox(self):
        for filename, pattern in (
                ("gen-build-hash.py", r"rerun:[^\n]*toolbox\.py"),
                ("gen-ui-page.py", r"CHECK FAILED[^\n]*toolbox\.py"),
                ("gen-ui-api.py", r"CHECK FAILED[^\n]*toolbox\.py"),
        ):
            with self.subTest(script=filename):
                text = (CODEGEN / filename).read_text(encoding="utf-8", errors="replace")
                self.assertRegex(text, pattern)

    def test_ui_page_stale_check_hint_points_at_the_toolbox(self):
        with tempfile.TemporaryDirectory() as temp:
            missing = Path(temp) / "UiPageCatalog.g.cs"
            result = run_script("scripts/codegen/gen-ui-page.py", "--check",
                                "--output", str(missing), timeout=120)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("toolbox.py ui-page", result.stdout + result.stderr)

    def test_ui_tools_use_a_single_codegen_prefix(self):
        for tool, args in (("ui-page", ("--check",)), ("ui-api", ("--check",))):
            with self.subTest(tool=tool):
                result = run_toolbox(tool, *args, timeout=180)
                output = result.stdout + result.stderr
                self.assertEqual(result.returncode, 0, output)
                self.assertIn(f"[codegen/{tool}]", output)
                self.assertNotIn("[codegen/codegen/", output)


# ---------------------------------------------------------------------------
# Executable call-site guard (UT-15)
# ---------------------------------------------------------------------------

CODEGEN_TOKEN = re.compile(r"(?:^|/)(?:scripts/)?codegen/([A-Za-z0-9_.\-]+\.py)")
DATA_FILES = (".workflow/config.json", "scripts/gate/toolchain.json")
EXEMPT_FILES = {
    "scripts/tests/test_toolbox.py",              # the guard itself
    "scripts/tests/fixtures/codegen-call-sites.json",
}
CONTRACT_TEST_FILE = "scripts/tests/test_script_contracts_sb.py"
COMPAT_FILE = "Luban/gen.bat"


def normalise_slashes(text):
    return text.replace("\\", "/")


def token_literals(text):
    return [f"scripts/codegen/{match.group(1)}"
            for match in CODEGEN_TOKEN.finditer(normalise_slashes(text))]


def _call_qualname(func):
    parts = []
    node = func
    while isinstance(node, ast.Attribute):
        parts.append(node.attr)
        node = node.value
    if isinstance(node, ast.Name):
        parts.append(node.id)
    return ".".join(reversed(parts))


def flatten_static(node):
    """Best-effort static text of a string expression.

    Covers ``"a/b" + "c"``, ``Path("scripts") / "codegen" / "gen-x.py"``,
    ``os.path.join(root, "scripts", "codegen", "gen-x.py")`` and backslash
    literals (normalised to '/'). When a leading prefix is dynamic only the
    static suffix is kept, which is enough to see the codegen token.
    """
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return normalise_slashes(node.value)
    if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Div, ast.Add)):
        left = flatten_static(node.left)
        right = flatten_static(node.right)
        separator = "/" if isinstance(node.op, ast.Div) else ""
        if left is not None and right is not None:
            return left + separator + right
        if right is not None:
            return right
        return None
    if isinstance(node, ast.Call):
        name = _call_qualname(node.func)
        if name in ("os.path.join", "ntpath.join", "posixpath.join"):
            parts = [flatten_static(arg) for arg in node.args]
            tail = []
            for part in reversed(parts):
                if part is None:
                    break
                tail.append(part)
            if tail:
                return "/".join(reversed(tail))
    return None


def scan_python_file(path, relative):
    source = path.read_text(encoding="utf-8", errors="replace")
    tree = ast.parse(source, filename=relative)
    sites = set()
    for node in ast.walk(tree):
        text = flatten_static(node)
        if not text:
            continue
        for literal in token_literals(text):
            sites.add((relative, getattr(node, "lineno", 0), literal))
    return sites


def scan_text_file(path, relative):
    sites = set()
    text = path.read_text(encoding="utf-8", errors="replace")
    for line_number, line in enumerate(text.splitlines(), start=1):
        for literal in token_literals(line):
            sites.add((relative, line_number, literal))
    return sites


def scan_call_sites(root):
    sites = set()
    scripts = Path(root) / "scripts"
    for path in sorted(scripts.rglob("*.py")):
        relative = path.relative_to(root).as_posix()
        if relative in EXEMPT_FILES:
            continue
        sites |= scan_python_file(path, relative)
    for path in sorted(scripts.rglob("*.bat")):
        sites |= scan_text_file(path, path.relative_to(root).as_posix())
    luban = Path(root) / "Luban"
    if luban.is_dir():
        for path in sorted(luban.glob("*.bat")):
            sites |= scan_text_file(path, path.relative_to(root).as_posix())
    for relative in DATA_FILES:
        path = Path(root) / relative
        if path.is_file():
            sites |= scan_text_file(path, relative)
    return sorted(sites)


FIXTURE = ROOT / "scripts" / "tests" / "fixtures" / "codegen-call-sites.json"


def load_call_site_fixture():
    with FIXTURE.open(encoding="utf-8") as handle:
        return json.load(handle)


class ScannerCoverageTests(unittest.TestCase):
    def test_scanner_reconstructs_join_concatenation_and_backslashes(self):
        backslash = chr(92)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "scripts" / "build"
            build.mkdir(parents=True)
            (build / "probe.py").write_text(
                "from pathlib import Path\n"
                "import os\n"
                "p = Path('scripts') / 'codegen' / 'gen-build-hash.py'\n"
                "q = os.path.join('scripts', 'codegen', 'gen_sync_code.py')\n"
                "r = 'scripts" + backslash + "codegen" + backslash + "gen_proto.py'\n"
                "s = 'codegen/toolbox.py'\n",
                encoding="utf-8",
            )
            (root / "Luban").mkdir()
            (root / "Luban" / "gen.bat").write_text(
                "python \"%~dp0..\\scripts\\codegen\\gen_luban.py\" %*\n", encoding="utf-8")
            (root / ".workflow").mkdir()
            (root / ".workflow" / "config.json").write_text(
                '{\n  "cmd": ["python", "scripts/codegen/gen-build-hash.py"]\n}\n',
                encoding="utf-8")

            sites = scan_call_sites(root)

        literals = {site[2] for site in sites}
        for expected in ("scripts/codegen/gen-build-hash.py",
                         "scripts/codegen/gen_sync_code.py",
                         "scripts/codegen/gen_proto.py",
                         "scripts/codegen/gen_luban.py",
                         "scripts/codegen/toolbox.py"):
            with self.subTest(literal=expected):
                self.assertIn(expected, literals)


class CallSiteGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sites = scan_call_sites(ROOT)
        cls.fixture = load_call_site_fixture()
        cls.entries = cls.fixture["entries"]

    def fixture_keys(self):
        return {(entry["file"], int(entry["line"]), entry["literal"])
                for entry in self.entries}

    def test_the_scan_surface_covers_config_and_toolchain_manifest(self):
        files = {site[0] for site in self.sites}
        self.assertTrue(self.sites)
        self.assertIn(".workflow/config.json", files)
        self.assertIn("scripts/gate/toolchain.json", files)

    def test_zero_unclassified_call_sites(self):
        classified = self.fixture_keys()
        unclassified = [site for site in self.sites if site not in classified]
        self.assertEqual(
            unclassified, [],
            "unclassified codegen literals (classify them in "
            "scripts/tests/fixtures/codegen-call-sites.json): "
            + "; ".join(f"{f}:{line} {literal}" for f, line, literal in unclassified))

    def test_zero_illegal_invocations(self):
        illegal = [entry for entry in self.entries
                   if entry["kind"] == "invocation" and entry["literal"] != TOOLBOX_LITERAL]
        self.assertEqual(
            illegal, [],
            "invocations must go through codegen/toolbox.py: "
            + "; ".join(f"{e['file']}:{e['line']} {e['literal']}" for e in illegal))

    def test_fixture_has_no_stale_entries(self):
        live = set(self.sites)
        stale = [key for key in self.fixture_keys() if key not in live]
        self.assertEqual(
            stale, [],
            "stale fixture entries (call site moved/removed): "
            + "; ".join(f"{f}:{line} {literal}" for f, line, literal in sorted(stale)))

    def test_kinds_and_explicit_exceptions(self):
        allowed = set(self.fixture["kinds"])
        self.assertTrue(allowed)
        for entry in self.entries:
            with self.subTest(file=entry["file"], line=entry["line"]):
                self.assertIn(entry["kind"], allowed)
                if entry["kind"] == "contract-test":
                    self.assertEqual(entry["file"], CONTRACT_TEST_FILE)
                if entry["kind"] == "compat":
                    self.assertEqual(entry["file"], COMPAT_FILE)
                if entry["kind"] == "toolbox":
                    self.assertEqual(entry["literal"], TOOLBOX_LITERAL)


if __name__ == "__main__":
    unittest.main()
