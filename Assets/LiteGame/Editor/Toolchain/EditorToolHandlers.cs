using System;
using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Widgets;
using LiteGame.Editor.UiTools.Workbench;
using LiteGame.EditorTools;
using LiteGame.EditorTools.UI;
using LiteSim.Editor;
using Tools.DisciplineScan.Editor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    internal static partial class EditorToolHandlers
    {
        public static void RegisterAll(EditorToolRegistry registry)
        {
            RegisterBaking(registry);
            RegisterGm(registry);
            RegisterUi(registry);
            RegisterActions(registry);
            RegisterContent(registry);
        }

        private static ToolResult Ok(string text)
        {
            return ToolResult.Success(text);
        }

        private static ToolResult Report(string text, bool ok)
        {
            return ok ? ToolResult.Success(text) : ToolResult.Failed(text);
        }

        private static void RegisterBaking(EditorToolRegistry registry)
        {
            registry.RegisterWindow("baking.toolbox", BakingToolbox.Open);
            registry.RegisterAction("baking.body", request => BakingToolActions.RunBody(request));
            registry.RegisterAction("baking.muzzle", request => BakingToolActions.RunMuzzle(request));
            registry.RegisterAction("baking.grip", request => BakingToolActions.RunGrip(request));
            registry.RegisterAction("baking.root-motion", request => BakingToolActions.RunRootMotion(request));
            registry.RegisterAction("baking.map", request => BakingToolActions.RunMap(request));
        }

        private static void RegisterGm(EditorToolRegistry registry)
        {
            registry.RegisterPage("debug.test-mode", () => new GmTestModePage());
            registry.RegisterPage("debug.tunables", () => new GmDebugTunablesPage());
            registry.RegisterPage("debug.commands", () => new GmCommandsPage());
            registry.RegisterPage("debug.scene", () => new GmScenePage());
            registry.RegisterPage("debug.config-diagnostics", () => new GmConfigDiagnosticsPage());
            registry.RegisterPage("debug.local-chain", () => new GmLocalChainPage());
            registry.RegisterPage("debug.net-diag", () => new GmNetDiagPage());
        }

        private static void RegisterUi(EditorToolRegistry registry)
        {
            registry.RegisterWindow("ui.workbench", UiWorkbenchWindow.Open);
            registry.RegisterWindow("ui.page-scaffold", UiPageScaffoldWindow.Open);
            registry.RegisterWindow("ui.page-register", UiPageRegisterWindow.Open);

            registry.RegisterAction("ui.validate", ValidateUi);
            registry.RegisterAction("ui.contract", GenerateUiContracts);
            registry.RegisterAction("ui.contract-diff", DiffUiContracts);
            registry.RegisterAction("ui.widget-manifest", GenerateUiWidgetManifest);
            registry.RegisterAction("ui.page-manifest", SyncUiPageManifest);
            registry.RegisterAction("ui.style.colors-refresh", ApplyUiColors);
            registry.RegisterAction("ui.style.colors-report", ReportUiColors);
            registry.RegisterAction("ui.style.text-report", ReportUiText);
            registry.RegisterAction("ui.style.text-apply", ApplyUiText);
            registry.RegisterAction("ui.binding-hash-all", () =>
            {
                UiBindGenerationHash.RecordAllForTool();
                return Ok("已重记录全部 BindRoot prefab（见 Console）。");
            });
            registry.RegisterAction("ui.binding-hash-selected", () =>
            {
                UiBindGenerationHash.RecordSelectedForTool();
                return Ok("已记录选中 prefab（见 Console）。");
            });
        }

        private static void RegisterActions(EditorToolRegistry registry)
        {
            registry.RegisterAction("codegen.sim-trig", request => SimTrigToolActions.Run(request));
            registry.RegisterAction("codegen.lua-reload", () =>
            {
                LuaDevReload.ReloadForTool();
                return Ok("已请求 Lua Reload（仅 Play 模式生效，见 Console）。");
            });
            registry.RegisterAction("codegen.lua-refill", () =>
            {
                LuaDevRefill.RefillForTool();
                return Ok("已请求 Lua 注册表重填（仅 Play 模式生效，见 Console）。");
            });
            registry.RegisterAction("quality.discipline", () =>
            {
                var result = DisciplineScanMenu.RunForTool();
                return Report(result.report, result.violations == 0);
            });
            registry.RegisterAction("quality.ieee", () =>
            {
                var result = IeeeBaselineChecker.Verify();
                return Report(result.report, result.ok);
            });
        }

        private static void RegisterContent(EditorToolRegistry registry)
        {
            registry.RegisterAction("content.bundle-builtin", () =>
            {
                BuiltinBundleBuildInvoker.BuildFromMenu();
                return Ok("已请求构建内置 Bundle（进度见 Console/构建面板）。");
            });
            registry.RegisterAction("content.bundle-candidate", () =>
            {
                BuiltinBundleBuildInvoker.RunForCandidateFromMenu();
                return Ok("已请求构建候选 Bundle（不覆盖内置包）。");
            });
        }

        private static ToolResult ValidateUi()
        {
            UiReport report = UiValidator.Run();
            return Report(report.ToString(), report.Passed);
        }

        private static ToolResult GenerateUiContracts()
        {
            UiContractBatchResult batch = UiContractBatch.Generate();
            if (!batch.Ok) return ToolResult.Failed(batch.Summary, batch.ToReport());
            return batch.Warnings.Count > 0
                ? ToolResult.Warning(batch.Summary, batch.ToReport())
                : ToolResult.Success(batch.Summary, batch.ToReport());
        }

        private static ToolResult DiffUiContracts()
        {
            UiContractBatchDiffResult diff = UiContractBatch.Diff();
            if (!diff.Ok) return ToolResult.Failed(diff.Summary, diff.ToReport());
            bool hasNotice = diff.ChangedCount > 0 || diff.MissingBaseline.Count > 0 || diff.Warnings.Count > 0;
            return hasNotice
                ? ToolResult.Warning(diff.Summary, diff.ToReport())
                : ToolResult.Success(diff.Summary, diff.ToReport());
        }

        private static ToolResult GenerateUiWidgetManifest()
        {
            UiWidgetManifestGenerationResult generated = UiWidgetManifestGenerator.GenerateToDisk();
            if (!generated.Ok) return ToolResult.Failed("控件清单生成失败。", generated.ErrorText);

            int warnings = generated.Warnings != null ? generated.Warnings.Count : 0;
            string summary = "控件清单：" + generated.Manifest.Widgets.Count + " 个控件 / 告警 " + warnings;
            string details = summary + "\n写入 " + UiWidgetManifestPaths.DefaultPath;
            foreach (string warning in generated.Warnings ?? new List<string>()) details += "\n  告警 " + warning;
            return warnings > 0 ? ToolResult.Warning(summary, details) : ToolResult.Success(summary, details);
        }

        private static ToolResult SyncUiPageManifest()
        {
            UiPageManifestSyncResult sync = UiPageManifestSync.SyncToDisk();
            if (!sync.Ok) return ToolResult.Failed("页面清单对账失败。", sync.ErrorText);

            int pages = sync.Manifest.Pages?.Count ?? 0;
            int forms = sync.Manifest.Pages?.Sum(page => page.Forms?.Count ?? 0) ?? 0;
            int warnings = sync.Warnings != null ? sync.Warnings.Count : 0;
            string summary = "页面清单：" + pages + " 页 / " + forms + " 行登记 / 告警 " + warnings;
            string details = summary + "\n写入 " + UiPageManifestPaths.DefaultPath;
            foreach (string warning in sync.Warnings ?? new List<string>()) details += "\n  告警 " + warning;
            return warnings > 0 ? ToolResult.Warning(summary, details) : ToolResult.Success(summary, details);
        }

        private static ToolResult ApplyUiColors()
        {
            var applied = UiStyleTool.ApplyColors(UiStyleTool.DefaultScanDirs);
            return Ok("已刷新 UI 颜色：prefab " + applied.prefabs + "，graphics " + applied.graphics + "。");
        }

        private static ToolResult ReportUiColors()
        {
            List<(string asset, string node, Color color)> drifts =
                UiStyleTool.ReportDrift(UiStyleTool.DefaultScanDirs);
            if (drifts.Count == 0) return Ok("UI 颜色无漂移。");
            string text = string.Join("\n", drifts.Select(item =>
                item.asset + " / " + item.node + " / #" + ColorUtility.ToHtmlStringRGBA(item.color)));
            return Ok("UI 颜色漂移 " + drifts.Count + " 条：\n" + text);
        }

        private static ToolResult ApplyUiText()
        {
            var applied = UiTextStyleTool.ApplyTextStyles();
            return Ok("已应用文字档：prefab " + applied.prefabs + "，写入 " + applied.writes + "。");
        }

        private static ToolResult ReportUiText()
        {
            List<UiTextIssue> issues = UiTextStyleTool.ReportTextDrift();
            if (issues.Count == 0) return Ok("文字档无漂移。");
            string text = string.Join("\n", issues.Select(issue =>
                issue.Asset + " / " + issue.Node + " / " + issue.Kind + " / " + issue.Detail));
            return Ok("文字档漂移 " + issues.Count + " 条：\n" + text);
        }
    }
}
