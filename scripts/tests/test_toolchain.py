# -*- coding: utf-8 -*-
"""Toolchain platform contract tests (manifest / CLI / generated docs / surfaces).

Red-first intent: these assertions describe the target contract before the
manifest v2 and scripts/toolchain.py exist. Run with:
  python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
"""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "scripts" / "toolchain.py"
MANIFEST = ROOT / "scripts" / "gate" / "toolchain.json"
DOC_INDEX = ROOT / "Docs" / "工具链" / "工具索引.md"


def run_cli(*args, timeout=60):
    return subprocess.run(
        [sys.executable, str(CLI), *args],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


class ManifestContractTests(unittest.TestCase):
    def load(self):
        self.assertTrue(MANIFEST.is_file(), f"manifest missing: {MANIFEST}")
        return json.loads(MANIFEST.read_text(encoding="utf-8"))

    def load_v2(self):
        manifest = self.load()
        for field in ("schemaVersion", "categories", "tools", "surfaces"):
            self.assertIn(field, manifest, f"manifest v2 field missing: {field}")
        return manifest

    def test_manifest_has_v3_fields(self):
        manifest = self.load_v2()
        self.assertEqual(manifest.get("schemaVersion"), 3)
        self.assertIsInstance(manifest.get("categories"), list)
        self.assertIsInstance(manifest.get("tools"), list)
        self.assertIsInstance(manifest.get("surfaces"), list)
        self.assertGreater(len(manifest["categories"]), 0)
        self.assertGreater(len(manifest["tools"]), 0)

    def test_tool_ids_and_handlers_are_unique(self):
        manifest = self.load_v2()
        tools = manifest["tools"]
        ids = [tool.get("id") for tool in tools]
        self.assertEqual(len(ids), len(set(ids)), f"duplicate tool ids: {ids}")
        handlers = [tool["handler"] for tool in tools if tool.get("handler")]
        self.assertEqual(len(handlers), len(set(handlers)), f"duplicate handlers: {handlers}")
        menus = [tool["menu"] for tool in tools if tool.get("menu")]
        self.assertEqual(len(menus), len(set(menus)), f"duplicate menus: {menus}")

    def test_categories_are_declared(self):
        manifest = self.load_v2()
        category_ids = {category.get("id") for category in manifest["categories"]}
        for tool in manifest["tools"]:
            self.assertIn(tool.get("category"), category_ids, f"undeclared category on {tool.get('id')}")

    def test_kind_contract(self):
        allowed = {"page", "window", "action", "cli", "gate", "build", "content", "docs"}
        manifest = self.load_v2()
        for tool in manifest["tools"]:
            kind = tool.get("kind")
            self.assertIn(kind, allowed, f"bad kind on {tool.get('id')}: {kind}")
            self.assertTrue(tool.get("title"), f"title missing on {tool.get('id')}")
            self.assertTrue(tool.get("summary"), f"summary missing on {tool.get('id')}")
            self.assertTrue(tool.get("docs"), f"docs missing on {tool.get('id')}")
            if kind in {"page", "window", "action"}:
                self.assertTrue(tool.get("handler"), f"handler missing on {tool.get('id')}")
            if kind in {"cli", "gate", "build", "content"}:
                self.assertTrue(tool.get("command"), f"command missing on {tool.get('id')}")

    def test_docs_and_command_scripts_exist(self):
        manifest = self.load_v2()
        for tool in manifest["tools"]:
            docs = ROOT / tool["docs"]
            self.assertTrue(docs.is_file(), f"docs path missing on {tool.get('id')}: {tool['docs']}")
            for token in tool.get("command") or []:
                if not isinstance(token, str):
                    continue
                if token.startswith(("scripts/", "Server/", "Assets/", "Tests/")):
                    self.assertTrue((ROOT / token).exists(), f"command path missing on {tool.get('id')}: {token}")

    def test_migrated_actions_declare_outputs_and_safety(self):
        manifest = self.load_v2()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        for tool_id in ("baking.body", "baking.muzzle", "baking.grip", "baking.root-motion", "baking.map", "codegen.sim-trig"):
            self.assertIn(tool_id, by_id)
            tool = by_id[tool_id]
            self.assertEqual(tool.get("kind"), "action")
            self.assertTrue(tool.get("outputs"), f"{tool_id} outputs missing")
            self.assertEqual(tool.get("safety", {}).get("dryRun"), "supported", tool_id)
            self.assertEqual(tool.get("safety", {}).get("undo"), "file", tool_id)

    def test_required_surfaces_declared(self):
        manifest = self.load_v2()
        surfaces = {surface.get("id"): surface for surface in manifest["surfaces"]}
        self.assertIn("ci", surfaces)
        self.assertIn("nightly-task", surfaces)
        self.assertIn("zcode-hook", surfaces)
        self.assertIn("claude-hook", surfaces)
        self.assertEqual(surfaces["nightly-task"].get("script"), "scripts/gate/nightly-gate.py")
        self.assertEqual(surfaces["nightly-task"].get("runner"), "python")


    def test_l0_entry_is_python(self):
        manifest = self.load()
        l0 = next(tool for tool in manifest["tools"] if tool["id"] == "quality.l0")
        self.assertEqual(l0["command"], ["python", "scripts/gate/l0-dep-scan.py"])
        self.assertTrue((ROOT / "scripts" / "gate" / "l0-dep-scan.py").is_file())
        self.assertFalse((ROOT / "scripts" / "gate" / "l0-dep-scan.ps1").exists())
        toolchain_text = (ROOT / "scripts" / "toolchain.py").read_text(encoding="utf-8")
        self.assertIn("l0-dep-scan.py", toolchain_text)
        self.assertNotIn("l0-dep-scan.ps1", toolchain_text)
        ci_text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        self.assertIn("l0-dep-scan.py", ci_text)
        self.assertNotIn("l0-dep-scan.ps1", ci_text)
        build_text = (ROOT / "scripts" / "build" / "build-player.py").read_text(encoding="utf-8")
        self.assertIn("l0-dep-scan.py", build_text)
        self.assertNotIn("l0-dep-scan.ps1", build_text)

    def test_p5_entries_are_python(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        expected = {
            "deploy.room": "scripts/deploy/deploy-room.py",
            "deploy.gm": "scripts/deploy/gm.py",
            "deploy.wg-peer": "scripts/deploy/wg-peer.py",
            "env.local-chain": "scripts/local/local-chain.py",
        }
        for tool_id, script in expected.items():
            self.assertEqual(by_id[tool_id]["command"], ["python", script])
            self.assertTrue((ROOT / script).is_file(), f"{tool_id} script missing")
        for old in (
            "scripts/deploy/deploy-room.ps1",
            "scripts/deploy/gm.ps1",
            "scripts/deploy/wg-peer.ps1",
            "scripts/deploy/_ssh-host.ps1",
            "scripts/local/local-chain.ps1",
        ):
            self.assertFalse((ROOT / old).exists(), f"legacy script still present: {old}")
        self.assertEqual(list((ROOT / "scripts").rglob("*.ps1")), [])

    def test_p4_entries_are_python(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        self.assertEqual(by_id["build.player"]["command"], ["python", "scripts/build/build-player.py"])
        self.assertEqual(by_id["build.smoke"]["command"], ["python", "scripts/build/player-smoke.py"])
        self.assertTrue((ROOT / "scripts" / "build" / "build-player.py").is_file())
        self.assertTrue((ROOT / "scripts" / "build" / "player-smoke.py").is_file())
        self.assertFalse((ROOT / "scripts" / "build" / "build-player.ps1").exists())
        self.assertFalse((ROOT / "scripts" / "build" / "player-smoke.ps1").exists())
        ci_text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        self.assertIn("build-player.py", ci_text)
        self.assertIn("player-smoke.py", ci_text)
        self.assertNotIn("build-player.ps1", ci_text)
        self.assertNotIn("player-smoke.ps1", ci_text)

    def test_p3b_entries_are_python(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        self.assertEqual(by_id["quality.l2"]["command"], ["python", "scripts/gate/l2-unity-gate.py"])
        self.assertEqual(by_id["env.nightly"]["command"], ["python", "scripts/gate/nightly-gate.py"])
        self.assertEqual(
            by_id["env.nightly-install"]["command"],
            ["python", "scripts/gate/install-nightly-task.py", "--force"],
        )
        self.assertTrue((ROOT / "scripts" / "gate" / "l2-unity-gate.py").is_file())
        self.assertTrue((ROOT / "scripts" / "gate" / "nightly-gate.py").is_file())
        self.assertTrue((ROOT / "scripts" / "gate" / "install-nightly-task.py").is_file())
        self.assertFalse((ROOT / "scripts" / "gate" / "l2-unity-gate.ps1").exists())
        self.assertFalse((ROOT / "scripts" / "gate" / "install-nightly-task.ps1").exists())
        surface = next(item for item in manifest["surfaces"] if item["id"] == "nightly-task")
        self.assertEqual(surface["runner"], "python")
        self.assertEqual(surface["script"], "scripts/gate/nightly-gate.py")
        ci_text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        self.assertIn("l2-unity-gate.py", ci_text)
        self.assertNotIn("l2-unity-gate.ps1", ci_text)
        test_text = (ROOT / "scripts" / "gate" / "test.py").read_text(encoding="utf-8")
        self.assertIn("l2-unity-gate.py", test_text)

    def test_test_entry_is_python(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        self.assertEqual(
            by_id["quality.l1"]["command"],
            ["python", "scripts/gate/test.py", "--lane", "L1", "--profile", "PullRequest"],
        )
        self.assertEqual(
            by_id["quality.l3"]["command"],
            ["python", "scripts/gate/test.py", "--lane", "L3", "--profile", "PullRequest"],
        )
        self.assertEqual(
            by_id["quality.doc"]["command"],
            ["python", "scripts/gate/test.py", "--lane", "Doc"],
        )
        self.assertTrue((ROOT / "scripts" / "gate" / "test.py").is_file())
        self.assertFalse((ROOT / "scripts" / "gate" / "test.ps1").exists())
        ci_text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        self.assertIn("scripts/gate/test.py", ci_text)
        self.assertNotIn("scripts/gate/test.ps1", ci_text)
        toolchain_text = (ROOT / "scripts" / "toolchain.py").read_text(encoding="utf-8")
        self.assertIn("test.py", toolchain_text)
        self.assertNotIn("test.ps1", toolchain_text)
        nightly_text = (ROOT / "scripts" / "gate" / "nightly-gate.py").read_text(encoding="utf-8")
        self.assertIn("test.py", nightly_text)
        self.assertNotIn("test.ps1", nightly_text)
        self.assertFalse((ROOT / "scripts" / "gate" / "nightly-gate.ps1").exists())

    def test_p2_entries_are_python(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        expected = {
            "content.gen-candidate": "scripts/content/gen-candidate.py",
            "content.publish-candidate": "scripts/content/publish-candidate.py",
            "content.serve-cdn": "scripts/content/serve-cdn.py",
            "env.restore": "scripts/gate/restore-check.py",
        }
        for tool_id, script in expected.items():
            self.assertEqual(by_id[tool_id]["command"], ["python", script])
            self.assertTrue((ROOT / script).is_file(), f"{tool_id} script missing")
        for old in (
            "scripts/content/gen-candidate.ps1",
            "scripts/content/publish-candidate.ps1",
            "scripts/content/serve-cdn.ps1",
            "scripts/gate/restore-check.ps1",
            "scripts/content/_local-cdn.ps1",
        ):
            self.assertFalse((ROOT / old).exists(), f"legacy script still present: {old}")
        toolchain_text = (ROOT / "scripts" / "toolchain.py").read_text(encoding="utf-8")
        self.assertIn("restore-check.py", toolchain_text)
        self.assertNotIn("restore-check.ps1", toolchain_text)
        build_text = (ROOT / "scripts" / "build" / "build-player.py").read_text(encoding="utf-8")
        self.assertIn("restore-check.py", build_text)
        self.assertNotIn("restore-check.ps1", build_text)

    def test_hook_entries_are_python(self):
        zcode = json.loads((ROOT / ".zcode" / "config.json").read_text(encoding="utf-8"))
        zcode_text = json.dumps(zcode, ensure_ascii=False)
        self.assertIn("shared-source-reminder.py", zcode_text)
        self.assertNotIn("shared-source-reminder.ps1", zcode_text)
        claude_text = (ROOT / ".claude" / "settings.json").read_text(encoding="utf-8")
        self.assertIn("shared-source-reminder.py", claude_text)
        self.assertNotIn("shared-source-reminder.ps1", claude_text)
        self.assertTrue((ROOT / "scripts" / "hooks" / "shared-source-reminder.py").is_file())
        self.assertFalse((ROOT / "scripts" / "hooks" / "shared-source-reminder.ps1").exists())


class CliContractTests(unittest.TestCase):
    def test_help_lists_verbs(self):
        result = run_cli("--help")
        self.assertEqual(result.returncode, 0, result.stderr)
        for verb in ("list", "check", "docs", "open", "run", "verify", "build", "doctor"):
            self.assertIn(verb, result.stdout)

    def test_list_json(self):
        result = run_cli("list", "--json")
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(result.stdout)
        self.assertIn("categories", payload)
        self.assertIn("tools", payload)
        ids = {tool["id"] for tool in payload["tools"]}
        self.assertIn("baking.toolbox", ids)
        self.assertIn("codegen.build-hash", ids)

    def test_unknown_tool_is_nonzero(self):
        result = run_cli("open", "no.such.tool")
        self.assertNotEqual(result.returncode, 0)

    def test_check_passes_on_repo_manifest(self):
        result = run_cli("check")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_check_catches_missing_command_path(self):
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        self.assertIn("tools", manifest, "manifest v2 tools missing")
        self.assertIn("categories", manifest, "manifest v2 categories missing")
        manifest["tools"].append(
            {
                "id": "fixture.missing",
                "title": "Fixture missing",
                "category": manifest["categories"][0]["id"],
                "kind": "cli",
                "summary": "fixture",
                "docs": "scripts/toolchain.py",
                "command": [sys.executable, "scripts/no-such-tool.py"],
                "writes": ["none"],
            }
        )
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "toolchain.json"
            path.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
            result = run_cli("check", "--manifest", str(path))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("no-such-tool.py", result.stdout + result.stderr)

    def test_check_optional_task_is_warning(self):
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        self.assertIn("surfaces", manifest, "manifest v2 surfaces missing")
        for surface in manifest["surfaces"]:
            if surface.get("id") == "nightly-task":
                surface["name"] = "NoSuchToolchainTask"
                surface["required"] = False
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "toolchain.json"
            path.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
            result = run_cli("check", "--manifest", str(path))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("NoSuchToolchainTask", result.stdout + result.stderr)


class GeneratedDocsTests(unittest.TestCase):
    def test_generated_doc_is_current(self):
        result = run_cli("docs", "--check")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(DOC_INDEX.is_file(), f"generated index missing: {DOC_INDEX}")
        text = DOC_INDEX.read_text(encoding="utf-8")
        self.assertIn("DO NOT EDIT", text)
        self.assertIn("baking.toolbox", text)
        self.assertIn("baking.body", text)
        self.assertIn("预览", text)
        self.assertIn("nightly-task", text)

    def test_generation_is_idempotent(self):
        first = run_cli("docs")
        self.assertEqual(first.returncode, 0, first.stderr)
        before = DOC_INDEX.read_bytes()
        second = run_cli("docs")
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(before, DOC_INDEX.read_bytes())


class SurfaceTests(unittest.TestCase):
    def test_ci_script_references_exist(self):
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        for line in workflow.splitlines():
            for token in line.replace("\"", " ").replace("'", " ").split():
                token = token.strip("$,;()[]{}")
                if token.startswith("scripts/") and token.endswith((".ps1", ".py")):
                    self.assertTrue((ROOT / token).is_file(), f"CI references missing script: {token}")

    def test_hook_script_references_exist(self):
        for relative in (".zcode/config.json", ".claude/settings.json"):
            path = ROOT / relative
            if not path.is_file():
                continue
            text = path.read_text(encoding="utf-8")
            for token in text.replace("\"", " ").replace("'", " ").split():
                token = token.strip("$,;()[]{}")
                if token.startswith("scripts/") and token.endswith((".ps1", ".py")):
                    self.assertTrue((ROOT / token).is_file(), f"{relative} references missing script: {token}")


class ScriptFormManifestTests(unittest.TestCase):
    def load(self):
        return json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_user_facing_scripts_registered(self):
        manifest = self.load()
        by_id = {tool["id"]: tool for tool in manifest["tools"]}
        expected = {
            "deploy.room": "scripts/deploy/deploy-room.py",
            "deploy.gm": "scripts/deploy/gm.py",
            "deploy.wg-peer": "scripts/deploy/wg-peer.py",
            "env.local-chain": "scripts/local/local-chain.py",
            "quality.doc-lint": "scripts/gate/doc-lint.py",
            "quality.scan-missing-guids": "scripts/content/scan_missing_guids.py",
        }
        for tool_id, script in expected.items():
            self.assertIn(tool_id, by_id, f"manifest missing {tool_id}")
            tool = by_id[tool_id]
            self.assertIn(script, tool.get("command") or [], f"{tool_id} command must point at {script}")
            self.assertTrue((ROOT / tool["docs"]).is_file(), f"{tool_id} docs missing: {tool['docs']}")

    def test_deploy_category_declared(self):
        manifest = self.load()
        self.assertIn("deploy", {category["id"] for category in manifest["categories"]})

    def test_internal_toolchain_modules_use_underscore_prefix(self):
        for name in ("_toolchain_manifest.py", "_toolchain_docs.py", "_toolchain_surfaces.py"):
            self.assertTrue((ROOT / "scripts" / name).is_file(), f"missing internal module {name}")
        for name in ("toolchain_manifest.py", "toolchain_docs.py", "toolchain_surfaces.py"):
            self.assertFalse((ROOT / "scripts" / name).exists(), f"legacy internal module still present: {name}")

    def test_local_cdn_loader_is_internal(self):
        self.assertTrue((ROOT / "scripts" / "content" / "_local_cdn.py").is_file())
        self.assertFalse((ROOT / "scripts" / "content" / "_local-cdn.ps1").exists())
        self.assertFalse((ROOT / "scripts" / "content" / "local-cdn.config.ps1").exists())

    def test_pipeline_patch_is_marked_oneshot(self):
        text = (ROOT / "scripts" / "pipeline" / "patch_p1_plugin_metas.py").read_text(encoding="utf-8")
        header = "\n".join(text.splitlines()[:20])
        self.assertIn("@oneshot", header)

    def test_gitattributes_declares_script_eol(self):
        path = ROOT / ".gitattributes"
        self.assertTrue(path.is_file(), ".gitattributes missing")
        text = path.read_text(encoding="utf-8")
        self.assertIn("scripts/**", text)
        self.assertIn("eol=lf", text)


if __name__ == "__main__":
    unittest.main()
