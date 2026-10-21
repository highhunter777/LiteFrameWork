# -*- coding: utf-8 -*-
"""Lay out a candidate into the CDN layout and produce the signed envelope."""

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _local_cdn import load_config  # noqa: E402

DEFAULT_ANCHORS = "Assets/LiteClient/Runtime/Resource/ContentTrustAnchors.cs"


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Publish a content candidate into the local/CDN layout")
    parser.add_argument("--content-dir", required=True)
    parser.add_argument("--cdn-root", default="")
    parser.add_argument("--release-id", default="rel-local-001")
    parser.add_argument("--revision", type=int, default=1)
    parser.add_argument("--expires-in-days", type=int, default=30)
    parser.add_argument("--bundle-source", default="")
    parser.add_argument("--config-path", default=None)
    parser.add_argument("--key-path", default=None)
    parser.add_argument("--anchors-cs", default=None)
    parser.add_argument("--key-id", default=None)
    return parser.parse_args(argv)


def copy_tree_contents(source, destination):
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination)


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    content = Path(args.content_dir).resolve()
    if not content.is_dir():
        return fail("content/publish-candidate", f"candidate dir missing: {content}")

    if args.bundle_source:
        bundle_source = Path(args.bundle_source).resolve()
        if not bundle_source.is_dir():
            return fail("content/publish-candidate", f"bundle source missing: {bundle_source}")
        bundle_dst = content / "bundle"
        copy_tree_contents(bundle_source, bundle_dst)
        info("content/publish-candidate", f"bundle segment mirrored: {bundle_dst}")

    config = load_config(args.config_path)
    cdn_root = Path(args.cdn_root) if args.cdn_root else config.directory
    if not cdn_root.is_absolute():
        cdn_root = repo_root() / cdn_root
    cdn_root = cdn_root.resolve()

    release_dir = cdn_root / args.release_id
    copy_tree_contents(content, release_dir)
    info("content/publish-candidate", f"files laid out: {release_dir}")

    gen = Path(__file__).resolve().with_name("gen-candidate.py")
    command = [
        sys.executable,
        str(gen),
        "--content-dir",
        str(content),
        "--release-id",
        args.release_id,
        "--revision",
        str(args.revision),
        "--expires-in-days",
        str(args.expires_in_days),
        "--out-file",
        str(cdn_root / "candidate.json"),
        "--anchors-cs",
        str(Path(args.anchors_cs).resolve()) if args.anchors_cs else str(repo_root() / DEFAULT_ANCHORS),
    ]
    if args.key_path:
        command.extend(["--key-path", str(Path(args.key_path).resolve())])
    if args.key_id:
        command.extend(["--key-id", args.key_id])

    result = subprocess.run(command, cwd=str(repo_root()))
    if result.returncode != 0:
        return fail("content/publish-candidate", "gen-candidate failed (see above) - envelope not produced")

    print("")
    info("content/publish-candidate", f"publish done: {cdn_root}")
    info("content/publish-candidate", f"serve: python scripts/content/serve-cdn.py (config: {config.config_path})")
    info("content/publish-candidate", f"consume: Test.exe -content.cdnUrl=http://{config.bind}:{config.port}")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
