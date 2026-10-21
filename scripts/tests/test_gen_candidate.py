# -*- coding: utf-8 -*-
"""Contracts for the Python signed-candidate generator (P2)."""

import base64
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "content" / "gen-candidate.py"
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _rsa_fixture import make_key_material  # noqa: E402


class GenCandidateTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"gen-candidate script missing: {SCRIPT}")

    def make_content(self, root):
        root = Path(root)
        (root / "config").mkdir(parents=True)
        (root / "lua").mkdir()
        (root / "bundle").mkdir()
        (root / "config" / "a.bytes").write_bytes(b"config-bytes")
        (root / "lua" / "x.lua").write_text("return 1\n", encoding="utf-8")
        (root / "lua" / "skip.txt").write_text("not lua\n", encoding="utf-8")
        (root / "bundle" / "pack.dat").write_bytes(b"bundle-bytes")
        (root / "skip.txt").write_text("skipped\n", encoding="utf-8")
        return root

    def test_envelope_is_canonical_and_verifiable(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            key, key_path, anchors = make_key_material(root)
            content = self.make_content(root / "content")
            out = root / "candidate.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--content-dir",
                    str(content),
                    "--release-id",
                    "rel-test",
                    "--revision",
                    "3",
                    "--out-file",
                    str(out),
                    "--key-path",
                    str(key_path),
                    "--anchors-cs",
                    str(anchors),
                    "--key-id",
                    "test-key",
                ],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=120,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            envelope = json.loads(out.read_text(encoding="utf-8"))
            manifest = envelope["manifest"]
            self.assertEqual(
                list(manifest.keys()),
                [
                    "SchemaVersion",
                    "ReleaseId",
                    "Revision",
                    "Platform",
                    "Channel",
                    "Compatibility",
                    "Dependencies",
                    "Files",
                    "CreatedAtUnix",
                    "ExpiresAtUnix",
                    "KeyId",
                    "Signature",
                    "EffectWindow",
                    "Revoked",
                    "Note",
                ],
            )
            self.assertEqual([entry["Path"] for entry in manifest["Files"]], ["bundle/pack.dat", "config/a.bytes", "lua/x.lua"])
            self.assertEqual(manifest["Files"][0]["Sha256"], hashlib.sha256(b"bundle-bytes").hexdigest())
            self.assertEqual(manifest["Signature"], "")
            canonical = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            key.public_key().verify(
                base64.b64decode(envelope["signature"]),
                canonical,
                padding.PKCS1v15(),
                hashes.SHA256(),
            )

    def test_empty_content_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            key, key_path, anchors = make_key_material(root)
            content = root / "content"
            content.mkdir()
            result = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--content-dir",
                    str(content),
                    "--release-id",
                    "rel-test",
                    "--revision",
                    "1",
                    "--out-file",
                    str(root / "candidate.json"),
                    "--key-path",
                    str(key_path),
                    "--anchors-cs",
                    str(anchors),
                ],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=120,
            )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("no controlled content", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
