# -*- coding: utf-8 -*-
"""UT-5: ``luban --check`` staging semantics (CRLF, publish compare, stale prune).

Contract pinned by this file (tests-first, implementation task T2):

- ``gen_luban.check_publish(source_dir, target_dir, extension) -> list[str]``
  returns the sorted repo-agnostic relative paths (POSIX) that differ from the
  checked-in target (missing target file, changed content, or a target file the
  staged set no longer produces -> prune-stale candidate). An empty list means
  the publish set is fresh.
- text extensions compare line-ending normalized (LF staging vs CRLF target is
  fresh); ``.bytes`` compares byte-exact.
- ``check_publish`` never writes, copies or deletes anything (``--check`` must
  be read-only; stale pruning is only applied by the generate path).
- ``reorganize_bin_stage`` keeps moving staged ``*.bytes`` into the
  TableLayout domain subdirectories and fails fast on unknown tables.
"""

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CODEGEN = ROOT / "scripts" / "codegen"
sys.path.insert(0, str(CODEGEN))


def load_gen_luban():
    spec = importlib.util.spec_from_file_location("gen_luban_check", CODEGEN / "gen_luban.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(data, bytes):
        path.write_bytes(data)
    else:
        path.write_text(data, encoding="utf-8", newline="")


def check_publish(module, source, target, extension):
    checker = getattr(module, "check_publish", None)
    if checker is None:
        raise AssertionError("gen_luban.check_publish(source_dir, target_dir, extension) missing")
    stale = checker(str(source), str(target), extension)
    return sorted(stale)


class ReorganizeBinStageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.gen_luban = load_gen_luban()

    def test_moves_bytes_into_domain_subdirectories(self):
        with tempfile.TemporaryDirectory() as temp:
            stage = Path(temp)
            write(stage / "tbcombatnum.bytes", b"\x01")
            write(stage / "tbmap.bytes", b"\x02")
            self.gen_luban.reorganize_bin_stage(
                str(stage), {"tbcombatnum": "Combat", "tbmap": "Map"})
            self.assertTrue((stage / "Combat" / "tbcombatnum.bytes").is_file())
            self.assertTrue((stage / "Map" / "tbmap.bytes").is_file())
            self.assertFalse((stage / "tbcombatnum.bytes").exists())
            self.assertFalse((stage / "tbmap.bytes").exists())

    def test_unknown_staged_table_fails_fast(self):
        with tempfile.TemporaryDirectory() as temp:
            stage = Path(temp)
            write(stage / "tbunknown.bytes", b"\x01")
            with self.assertRaises(ValueError):
                self.gen_luban.reorganize_bin_stage(str(stage), {"tbcombatnum": "Combat"})


class CheckPublishTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.gen_luban = load_gen_luban()

    def test_identical_text_is_fresh_regardless_of_line_endings(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "A.cs", "class A {}\n")
            write(target / "A.cs", "class A {}\r\n")
            self.assertEqual(check_publish(self.gen_luban, source, target, ".cs"), [])

    def test_changed_text_is_stale(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "A.cs", "class A { int x; }\n")
            write(target / "A.cs", "class A {}\n")
            self.assertEqual(check_publish(self.gen_luban, source, target, ".cs"), ["A.cs"])

    def test_missing_target_file_is_stale(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "nested" / "A.cs", "class A {}\n")
            target.mkdir()
            self.assertEqual(
                check_publish(self.gen_luban, source, target, ".cs"), ["nested/A.cs"])

    def test_stale_prune_candidate_is_listed_but_kept(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "Combat" / "tbcombatnum.bytes", b"\x01")
            write(target / "Combat" / "tbcombatnum.bytes", b"\x01")
            write(target / "Combat" / "tbretired.bytes", b"\x02")
            before = (target / "Combat" / "tbretired.bytes").read_bytes()

            stale = check_publish(self.gen_luban, source, target, ".bytes")

            self.assertEqual(stale, ["Combat/tbretired.bytes"])
            self.assertTrue((target / "Combat" / "tbretired.bytes").is_file(),
                            "--check must not prune; pruning belongs to the generate path")
            self.assertEqual((target / "Combat" / "tbretired.bytes").read_bytes(), before)

    def test_binary_bytes_compare_is_byte_exact(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "Combat" / "tb.bin", b"")
            write(source / "Combat" / "tbcombatnum.bytes", b"\x01\x02")
            write(target / "Combat" / "tbcombatnum.bytes", b"\x01\x03")
            self.assertEqual(
                check_publish(self.gen_luban, source, target, ".bytes"),
                ["Combat/tbcombatnum.bytes"])

    def test_check_does_not_touch_the_tree(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "stage"
            target = Path(temp) / "published"
            write(source / "A.cs", "class A {}\n")
            write(target / "A.cs", "class A { int y; }\n")
            write(target / "B.cs", "class B {}\n")

            snapshot = {
                path.relative_to(target).as_posix(): path.read_bytes()
                for path in target.rglob("*") if path.is_file()
            }
            check_publish(self.gen_luban, source, target, ".cs")
            after = {
                path.relative_to(target).as_posix(): path.read_bytes()
                for path in target.rglob("*") if path.is_file()
            }
            self.assertEqual(after, snapshot, "--check must be read-only")


if __name__ == "__main__":
    unittest.main()
