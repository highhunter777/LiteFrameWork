# -*- coding: utf-8 -*-
"""Generate a signed content-candidate envelope.

The canonical manifest bytes are written manually to match the PowerShell
implementation and the runtime's Newtonsoft Formatting.None contract.
"""

import argparse
import base64
import hashlib
import re
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

DEFAULT_KEY = Path.home() / ".unitylib-content-signing" / "release-key-2026-09-26.xml"
DEFAULT_ANCHORS = "Assets/LiteFramework/Runtime/Modules/Content/ContentTrustAnchors.cs"


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Generate a signed content-candidate envelope")
    parser.add_argument("--content-dir", required=True)
    parser.add_argument("--release-id", required=True)
    parser.add_argument("--revision", required=True, type=int)
    parser.add_argument("--out-file", default="content/candidate.json")
    parser.add_argument("--expires-in-days", type=int, default=30)
    parser.add_argument("--platform", default="")
    parser.add_argument("--anchors-cs", default=DEFAULT_ANCHORS)
    parser.add_argument("--key-path", default=str(DEFAULT_KEY))
    parser.add_argument("--key-id", default="release-key-2026-09-26")
    return parser.parse_args(argv)


def esc(value):
    if value is None:
        return '""'
    text = str(value)
    text = text.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t")
    return '"' + text + '"'


def collect_entries(content_dir):
    root = Path(content_dir).resolve()
    entries = []
    skipped = 0
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        controlled = (
            relative.startswith("config/")
            or (relative.startswith("lua/") and relative.endswith(".lua"))
            or relative.startswith("bundle/")
        )
        if not controlled:
            skipped += 1
            continue
        data = path.read_bytes()
        entries.append(
            {
                "path": relative,
                "length": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
            }
        )
    entries.sort(key=lambda entry: entry["path"].casefold())
    return entries, skipped


def canonical_manifest(entries, release_id, revision, platform, created, expires, key_id):
    parts = [
        '{"SchemaVersion":1',
        ',"ReleaseId":',
        esc(release_id),
        ',"Revision":',
        str(int(revision)),
        ',"Platform":',
        esc(platform),
        ',"Channel":""',
        ',"Compatibility":{}',
        ',"Dependencies":[]',
        ',"Files":[',
    ]
    for index, entry in enumerate(entries):
        if index:
            parts.append(",")
        parts.extend(
            [
                '{"Path":',
                esc(entry["path"]),
                ',"Length":',
                str(int(entry["length"])),
                ',"Sha256":',
                esc(entry["sha256"]),
                "}",
            ]
        )
    parts.extend(
        [
            "]",
            ',"CreatedAtUnix":',
            str(int(created)),
            ',"ExpiresAtUnix":',
            str(int(expires)),
            ',"KeyId":',
            esc(key_id),
            ',"Signature":""',
            ',"EffectWindow":0',
            ',"Revoked":false',
            ',"Note":""',
            "}",
        ]
    )
    return "".join(parts)


def _b64int(text):
    return int.from_bytes(base64.b64decode(text), "big")


def load_private_key(path):
    root = ET.fromstring(Path(path).read_text(encoding="utf-8"))
    required = ("Modulus", "Exponent", "P", "Q", "DP", "DQ", "InverseQ", "D")
    values = {}
    for name in required:
        node = root.find(name)
        if node is None or not node.text:
            raise ValueError(f"RSA key missing {name}: {path}")
        values[name] = _b64int(node.text)
    from cryptography.hazmat.primitives.asymmetric import rsa

    public = rsa.RSAPublicNumbers(values["Exponent"], values["Modulus"])
    private = rsa.RSAPrivateNumbers(
        values["P"], values["Q"], values["D"], values["DP"], values["DQ"], values["InverseQ"], public
    )
    return private.private_key()


def anchor_modulus(path):
    text = Path(path).read_text(encoding="utf-8")
    match = re.search(r'FromBase64String\("([^"]{100,})"\)', text)
    if not match:
        raise ValueError(f"cannot extract anchor modulus: {path}")
    return _b64int(match.group(1))


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    content = Path(args.content_dir)
    if not content.is_dir():
        return fail("content/gen-candidate", f"candidate directory missing: {content}")
    key_path = Path(args.key_path)
    if not key_path.is_file():
        return fail("content/gen-candidate", f"private key missing: {key_path}")
    anchors = Path(args.anchors_cs)
    if not anchors.is_file():
        return fail("content/gen-candidate", f"anchor table missing: {anchors}")

    entries, skipped = collect_entries(content)
    if not entries:
        return fail(
            "content/gen-candidate",
            f"no controlled content in {content} (need config/**, lua/**.lua or bundle/**); skipped {skipped}",
        )

    now = int(time.time())
    expires = now + int(args.expires_in_days) * 86400
    canonical = canonical_manifest(entries, args.release_id, args.revision, args.platform, now, expires, args.key_id)
    canonical_bytes = canonical.encode("utf-8")

    try:
        from cryptography.hazmat.primitives import hashes
        from cryptography.hazmat.primitives.asymmetric import padding, rsa

        private_key = load_private_key(key_path)
        signature = private_key.sign(canonical_bytes, padding.PKCS1v15(), hashes.SHA256())
        public_key = rsa.RSAPublicNumbers(65537, anchor_modulus(anchors)).public_key()
        public_key.verify(signature, canonical_bytes, padding.PKCS1v15(), hashes.SHA256())
    except Exception as exc:
        return fail("content/gen-candidate", f"signing/self-check failed: {exc}")

    envelope = '{"manifest":' + canonical + ',"signature":"' + base64.b64encode(signature).decode("ascii") + '"}'
    out = Path(args.out_file)
    if not out.is_absolute():
        out = Path.cwd() / out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(envelope.encode("utf-8"))

    info("content/gen-candidate", f"candidate entries: {len(entries)} (skipped {skipped})")
    info("content/gen-candidate", f"envelope: {out}")
    info("content/gen-candidate", f"PASS: signed and verified against anchor {args.key_id}")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
