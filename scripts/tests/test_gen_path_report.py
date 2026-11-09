# -*- coding: utf-8 -*-
"""UT-1/UT-3: every generator declares its own I/O paths from its own constants.

Contract pinned by this file (tests-first, implementation tasks T1):

- every Python generator (build-hash / proto / luban / sync-code / ui-page /
  ui-api) and the two read-only path consumers (refresh-hash / local-chain)
  exposes ``path_report()``;
- ``path_report()`` returns a list of dicts:
    {"path": <repo-relative POSIX path, no "./", no backslash>,
     "kind": "input" | "output",
     "optional": <bool, optional - defaults to False>}
  a directory declaration carries the bare directory path (no trailing slash);
- the declared paths are derived from the module's own constants (no second
  hand-copied list): outputs must equal the module's output constants, inputs
  must cover the module's input constants;
- paths are declared repo-relative. Nothing here executes a generator.

Also pins the shared single-source helpers (``_codegen.normalize_eol`` /
``input_digest`` / ``load_module`` and ``gen-build-hash.shared_out_file``) that
refresh-hash / local-chain / proto consume.
"""

import hashlib
import importlib
import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CODEGEN = ROOT / "scripts" / "codegen"
sys.path.insert(0, str(CODEGEN))
sys.path.insert(0, str(ROOT / "scripts"))

BUILTIN_PRODUCTS = {
    "build-hash": ("gen-build-hash.py", CODEGEN),
    "proto": ("gen_proto.py", CODEGEN),
    "luban": ("gen_luban.py", CODEGEN),
    "sync-code": ("gen_sync_code.py", CODEGEN),
    "ui-page": ("gen-ui-page.py", CODEGEN),
    "ui-api": ("gen-ui-api.py", CODEGEN),
    "refresh-hash": ("refresh-hash.py", CODEGEN),
    "local-chain": ("local-chain.py", ROOT / "scripts" / "local"),
}

SHARED_OUT_REL = "Packages/com.litegame.net/Core/Protocol/BuildHash.g.cs"
BUILD_HASH_INPUTS = (
    "Packages/com.litegame.sim/Scripts",
    "Packages/com.litegame.sim/Systems",
    "Packages/com.litegame.net/Core/Proto",
    "Packages/com.litegame.net/Core/Protocol",
    "Packages/com.litegame.net/Core/Transport/Security",
    "Packages/com.litegame.sync/Shared",
    "Packages/com.litegame.sync/Snapshot",
    "Packages/com.litegame.sync/Input",
    "Packages/com.litegame.sync/Events",
    "Assets/GameData/Generated",
)


def load(name, filename, folder=CODEGEN):
    spec = importlib.util.spec_from_file_location(name, folder / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def rel_of(path):
    """Repo-relative POSIX string for an absolute path under ROOT."""
    return Path(path).resolve().relative_to(ROOT).as_posix()


def fetch_report(module, label):
    """Call path_report() and normalise it, failing on contract violations."""
    reporter = getattr(module, "path_report", None)
    if reporter is None:
        raise AssertionError(f"{label} does not expose path_report()")
    entries = list(reporter())
    if not entries:
        raise AssertionError(f"{label}.path_report() returned no entries")
    normalised = []
    for entry in entries:
        if not isinstance(entry, dict):
            raise AssertionError(f"{label}.path_report() entry is not a dict: {entry!r}")
        path = entry.get("path")
        kind = entry.get("kind")
        if not isinstance(path, str) or not path:
            raise AssertionError(f"{label}.path_report() entry without path: {entry!r}")
        if kind not in ("input", "output"):
            raise AssertionError(f"{label}.path_report() entry with bad kind: {entry!r}")
        if path.startswith("./") or path.startswith("/") or "\\" in path:
            raise AssertionError(
                f"{label}.path_report() path must be repo-relative POSIX: {path!r}")
        if path.endswith("/"):
            raise AssertionError(f"{label}.path_report() path keeps a trailing slash: {path!r}")
        normalised.append({
            "path": path,
            "kind": kind,
            "optional": bool(entry.get("optional", False)),
        })
    return normalised


def paths_of(entries, kind):
    return [entry["path"] for entry in entries if entry["kind"] == kind]


def covers(covered, declared):
    """True when a declared output equals / is a parent of the covered path."""
    return declared == covered or covered.startswith(declared + "/")


def tracked_under(paths):
    """Tracked file set for the declared paths (doctor's tracked/untracked layering)."""
    result = subprocess.run(
        ["git", "ls-files", "--", *sorted(paths)],
        cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
        errors="replace", timeout=120,
    )
    if result.returncode != 0:
        raise AssertionError(f"git ls-files failed: {result.stderr}")
    return {line.strip() for line in result.stdout.splitlines() if line.strip()}


def is_tracked(path, tracked):
    return path in tracked or any(item.startswith(path + "/") for item in tracked)


class PathReportShapeTests(unittest.TestCase):
    def test_every_generator_and_consumer_exposes_path_report(self):
        for label, (filename, folder) in BUILTIN_PRODUCTS.items():
            with self.subTest(tool=label):
                module = load(f"path_report_{label.replace('-', '_')}", filename, folder)
                fetch_report(module, label)

    def test_generators_declare_at_least_one_input_and_output(self):
        generators = ("build-hash", "proto", "luban", "sync-code", "ui-page", "ui-api")
        for label in generators:
            with self.subTest(tool=label):
                filename, folder = BUILTIN_PRODUCTS[label]
                entries = fetch_report(
                    load(f"path_report_io_{label.replace('-', '_')}", filename, folder), label)
                self.assertTrue(paths_of(entries, "input"), f"{label}: no input declared")
                self.assertTrue(paths_of(entries, "output"), f"{label}: no output declared")

    def test_declared_tracked_paths_exist_on_disk(self):
        """A declared non-optional *tracked* path that does not exist is exactly
        the rot doctor must flag. Untracked machine-private inputs
        (deploy/.env, third-party assets) are hints only, so a clean clone must
        not go red for them."""
        reports = {}
        for label, (filename, folder) in BUILTIN_PRODUCTS.items():
            module = load(f"path_report_exists_{label.replace('-', '_')}", filename, folder)
            reports[label] = fetch_report(module, label)
        declared = {entry["path"] for entries in reports.values() for entry in entries}
        tracked = tracked_under(declared)
        for label, entries in reports.items():
            for entry in entries:
                if entry["optional"]:
                    continue
                if not is_tracked(entry["path"], tracked):
                    continue
                with self.subTest(tool=label, path=entry["path"]):
                    self.assertTrue(
                        (ROOT / entry["path"]).exists(),
                        f"{label} declares a missing tracked path: {entry['path']}")


class PathSingleSourceTests(unittest.TestCase):
    """Declared paths must equal the module's own constants (no second copy)."""

    def test_build_hash_outputs_and_source_dirs(self):
        module = load("path_report_build_hash", "gen-build-hash.py")
        entries = fetch_report(module, "build-hash")
        outputs = paths_of(entries, "output")
        inputs = paths_of(entries, "input")

        self.assertIn(SHARED_OUT_REL, outputs)
        self.assertIn(rel_of(module.META_OUT_FILE), outputs)
        for source in BUILD_HASH_INPUTS:
            self.assertIn(source, inputs)
        for link in module.SHARED_LINK_FILES:
            self.assertIn(link, inputs)

    def test_proto_uses_proto_rel_for_the_input(self):
        module = load("path_report_proto", "gen_proto.py")
        entries = fetch_report(module, "proto")
        self.assertEqual(module.PROTO_REL, "Packages/com.litegame.net/Core/Proto/battle.proto")
        self.assertIn(module.PROTO_REL, paths_of(entries, "input"))
        self.assertIn(
            "Packages/com.litegame.net/Core/Proto/Generated/Battle.cs", paths_of(entries, "output"))

    def test_sync_code_output_constant_is_the_current_path(self):
        module = load("path_report_sync_code", "gen_sync_code.py")
        entries = fetch_report(module, "sync-code")
        expected = "Packages/com.litegame.sim/Diagnostics/Generated/EntitySlot.Sync.g.cs"
        self.assertEqual(rel_of(module.OUT_FILE), expected)
        self.assertIn(expected, paths_of(entries, "output"))
        for _name, source in module.TARGETS:
            self.assertIn(source, paths_of(entries, "input"))

    def test_luban_outputs_cover_publish_targets(self):
        module = load("path_report_luban", "gen_luban.py")
        entries = fetch_report(module, "luban")
        outputs = paths_of(entries, "output")
        for _stage, target_rel, _extension in module.PUBLISH_TARGETS:
            target = target_rel.replace("\\", "/")
            with self.subTest(target=target):
                self.assertTrue(
                    any(covers(target, declared) for declared in outputs),
                    f"luban does not declare publish target {target}: {outputs}")
        self.assertEqual(module.TABLE_LAYOUT_REL.replace("\\", "/"),
                         "Assets/LiteGame/Modules/Config/Table/TableLayout.cs")
        self.assertIn(module.TABLE_LAYOUT_REL.replace("\\", "/"), paths_of(entries, "input"))

    def test_ui_page_inputs_and_output(self):
        module = load("path_report_ui_page", "gen-ui-page.py")
        entries = fetch_report(module, "ui-page")
        for declared in (module.DEFAULT_TABLE, module.DEFAULT_MANIFEST, module.DEFAULT_CONTRACTS):
            self.assertIn(rel_of(declared), paths_of(entries, "input"))
        self.assertIn(rel_of(module.DEFAULT_OUTPUT), paths_of(entries, "output"))

    def test_ui_api_spec_and_outputs(self):
        module = load("path_report_ui_api", "gen-ui-api.py")
        entries = fetch_report(module, "ui-api")
        outputs = paths_of(entries, "output")
        self.assertIn(rel_of(module.SPEC_PATH), paths_of(entries, "input"))
        for output in (module.OUT_INDEX, module.OUT_BASE, module.OUT_MANIFEST,
                       module.OUT_SHIM, module.OUT_DISPATCH):
            self.assertIn(rel_of(output), outputs)

    def test_refresh_hash_and_local_chain_consume_the_current_build_hash_path(self):
        for label, filename, folder in (
                ("refresh-hash", "refresh-hash.py", CODEGEN),
                ("local-chain", "local-chain.py", ROOT / "scripts" / "local")):
            with self.subTest(tool=label):
                module = load(f"path_report_consumer_{label.replace('-', '_')}", filename, folder)
                entries = fetch_report(module, label)
                declared = [entry["path"] for entry in entries]
                self.assertIn(SHARED_OUT_REL, declared)


class SharedSingleSourceHelperTests(unittest.TestCase):
    """_codegen + gen-build-hash single-source helpers (UT-3 / UT-8 building blocks)."""

    @classmethod
    def setUpClass(cls):
        cls.codegen = importlib.import_module("_codegen")

    def test_normalize_eol_collapses_crlf_and_cr(self):
        normalize = getattr(self.codegen, "normalize_eol", None)
        self.assertIsNotNone(normalize, "_codegen.normalize_eol() missing")
        self.assertEqual(normalize("a\r\nb\rc\n"), "a\nb\nc\n")
        self.assertEqual(normalize(b"a\r\nb\rc\n"), b"a\nb\nc\n")

    def test_input_digest_is_order_independent_eol_normalized_and_8_hex(self):
        digest = getattr(self.codegen, "input_digest", None)
        self.assertIsNotNone(digest, "_codegen.input_digest() missing")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "a.txt").write_bytes(b"alpha\r\nbeta\r\n")
            (root / "b.txt").write_bytes(b"gamma\n")

            first = digest(["b.txt", "a.txt"], root)
            second = digest(["a.txt", "b.txt"], root)
            self.assertEqual(first, second, "digest must be path-order independent")
            self.assertRegex(first, r"^[0-9a-f]{8}$")
            self.assertEqual(first, first.lower())

            # LF clone of the same logical content must produce the same digest.
            (root / "a.txt").write_bytes(b"alpha\nbeta\n")
            self.assertEqual(digest(["a.txt", "b.txt"], root), first,
                             "digest must be line-ending normalized")

            (root / "a.txt").write_bytes(b"alpha\nBETA\n")
            self.assertNotEqual(digest(["a.txt", "b.txt"], root), first)

            expected = hashlib.sha256(
                b"a.txt\x00alpha\nbeta\n" b"b.txt\x00gamma\n").hexdigest()[:8]
            self.assertEqual(first, expected,
                             "digest = sha256 over sorted 'rel\\0normalized content'")

    def test_load_module_returns_a_loaded_generator(self):
        loader = getattr(self.codegen, "load_module", None)
        self.assertIsNotNone(loader, "_codegen.load_module() missing")
        module = loader("gen-build-hash")
        self.assertTrue(hasattr(module, "SHARED_TARGETS"))
        self.assertTrue(callable(getattr(module, "shared_out_file", None)))

    def test_shared_out_file_is_the_single_path_source(self):
        module = load("path_report_build_hash_shared", "gen-build-hash.py")
        shared_out_file = getattr(module, "shared_out_file", None)
        self.assertIsNotNone(shared_out_file, "gen-build-hash.shared_out_file() missing")
        self.assertEqual(module.SHARED_OUT_REL, SHARED_OUT_REL)
        self.assertEqual(Path(module.SHARED_OUT_FILE), ROOT / SHARED_OUT_REL)
        with tempfile.TemporaryDirectory() as temp:
            fake_root = Path(temp)
            self.assertEqual(Path(shared_out_file(fake_root)).resolve(),
                             (fake_root / SHARED_OUT_REL).resolve())
        self.assertEqual(Path(shared_out_file(ROOT)).resolve(),
                         (ROOT / SHARED_OUT_REL).resolve())


if __name__ == "__main__":
    unittest.main()
