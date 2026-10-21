# -*- coding: utf-8 -*-
"""Serve the local CDN publish root over static HTTP."""

import argparse
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import configure_stdio, fail, info, repo_root  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _local_cdn import load_config  # noqa: E402


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Serve the local CDN publish root")
    parser.add_argument("--config-path", default=None)
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--bind", default="")
    parser.add_argument("--directory", default="")
    return parser.parse_args(argv)


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    config = load_config(args.config_path)
    directory = Path(args.directory) if args.directory else config.directory
    if not directory.is_absolute():
        directory = repo_root() / directory
    directory = directory.resolve()
    bind = args.bind or config.bind
    port = args.port if args.port > 0 else config.port
    if not directory.is_dir():
        return fail("content/serve", f"serve directory missing: {directory} (run scripts/content/publish-candidate.py first)")

    info("content/serve", f"serving: {directory}")
    info("content/serve", f"listen: http://{bind}:{port} (Ctrl+C stops)")
    info("content/serve", f"consume: Test.exe -content.cdnUrl=http://{bind}:{port}")
    result = subprocess.run(
        [sys.executable, "-m", "http.server", str(port), "--directory", str(directory), "--bind", bind]
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
