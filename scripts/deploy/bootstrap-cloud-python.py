# -*- coding: utf-8 -*-
"""Install the embedded Python runtime used by the cloud remote runner."""

import argparse
import hashlib
import re
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, warn  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _ssh_host import resolve_ssh_host  # noqa: E402
from _remote import ensure_remote_ready, probe_remote_python  # noqa: E402

PYTHON_VERSION = "3.12.9"
PYTHON_URL = f"https://www.python.org/ftp/python/{PYTHON_VERSION}/python-{PYTHON_VERSION}-embed-amd64.zip"
PYTHON_SHA256 = "615861fb801e8b04c847598db4e1e46e4b046295017caa37cb5486dde72b5865"
REMOTE_DIR = "C:/litegame/python"
REMOTE_PYTHON = f"{REMOTE_DIR}/python.exe"
REMOTE_ZIP = "C:/litegame/python-embed.zip"


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Install the cloud-host embedded Python runtime")
    parser.add_argument("--ssh-host", default="")
    parser.add_argument("--python-url", default=PYTHON_URL)
    parser.add_argument("--sha256", default="")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", "--what-if", dest="dry_run", action="store_true",
                        help="print the plan without downloading/uploading (alias: --what-if)")
    return parser.parse_args(argv)


def _run(command):
    return subprocess.run(
        command,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def ssh_run(host, command):
    return _run(["ssh", "-o", "BatchMode=yes", host, command])


def remote_version(host):
    result = probe_remote_python(host, REMOTE_PYTHON)
    text = (result.stdout or "") + (result.stderr or "")
    match = re.search(r"Python (\d+)\.(\d+)", text)
    if result.returncode != 0 or not match:
        return None
    return int(match.group(1)), int(match.group(2))


def download(url, destination, expected_sha256):
    try:
        urllib.request.urlretrieve(url, destination)
    except Exception as exc:  # noqa: BLE001 - surface the network error as a tool failure
        raise RuntimeError(f"download failed: {url}: {exc}")
    digest = hashlib.sha256(Path(destination).read_bytes()).hexdigest()
    if expected_sha256 and digest.lower() != expected_sha256.lower():
        raise RuntimeError(f"sha256 mismatch: got {digest}, expected {expected_sha256}")
    return digest


def install_remote(host, archive):
    remote_zip = REMOTE_ZIP
    upload = _run(["scp", "-q", str(archive), f"{host}:{remote_zip}"])
    if upload.returncode != 0:
        raise RuntimeError(f"scp embedded python failed: {((upload.stdout or '') + (upload.stderr or '')).strip()[:200]}")
    mkdir = ssh_run(host, r"cmd /c if not exist C:\litegame\python mkdir C:\litegame\python")
    if mkdir.returncode != 0:
        raise RuntimeError(f"remote mkdir failed: {((mkdir.stdout or '') + (mkdir.stderr or '')).strip()[:200]}")
    extract = ssh_run(host, rf"C:\Windows\System32\tar.exe -xf {remote_zip.replace('/', chr(92))} -C C:\litegame\python")
    if extract.returncode != 0:
        raise RuntimeError(f"remote unzip failed: {((extract.stdout or '') + (extract.stderr or '')).strip()[:200]}")
    ssh_run(host, r"cmd /c del C:\litegame\python-embed.zip")


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    try:
        host = args.ssh_host.strip() or resolve_ssh_host()
    except RuntimeError as exc:
        return fail("deploy/cloud-python", str(exc))
    current = remote_version(host)
    if current and current >= (3, 10) and not args.force:
        info("deploy/cloud-python", f"cloud Python already present: {current[0]}.{current[1]} ({REMOTE_PYTHON})")
        if not args.dry_run:
            ensure_remote_ready(host)
        return EXIT_OK
    if args.dry_run:
        info("deploy/cloud-python", f"would upload {args.python_url} and install to {REMOTE_DIR}")
        return EXIT_OK

    expected = args.sha256 or (PYTHON_SHA256 if args.python_url == PYTHON_URL else "")
    if not expected:
        warn("deploy/cloud-python", "no sha256 for custom URL; integrity check skipped")
    try:
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / "python-embed.zip"
            info("deploy/cloud-python", f"download {args.python_url}")
            digest = download(args.python_url, archive, expected)
            info("deploy/cloud-python", f"sha256 {digest}")
            install_remote(host, archive)
        installed = remote_version(host)
        if not installed or installed < (3, 10):
            raise RuntimeError(f"remote Python verification failed: {installed}")
        info("deploy/cloud-python", f"cloud Python ready: {installed[0]}.{installed[1]} ({REMOTE_PYTHON})")
        ensure_remote_ready(host)
        return EXIT_OK
    except RuntimeError as exc:
        return fail("deploy/cloud-python", str(exc))


if __name__ == "__main__":
    sys.exit(main())
