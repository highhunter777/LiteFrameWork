# -*- coding: utf-8 -*-
"""Contracts for the Python candidate publisher (P2)."""

import base64
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "content" / "publish-candidate.py"
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _rsa_fixture import make_key_material  # noqa: E402


class PublishCandidateTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"publish-candidate script missing: {SCRIPT}")

    def test_layout_and_signed_envelope(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            key, key_path, anchors = make_key_material(root)
            content = root / "content"
            (content / "config").mkdir(parents=True)
            (content / "config" / "a.txt").write_text("a", encoding="utf-8")
            bundle = root / "bundle-src"
            bundle.mkdir()
            (bundle / "pack.dat").write_bytes(b"bundle")
            cdn = root / "cdn"
            cdn.mkdir()
            config = root / "local-cdn.json"
            config.write_text(
                json.dumps({"directory": str(cdn), "bind": "127.0.0.1", "port": 18090}),
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--content-dir",
                    str(content),
                    "--config-path",
                    str(config),
                    "--release-id",
                    "rel-test",
                    "--revision",
                    "2",
                    "--bundle-source",
                    str(bundle),
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
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue((cdn / "candidate.json").is_file())
            self.assertTrue((cdn / "rel-test" / "config" / "a.txt").is_file())
            self.assertTrue((cdn / "rel-test" / "bundle" / "pack.dat").is_file())
            self.assertTrue((content / "bundle" / "pack.dat").is_file())
            envelope = json.loads((cdn / "candidate.json").read_text(encoding="utf-8"))
            manifest = envelope["manifest"]
            canonical = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            key.public_key().verify(
                base64.b64decode(envelope["signature"]),
                canonical,
                padding.PKCS1v15(),
                hashes.SHA256(),
            )

    def test_missing_content_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            key, key_path, anchors = make_key_material(root)
            config = root / "local-cdn.json"
            config.write_text(
                json.dumps({"directory": str(root / "cdn"), "bind": "127.0.0.1", "port": 18090}),
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--content-dir",
                    str(root / "missing"),
                    "--config-path",
                    str(config),
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
                timeout=60,
            )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
