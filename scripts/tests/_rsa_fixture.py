# -*- coding: utf-8 -*-
"""Temporary RSA key + anchor fixture for content-signing tests."""

import base64
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric import rsa


def _b64(value):
    raw = value.to_bytes((value.bit_length() + 7) // 8, "big")
    return base64.b64encode(raw).decode("ascii")


def make_key_material(directory):
    root = Path(directory)
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    numbers = key.private_numbers()
    public = numbers.public_numbers
    xml = (
        "<RSAKeyValue>"
        f"<Modulus>{_b64(public.n)}</Modulus>"
        f"<Exponent>{_b64(public.e)}</Exponent>"
        f"<P>{_b64(numbers.p)}</P>"
        f"<Q>{_b64(numbers.q)}</Q>"
        f"<DP>{_b64(numbers.dmp1)}</DP>"
        f"<DQ>{_b64(numbers.dmq1)}</DQ>"
        f"<InverseQ>{_b64(numbers.iqmp)}</InverseQ>"
        f"<D>{_b64(numbers.d)}</D>"
        "</RSAKeyValue>"
    )
    key_path = root / "release-key.xml"
    key_path.write_text(xml, encoding="utf-8")
    anchors_path = root / "ContentTrustAnchors.cs"
    anchors_path.write_text(
        "internal static class TestAnchors\n"
        "{\n"
        f'    private static readonly byte[] Key = Convert.FromBase64String("{_b64(public.n)}");\n'
        "}\n",
        encoding="utf-8",
    )
    return key, key_path, anchors_path
