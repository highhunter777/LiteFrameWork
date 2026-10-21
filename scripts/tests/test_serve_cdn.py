# -*- coding: utf-8 -*-
"""Contracts for the Python local CDN server (P2)."""

import json
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "content" / "serve-cdn.py"


def free_port():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class ServeCdnTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"serve-cdn script missing: {SCRIPT}")

    def test_serves_directory(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            payload = root / "hello.txt"
            payload.write_text("hello-cdn", encoding="utf-8")
            config = root / "local-cdn.json"
            port = free_port()
            config.write_text(
                json.dumps({"directory": str(root), "bind": "127.0.0.1", "port": port}),
                encoding="utf-8",
            )
            process = subprocess.Popen(
                [sys.executable, str(SCRIPT), "--config-path", str(config)],
                cwd=str(ROOT),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            try:
                body = None
                last_error = None
                for _ in range(50):
                    try:
                        with urllib.request.urlopen(f"http://127.0.0.1:{port}/hello.txt", timeout=1) as response:
                            body = response.read().decode("utf-8")
                        break
                    except Exception as exc:  # server may not be ready yet
                        last_error = exc
                        time.sleep(0.1)
                self.assertEqual(body, "hello-cdn", f"last error: {last_error}")
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                if process.stdout:
                    process.stdout.close()
                if process.stderr:
                    process.stderr.close()

    def test_missing_directory_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            config = root / "local-cdn.json"
            config.write_text(
                json.dumps({"directory": str(root / "missing"), "bind": "127.0.0.1", "port": free_port()}),
                encoding="utf-8",
            )
            result = subprocess.run(
                [sys.executable, str(SCRIPT), "--config-path", str(config)],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=30,
            )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("serve directory missing", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
