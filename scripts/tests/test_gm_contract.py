# -*- coding: utf-8 -*-
"""GM HTTP contract: single source, server/editor/CLI consistency, body building."""

import importlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
sys.path.insert(0, str(ROOT / "scripts" / "deploy"))

from _gm_contract import ROUTE_RE, SERVER_SOURCE, action_spec, actions, check_consistency, load  # noqa: E402

gm = importlib.import_module("gm")


class ContractSourceTests(unittest.TestCase):
    def test_contract_matches_server_editor_and_cli(self):
        self.assertEqual(check_consistency(ROOT), [])

    def test_contract_declares_every_server_route(self):
        contract = load(ROOT)
        declared = set(actions(contract))
        server = set(ROUTE_RE.findall((ROOT / SERVER_SOURCE).read_text(encoding="utf-8")))
        self.assertEqual(declared, server)

    def test_actions_have_path_and_kind(self):
        for name, spec in actions(load(ROOT)).items():
            self.assertTrue(spec.get("path", "").startswith("/gm/"), name)
            self.assertIn(spec.get("kind"), {"read", "write"}, name)

    def test_manifest_action_enum_matches_contract(self):
        manifest = json.loads((ROOT / "scripts" / "gate" / "toolchain.json").read_text(encoding="utf-8"))
        tool = next(item for item in manifest["tools"] if item["id"] == "deploy.gm")
        values = next(arg["values"] for arg in tool["args"] if arg["name"] == "action")
        self.assertEqual(set(values), set(actions(load(ROOT))))

    def test_drift_is_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            contract = load(ROOT)
            contract["actions"].append({"name": "ghost", "path": "/gm/ghost", "kind": "write",
                                        "queued": True, "request": []})
            target = root / "Server" / "RoomServer" / "gm-contract.json"
            target.parent.mkdir(parents=True)
            target.write_text(json.dumps(contract), encoding="utf-8")
            server = root / SERVER_SOURCE
            server.write_text('case "/gm/rooms": break;\n', encoding="utf-8")
            violations = check_consistency(root)
        self.assertTrue(any("ghost" in item for item in violations), violations)


class RequestBuilderTests(unittest.TestCase):
    def setUp(self):
        self.contract = load(ROOT)

    def test_rules_flags_from_booleans(self):
        path, body = gm.build_request(self.contract, "rules", no_death=True)
        self.assertEqual(path, "/gm/rules")
        self.assertEqual(body, {"flags": 1})
        _, body = gm.build_request(self.contract, "rules", no_death=True, infinite_ammo=True)
        self.assertEqual(body, {"flags": 3})

    def test_rules_keeps_room_when_given(self):
        _, body = gm.build_request(self.contract, "rules", room="r1", flags=2)
        self.assertEqual(body, {"room": "r1", "flags": 2})

    def test_kick_requires_player(self):
        with self.assertRaises(ValueError) as ctx:
            gm.build_request(self.contract, "kick", room="r1")
        self.assertIn("--player", str(ctx.exception))

    def test_diag_fetch_requires_file(self):
        with self.assertRaises(ValueError) as ctx:
            gm.build_request(self.contract, "diag-fetch")
        self.assertIn("--file", str(ctx.exception))
        path, body = gm.build_request(self.contract, "diag-fetch", file_name="a.json.gz")
        self.assertEqual(path, "/gm/diag-fetch")
        self.assertEqual(body, {"file": "a.json.gz"})

    def test_settle_flush_has_empty_body(self):
        path, body = gm.build_request(self.contract, "settle-flush")
        self.assertEqual(path, "/gm/settle-flush")
        self.assertEqual(body, {})

    def test_unknown_action(self):
        with self.assertRaises(ValueError):
            action_spec(self.contract, "bogus")


if __name__ == "__main__":
    unittest.main()
