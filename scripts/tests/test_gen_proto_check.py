# -*- coding: utf-8 -*-
"""UT-6: ``proto --check`` staleness semantics.

Contract pinned by this file (tests-first, implementation task T2):

- ``gen_proto.SIDECAR_REL`` == "Assets/LiteNet/Core/Proto/Generated/Battle.cs.input";
- sidecar payload is one line ``battle.proto sha256:<8 hex>`` where the digest is
  the line-ending-normalized input digest of ``PROTO_REL`` (same algorithm as
  ``_codegen.input_digest``: sha256 over ``"<rel>\\0<normalized content>"``,
  first 8 hex chars);
- ``gen_proto.check(root, protoc="")`` returns 0 when up to date and 1 when
  stale; with an empty ``protoc`` it runs the stamp mode:
  * Battle.cs missing            -> 1
  * sidecar missing              -> 1
  * sidecar digest mismatch      -> 1
  * .proto CRLF vs sidecar on LF -> 0 (line-ending normalized)
  * check never writes the repo.
"""

import hashlib
import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CODEGEN = ROOT / "scripts" / "codegen"
sys.path.insert(0, str(CODEGEN))

PROTO_REL = "Assets/LiteNet/Core/Proto/battle.proto"
BATTLE_REL = "Assets/LiteNet/Core/Proto/Generated/Battle.cs"
SIDECAR_REL = "Assets/LiteNet/Core/Proto/Generated/Battle.cs.input"


def load_gen_proto():
    spec = importlib.util.spec_from_file_location("gen_proto_check", CODEGEN / "gen_proto.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest_of(root):
    raw = (Path(root) / PROTO_REL).read_bytes()
    normalized = raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    payload = PROTO_REL.encode("utf-8") + b"\x00" + normalized
    return hashlib.sha256(payload).hexdigest()[:8]


def stamp_of(root):
    return f"battle.proto sha256:{digest_of(root)}"


def make_tree(temp, proto_bytes=b"syntax = \"proto3\";\n", battle=True, stamp=True):
    root = Path(temp)
    proto = root / PROTO_REL
    proto.parent.mkdir(parents=True, exist_ok=True)
    proto.write_bytes(proto_bytes)
    generated = root / BATTLE_REL
    generated.parent.mkdir(parents=True, exist_ok=True)
    if battle:
        generated.write_text("// protoc output\n", encoding="utf-8")
    if stamp:
        (root / SIDECAR_REL).write_text(stamp_of(root) + "\n", encoding="utf-8")
    return root


class ProtoStampCheckTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.gen_proto = load_gen_proto()

    def check(self, root):
        checker = getattr(self.gen_proto, "check", None)
        self.assertIsNotNone(checker, "gen_proto.check(root, protoc='') missing")
        return checker(root)

    def test_sidecar_constant_is_the_generated_directory_stamp(self):
        self.assertEqual(getattr(self.gen_proto, "SIDECAR_REL", None), SIDECAR_REL)

    def test_fresh_stamp_passes(self):
        with tempfile.TemporaryDirectory() as temp:
            self.assertEqual(self.check(make_tree(temp)), 0)

    def test_crlf_proto_matches_lf_stamp(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_tree(temp, proto_bytes=b"syntax = \"proto3\";\n")
            stamp = stamp_of(root)  # LF digest
            (root / PROTO_REL).write_bytes(b"syntax = \"proto3\";\r\n")
            (root / SIDECAR_REL).write_text(stamp + "\n", encoding="utf-8")
            self.assertEqual(self.check(root), 0,
                             "CRLF workspace must not fake a stale proto")

    def test_missing_sidecar_is_stale(self):
        with tempfile.TemporaryDirectory() as temp:
            self.assertEqual(self.check(make_tree(temp, stamp=False)), 1)

    def test_mismatched_sidecar_is_stale(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_tree(temp)
            (root / SIDECAR_REL).write_text("battle.proto sha256:00000000\n", encoding="utf-8")
            self.assertEqual(self.check(root), 1)

    def test_changed_proto_is_stale(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_tree(temp)
            (root / PROTO_REL).write_bytes(b"syntax = \"proto3\";\n// changed\n")
            self.assertEqual(self.check(root), 1)

    def test_missing_battle_cs_is_stale_even_with_stamp(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_tree(temp, battle=False)
            self.assertEqual(self.check(root), 1)

    def test_check_does_not_write_the_tree(self):
        with tempfile.TemporaryDirectory() as temp:
            root = make_tree(temp)
            before = {
                PROTO_REL: (root / PROTO_REL).read_bytes(),
                BATTLE_REL: (root / BATTLE_REL).read_bytes(),
                SIDECAR_REL: (root / SIDECAR_REL).read_bytes(),
            }
            self.check(root)
            for rel, data in before.items():
                self.assertEqual((root / rel).read_bytes(), data, f"{rel} was rewritten by --check")


class ProtoStampRepoTests(unittest.TestCase):
    """IT-2: the committed sidecar must exist and match the live .proto."""

    def test_repo_sidecar_exists_and_matches_the_proto(self):
        sidecar = ROOT / SIDECAR_REL
        self.assertTrue(sidecar.is_file(),
                        f"sidecar missing: {SIDECAR_REL} (run toolbox.py proto --protoc once)")
        text = sidecar.read_text(encoding="utf-8").strip()
        self.assertRegex(text, r"^battle\.proto sha256:[0-9a-f]{8}$")
        self.assertEqual(text, stamp_of(ROOT))

    def test_toolbox_proto_check_is_green(self):
        result = subprocess.run(
            [sys.executable, str(CODEGEN / "toolbox.py"), "proto", "--check"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
