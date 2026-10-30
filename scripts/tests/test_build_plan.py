# -*- coding: utf-8 -*-
"""Contracts for the build preflight CLI (read-only diff -> rebuild plan)."""

import importlib.util
import json
import subprocess
import sys
import unittest
from unittest import mock
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build" / "build-plan.py"


def load_module():
    if not SCRIPT.is_file():
        raise AssertionError(f"build-plan script missing: {SCRIPT}")
    spec = importlib.util.spec_from_file_location("build_plan_under_test", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class BuildPlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = load_module()

    def plan(self, paths):
        return self.mod.build_plan(list(paths), base="BASE", head="HEAD")

    def finding(self, plan, path):
        normalized = self.mod.normalize_path(path)
        for item in plan["findings"]:
            if item["path"] == normalized:
                return item
        self.fail(f"finding missing for {path}: {plan['findings']}")

    def assert_actions(self, plan, group, expected):
        self.assertEqual(plan[group], expected, f"{group}: {plan[group]}")

    # ---- AC1 / AC3 / AC9 ----

    def test_shared_link_membership_does_not_require_existing_file(self):
        self.assertTrue(self.mod._is_shared_path(
            "Missing/Link.cs", set(), (), {"Missing/Link.cs"}))

    def test_deleted_or_renamed_shared_prefix_is_still_shared(self):
        plan = self.plan(["Assets/LiteNet/Core/Protocol/Deleted.cs"])
        self.assertEqual(self.finding(plan, "Assets/LiteNet/Core/Protocol/Deleted.cs")["area"], "shared")

    def test_project_file_is_two_end_build_config(self):
        path = "Assets/LiteSim/LiteSim.csproj"
        item = self.finding(self.plan([path]), path)
        self.assertEqual(item["area"], "project_settings")
        self.assertIn("rebuild_client_player", item["deliveryActions"])
        self.assertIn("rebuild_server_room", item["deliveryActions"])

    def test_ignored_prefixes(self):
        self.assertTrue(self.mod._ignored("Builds/x.exe"))
        self.assertTrue(self.mod._ignored("TestResults/y.json"))
        self.assertFalse(self.mod._ignored("Docs/x.md"))

    def test_manifest_drift_per_project_does_not_misreport_registered_links(self):
        plan = self.plan([])
        paths = {entry["path"] for entry in plan["manifestDrift"]}
        for registered in (
            "Assets/LiteGame/Modules/Config/SimConfigMapper.cs",
            "Assets/LiteGame/Modules/Config/TableLayout.cs",
            "Assets/RoomServer/Modules/JoinTicketFormat.cs",
            "Assets/RoomServer/Modules/Base64Url.cs",
            "Assets/RoomServer/Core/MatchStateMachine.cs",
        ):
            self.assertNotIn(registered, paths)
        self.assertTrue(any(path.endswith("MapTableProjection.cs") for path in paths))

    def test_human_output_renders_manifest_drift(self):
        text = self.mod._render_text(self.plan([]))
        self.assertIn("已知例外", text)
        self.assertIn("MapTableProjection.cs", text)

    def test_actions_follow_semantic_order(self):
        plan = self.plan(["Luban/Data/weapons.xlsx"])
        self.assertEqual(plan["delivery"][:5], [
            "regen_luban", "regen_buildhash", "rebuild_content_bundle",
            "rebuild_client_player", "rebuild_server_room",
        ])


    def test_no_changes_reports_nothing_to_build(self):
        plan = self.plan([])
        self.assertEqual(plan["findings"], [])
        self.assertFalse(plan["deliveryNeedsPlayerBuild"])
        self.assertTrue(any("不需要出包" in note for note in plan["notes"]), plan["notes"])

    def test_shared_source_requires_hash_client_server_and_publish(self):
        plan = self.plan(["Assets/LiteNet/Core/Protocol/Wire.cs"])
        item = self.finding(plan, "Assets/LiteNet/Core/Protocol/Wire.cs")
        self.assertEqual(item["area"], "shared")
        self.assertIn("regen_buildhash", item["innerActions"])
        for action in ("rebuild_client_player", "rebuild_server_room", "publish_server"):
            self.assertIn(action, item["deliveryActions"])

    def test_shared_link_file_is_shared(self):
        plan = self.plan(["Assets/LiteGame/Modules/Config/TableLayout.cs"])
        self.assertEqual(self.finding(plan, "Assets/LiteGame/Modules/Config/TableLayout.cs")["area"], "shared")

    def test_known_missing_shared_link_is_shared_with_warning(self):
        plan = self.plan(["Assets/LiteGame/Modules/Config/MapTableProjection.cs"])
        item = self.finding(plan, "Assets/LiteGame/Modules/Config/MapTableProjection.cs")
        self.assertEqual(item["area"], "shared")
        self.assertIn("rebuild_server_room", item["deliveryActions"])
        self.assertTrue(any("MapTableProjection" in note for note in plan["notes"]), plan["notes"])

    def test_proto(self):
        plan = self.plan(["Assets/LiteNet/Proto/Wire.proto"])
        item = self.finding(plan, "Assets/LiteNet/Proto/Wire.proto")
        self.assertEqual(item["area"], "proto")
        self.assertIn("regen_proto", item["innerActions"])
        self.assertIn("publish_server", item["deliveryActions"])

    def test_luban(self):
        plan = self.plan(["Luban/Data/weapons.xlsx"])
        item = self.finding(plan, "Luban/Data/weapons.xlsx")
        self.assertEqual(item["area"], "luban")
        self.assertIn("regen_luban", item["innerActions"])
        self.assertIn("rebuild_content_bundle", item["deliveryActions"])
        self.assertIn("publish_server", item["deliveryActions"])

    # ---- server / meta / room kernel ----

    def test_meta_server(self):
        plan = self.plan(["Server/MetaServer/Program.cs"])
        item = self.finding(plan, "Server/MetaServer/Program.cs")
        self.assertEqual(item["area"], "meta")
        self.assertIn("publish_meta", item["deliveryActions"])

    def test_meta_extra_room_file_also_rebuilds_server(self):
        path = "Assets/RoomServer/Modules/JoinTicketFormat.cs"
        item = self.finding(self.plan([path]), path)
        self.assertEqual(item["area"], "meta")
        for action in ("publish_meta", "rebuild_server_room", "publish_server"):
            self.assertIn(action, item["deliveryActions"])

    def test_room_server_host(self):
        plan = self.plan(["Server/RoomServer/Program.cs"])
        item = self.finding(plan, "Server/RoomServer/Program.cs")
        self.assertEqual(item["area"], "server")
        self.assertIn("rebuild_server_room", item["deliveryActions"])
        self.assertIn("publish_server", item["deliveryActions"])

    def test_room_kernel_does_not_rebuild_client(self):
        plan = self.plan(["Assets/RoomServer/Core/Ghost.cs"])
        item = self.finding(plan, "Assets/RoomServer/Core/Ghost.cs")
        self.assertEqual(item["area"], "room_kernel")
        self.assertIn("editor_reload", item["innerActions"])
        self.assertIn("rebuild_server_room", item["deliveryActions"])
        self.assertNotIn("rebuild_client_player", item["deliveryActions"])

    # ---- client / content / editor / docs ----

    def test_client_code(self):
        plan = self.plan(["Assets/LiteGame/Modules/Procedure/Stages/ProcedureMatch.cs"])
        item = self.finding(plan, "Assets/LiteGame/Modules/Procedure/Stages/ProcedureMatch.cs")
        self.assertEqual(item["area"], "client_code")
        self.assertIn("rebuild_client_player", item["deliveryActions"])
        self.assertNotIn("publish_server", item["deliveryActions"])

    def test_content_resources(self):
        paths = [
            "Assets/Art/weapons/rifle.png",
            "Assets/Animation/aim.anim",
            "Assets/LiteGame/Lua/UI/Page_Login.lua",
            "Assets/ShareResource/UI/Screens/Page_Lobby.prefab",
            "Assets/Scenes/Boot.unity",
            "Assets/GameData/Config/UI/UiTheme.asset",
        ]
        plan = self.plan(paths)
        for path in paths:
            item = self.finding(plan, path)
            self.assertEqual(item["area"], "content", path)
            self.assertIn("editor_reload", item["innerActions"], path)
            self.assertIn("rebuild_content_bundle", item["deliveryActions"], path)
            self.assertIn("rebuild_client_player", item["deliveryActions"], path)

    def test_config_table_values_also_publish_server(self):
        path = "Assets/GameData/Config/Tables/weapon.bytes"
        item = self.finding(self.plan([path]), path)
        self.assertEqual(item["area"], "content")
        self.assertIn("publish_server", item["deliveryActions"])

    def test_meta_files_follow_their_asset_area(self):
        plan = self.plan([
            "Assets/LiteFramework/Runtime/Modules/UI/Style/UiThemeAsset.cs.meta",
            "Assets/ShareResource/UI/Screens/Page_Lobby.prefab.meta",
        ])
        self.assertEqual(self.finding(plan, "Assets/LiteFramework/Runtime/Modules/UI/Style/UiThemeAsset.cs.meta")["area"], "client_code")
        self.assertEqual(self.finding(plan, "Assets/ShareResource/UI/Screens/Page_Lobby.prefab.meta")["area"], "content")

    def test_editor_code(self):
        plan = self.plan(["Assets/LiteGame/Editor/Toolchain/Registry/EditorToolHandlers.cs"])
        item = self.finding(plan, "Assets/LiteGame/Editor/Toolchain/Registry/EditorToolHandlers.cs")
        self.assertEqual(item["area"], "editor_code")
        self.assertIn("editor_reload", item["innerActions"])
        self.assertNotIn("rebuild_client_player", item["deliveryActions"])

    def test_docs_scripts_and_ci_need_no_build(self):
        plan = self.plan(["Docs/项目说明/11-联机测试流程.md", "scripts/gate/test.py", ".github/workflows/ci.yml"])
        self.assertFalse(plan["deliveryNeedsPlayerBuild"])
        for path in ("Docs/项目说明/11-联机测试流程.md", "scripts/gate/test.py", ".github/workflows/ci.yml"):
            self.assertEqual(self.finding(plan, path)["area"], "docs" if path.startswith("Docs/") else "scripts")

    def test_deploy_config(self):
        path = "deploy/roomserver.windows.json"
        item = self.finding(self.plan([path]), path)
        self.assertEqual(item["area"], "deploy_config")
        self.assertIn("publish_room_config", item["deliveryActions"])
        self.assertIn("restart_room", item["deliveryActions"])

    def test_luban_package_and_other_packages(self):
        plan = self.plan(["Packages/com.code-philosophy.luban/Runtime/ByteBuf.cs", "Packages/com.unity.textmeshpro/Foo.cs"])
        self.assertEqual(self.finding(plan, "Packages/com.code-philosophy.luban/Runtime/ByteBuf.cs")["area"], "package")
        self.assertIn("publish_server", self.finding(plan, "Packages/com.code-philosophy.luban/Runtime/ByteBuf.cs")["deliveryActions"])
        self.assertIn("rebuild_client_player", self.finding(plan, "Packages/com.unity.textmeshpro/Foo.cs")["deliveryActions"])

    def test_project_settings(self):
        item = self.finding(self.plan(["ProjectSettings/GraphicsSettings.asset"]), "ProjectSettings/GraphicsSettings.asset")
        self.assertEqual(item["area"], "project_settings")
        self.assertIn("rebuild_client_player", item["deliveryActions"])

    # ---- AC4 ----

    def test_unknown_path_is_conservative(self):
        plan = self.plan(["Weird/file.bin"])
        item = self.finding(plan, "Weird/file.bin")
        self.assertEqual(item["area"], "unknown")
        for action in ("regen_buildhash", "rebuild_client_player", "rebuild_server_room", "publish_server"):
            self.assertIn(action, item["deliveryActions"])
        self.assertIn("Weird/file.bin", plan["unknownPaths"])

    def test_priority_conflicts(self):
        plan = self.plan([
            "Assets/LiteSim/Scripts/Editor/Helper.cs",
            "Assets/LiteGame/Lua/UI/Page_Login.lua",
            "Assets/LiteGame/Editor/Toolchain/Registry/EditorToolHandlers.cs",
        ])
        self.assertEqual(self.finding(plan, "Assets/LiteSim/Scripts/Editor/Helper.cs")["area"], "editor_code")
        self.assertEqual(self.finding(plan, "Assets/LiteGame/Lua/UI/Page_Login.lua")["area"], "content")
        self.assertEqual(self.finding(plan, "Assets/LiteGame/Editor/Toolchain/Registry/EditorToolHandlers.cs")["area"], "editor_code")


    def test_drift_is_project_scoped(self):
        build_hash = self.mod._load_build_hash()
        meta_link = "../../Assets/RoomServer/Modules/JoinTicketFormat.cs"
        drift = self.mod.drift_for_links(
            [meta_link], ROOT, registered=set(build_hash.SHARED_LINK_FILES), wildcards=())
        self.assertTrue(any(not entry["known"] for entry in drift), drift)
        shared_link = "../../Assets/LiteGame/Modules/Config/TableLayout.cs"
        drift = self.mod.drift_for_links(
            [shared_link], ROOT, registered=set(build_hash.META_EXTRA_FILES), wildcards=())
        self.assertTrue(any(not entry["known"] for entry in drift), drift)
        drift = self.mod.drift_for_links(
            ["../../Packages/com.code-philosophy.luban/Runtime/*.cs"], ROOT,
            registered=set(build_hash.SHARED_LINK_FILES), wildcards=self.mod.WILDCARD_WHITELIST)
        self.assertEqual(drift, [])

    def test_mixed_changes_are_deduped_in_semantic_order(self):
        plan = self.plan([
            "Assets/LiteNet/Core/Protocol/Wire.cs",
            "Assets/LiteGame/Modules/Procedure/Stages/ProcedureMatch.cs",
            "Assets/ShareResource/UI/Screens/Page_Lobby.prefab",
            "Docs/项目说明/11-联机测试流程.md",
        ])
        self.assertEqual(plan["delivery"], [
            "regen_buildhash", "rebuild_content_bundle", "rebuild_client_player",
            "rebuild_server_room", "publish_server",
        ])

    def test_collect_changes_counts_ignored_and_includes_untracked(self):
        def fake_git(args, project):
            if args[0] == "rev-parse": return "abc\n"
            if args[0] == "diff" and any("...HEAD" in arg for arg in args): return "M\tDocs/a.md\n"
            if args[0] == "diff": return ""
            if args[0] == "ls-files": return "Builds/x.exe\nDocs/b.md\n"
            return ""

        with mock.patch.object(self.mod, "_run_git", side_effect=fake_git):
            paths, ignored = self.mod.collect_changes("origin/main", ROOT)
        self.assertIn("Docs/a.md", paths)
        self.assertIn("Docs/b.md", paths)
        self.assertEqual(ignored, 1)

    def test_human_output_renders_unknown_drift(self):
        plan = self.plan([])
        plan["manifestDrift"].append({"path": "Assets/NewLink.cs", "known": False})
        text = self.mod._render_text(plan)
        self.assertIn("未登记", text)
        self.assertIn("Assets/NewLink.cs", text)

    # ---- AC7 matrix ----

    def test_release_matrix_six_rows(self):
        rows = [
            ("Assets/ShareResource/UI/Screens/Page_Lobby.prefab", {"rebuild_content_bundle", "rebuild_client_player"}),
            ("Server/RoomServer/Program.cs", {"rebuild_server_room", "publish_server"}),
            ("Assets/LiteNet/Core/Protocol/Wire.cs", {"rebuild_client_player", "rebuild_server_room", "publish_server"}),
            ("Assets/RoomServer/Core/Ghost.cs", {"rebuild_server_room", "publish_server"}),
            ("Luban/Data/weapons.xlsx", {"rebuild_content_bundle", "rebuild_client_player", "rebuild_server_room", "publish_server"}),
            ("deploy/roomserver.windows.json", {"publish_room_config", "restart_room"}),
        ]
        for path, expected in rows:
            plan = self.plan([path])
            delivery = set(plan["delivery"])
            self.assertTrue(expected <= delivery, f"{path}: {delivery}")

    # ---- AC5 JSON ----

    def test_json_shape(self):
        plan = self.plan(["Assets/LiteNet/Core/Protocol/Wire.cs"])
        self.assertEqual(plan["schemaVersion"], 1)
        for key in (
            "base", "head", "innerLoop", "delivery", "deliveryNeedsPlayerBuild",
            "findings", "unknownPaths", "manifestDrift", "ignoredCount", "notes",
        ):
            self.assertIn(key, plan)
        item = plan["findings"][0]
        for key in ("path", "area", "ring", "innerActions", "deliveryActions", "reason"):
            self.assertIn(key, item)

    # ---- AC9 normalization ----

    def test_path_normalization_and_dedup(self):
        plan = self.plan([".\\Assets\\LiteNet\\Core\\Protocol\\Wire.cs", "Assets/LiteNet/Core/Protocol/Wire.cs"])
        self.assertEqual(len(plan["findings"]), 1)
        self.assertEqual(plan["findings"][0]["path"], "Assets/LiteNet/Core/Protocol/Wire.cs")

    # ---- AC4 drift ----

    def test_csproj_drift_known_exception(self):
        text = (ROOT / "Server" / "RoomServer" / "RoomServer.csproj").read_text(encoding="utf-8")
        links = self.mod.parse_csproj(text)
        self.assertTrue(any(link.endswith("MapTableProjection.cs") for link in links), links)
        plan = self.mod.build_plan([], base="BASE", head="HEAD")
        drift = {entry["path"]: entry["known"] for entry in plan["manifestDrift"]}
        self.assertTrue(any(path.endswith("MapTableProjection.cs") and known for path, known in drift.items()), drift)

    def test_csproj_drift_injected_unknowns(self):
        text = (
            '<ItemGroup><Compile Include="..\\..\\Assets\\NewFile.cs" Link="New.cs" />'
            '<Compile Include="..\\..\\Elsewhere\\**\\*.cs" Link="Else\\%(Filename)" /></ItemGroup>'
        )
        links = self.mod.parse_csproj(text)
        drift = self.mod.drift_for_links(links, ROOT)
        self.assertTrue(any(entry["path"].endswith("NewFile.cs") and not entry["known"] for entry in drift), drift)
        self.assertTrue(any(not entry["known"] for entry in drift if "*" in entry["path"]), drift)

    # ---- read-only CLI ----

    def test_cli_help(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--help"], cwd=str(ROOT),
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--base", result.stdout)

    def test_cli_base_failure(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--base", "no-such-ref-xyz"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)

    def test_cli_changed_file_json(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--changed-file", "Assets/LiteNet/Core/Protocol/Wire.cs", "--json"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        data = json.loads(result.stdout)
        self.assertEqual(data["schemaVersion"], 1)
        self.assertTrue(any(item["area"] == "shared" for item in data["findings"]))

    def test_cli_readonly_worktree(self):
        before = subprocess.run(["git", "status", "--porcelain"], cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace")
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--base", "HEAD", "--json"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        after = subprocess.run(["git", "status", "--porcelain"], cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace")
        self.assertEqual(before.stdout, after.stdout)

    def test_cli_foreign_project_path_fails(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--project-path", str(ROOT.parent), "--changed-file", "Docs/x.md"],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
