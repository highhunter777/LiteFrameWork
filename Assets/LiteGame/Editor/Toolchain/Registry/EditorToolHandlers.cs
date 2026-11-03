using System;
using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor;
using LiteGame.Editor.UITools.Binding;
using LiteGame.Editor.UITools.Compose;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Preview;
using LiteGame.Editor.UITools.Validation;
using LiteGame.Editor.UITools.Widgets;
using LiteGame.Editor.UITools.Workbench;
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
            RegisterUI(registry);
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
            registry.RegisterPage("debug.test-mode", () => new GMTestModePage());
            registry.RegisterPage("debug.tunables", () => new GMDebugTunablesPage());
            registry.RegisterPage("debug.commands", () => new GMCommandsPage());
            registry.RegisterPage("debug.scene", () => new GMScenePage());
            registry.RegisterPage("debug.config-diagnostics", () => new GMConfigDiagnosticsPage());
            registry.RegisterPage("debug.local-chain", () => new GMLocalChainPage());
            registry.RegisterPage("debug.net-diag", () => new GMNetDiagPage());
        }

        private static void RegisterUI(EditorToolRegistry registry)
        {
            registry.RegisterWindow("ui.workbench", UIWorkbenchWindow.Open);
            registry.RegisterWindow("ui.page-scaffold", UIPageScaffoldWindow.Open);
            registry.RegisterWindow("ui.page-register", UIPageRegisterWindow.Open);
            registry.RegisterWindow("ui.preview-matrix", UIPreviewMatrixWindow.Open);

            registry.RegisterAction("ui.validate", ValidateUI);
            registry.RegisterAction("ui.contract", GenerateUIContracts);
            registry.RegisterAction("ui.contract-diff", DiffUIContracts);
            registry.RegisterAction("ui.widget-manifest", GenerateUIWidgetManifest);
            registry.RegisterAction("ui.page-manifest", SyncUIPageManifest);
            registry.RegisterAction("ui.style.colors-refresh", ApplyUIColors);
            registry.RegisterAction("ui.style.colors-report", ReportUIColors);
            registry.RegisterAction("ui.style.text-report", ReportUIText);
            registry.RegisterAction("ui.style.text-apply", ApplyUIText);
            registry.RegisterAction("ui.style.tokens", GenerateThemeTokens);
            registry.RegisterAction("ui.binding-hash-all", () =>
            {
                UIBindGenerationHash.RecordAllForTool();
                return Ok("已重记录全部 BindRoot prefab（见 Console）。");
            });
            registry.RegisterAction("ui.binding-hash-selected", () =>
            {
                UIBindGenerationHash.RecordSelectedForTool();
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

        private static ToolResult ValidateUI()
        {
            UIReport report = UIValidator.Run();
            return Report(report.ToString(), report.Passed);
        }

        private static ToolResult GenerateUIContracts()
        {
            UIContractBatchResult batch = UIContractBatch.Generate();
            if (!batch.Ok) return ToolResult.Failed(batch.Summary, batch.ToReport());
            return batch.Warnings.Count > 0
                ? ToolResult.Warning(batch.Summary, batch.ToReport())
                : ToolResult.Success(batch.Summary, batch.ToReport());
        }

        private static ToolResult DiffUIContracts()
        {
            UIContractBatchDiffResult diff = UIContractBatch.Diff();
            if (!diff.Ok) return ToolResult.Failed(diff.Summary, diff.ToReport());
            bool hasNotice = diff.ChangedCount > 0 || diff.MissingBaseline.Count > 0 || diff.Warnings.Count > 0;
            return hasNotice
                ? ToolResult.Warning(diff.Summary, diff.ToReport())
                : ToolResult.Success(diff.Summary, diff.ToReport());
        }

        private static ToolResult GenerateUIWidgetManifest()
        {
            UIWidgetManifestGenerationResult generated = UIWidgetManifestGenerator.GenerateToDisk();
            if (!generated.Ok) return ToolResult.Failed("控件清单生成失败。", generated.ErrorText);

            int warnings = generated.Warnings != null ? generated.Warnings.Count : 0;
            string summary = "控件清单：" + generated.Manifest.Widgets.Count + " 个控件 / 告警 " + warnings;
            string details = summary + "\n写入 " + UIWidgetManifestPaths.DefaultPath;
            foreach (string warning in generated.Warnings ?? new List<string>()) details += "\n  告警 " + warning;
            return warnings > 0 ? ToolResult.Warning(summary, details) : ToolResult.Success(summary, details);
        }

        private static ToolResult SyncUIPageManifest()
        {
            UIPageManifestSyncResult sync = UIPageManifestSync.SyncToDisk();
            if (!sync.Ok) return ToolResult.Failed("页面清单对账失败。", sync.ErrorText);

            int pages = sync.Manifest.Pages?.Count ?? 0;
            int forms = sync.Manifest.Pages?.Sum(page => page.Forms?.Count ?? 0) ?? 0;
            int warnings = sync.Warnings != null ? sync.Warnings.Count : 0;
            string summary = "页面清单：" + pages + " 页 / " + forms + " 行登记 / 告警 " + warnings;
            string details = summary + "\n写入 " + UIPageManifestPaths.DefaultPath;
            foreach (string warning in sync.Warnings ?? new List<string>()) details += "\n  告警 " + warning;
            return warnings > 0 ? ToolResult.Warning(summary, details) : ToolResult.Success(summary, details);
        }

        private static ToolResult ApplyUIColors()
        {
            var applied = UIStyleTool.ApplyColors(UIStyleTool.DefaultScanDirs);
            return Ok("已刷新 UI 颜色：prefab " + applied.prefabs + "，graphics " + applied.graphics + "。");
        }

        private static ToolResult ReportUIColors()
        {
            List<(string asset, string node, Color color)> drifts =
                UIStyleTool.ReportDrift(UIStyleTool.DefaultScanDirs);
            if (drifts.Count == 0) return Ok("UI 颜色无漂移。");
            string text = string.Join("\n", drifts.Select(item =>
                item.asset + " / " + item.node + " / #" + ColorUtility.ToHtmlStringRGBA(item.color)));
            return Ok("UI 颜色漂移 " + drifts.Count + " 条：\n" + text);
        }

        private static ToolResult ApplyUIText()
        {
            var applied = UITextStyleTool.ApplyTextStyles();
            return Ok("已应用文字档：prefab " + applied.prefabs + "，写入 " + applied.writes + "。");
        }

        private static ToolResult ReportUIText()
        {
            List<UITextIssue> issues = UITextStyleTool.ReportTextDrift();
            if (issues.Count == 0) return Ok("文字档无漂移。");
            string text = string.Join("\n", issues.Select(issue =>
                issue.Asset + " / " + issue.Node + " / " + issue.Kind + " / " + issue.Detail));
            return Ok("文字档漂移 " + issues.Count + " 条：\n" + text);
        }

        /// <summary>令牌源 JSON → 主题资产 SO（《UI 编辑器工具链专项设计》§8.1）：幂等、失败零写盘。</summary>
        private static ToolResult GenerateThemeTokens()
        {
            UIThemeGenerationResult result = UIThemeAssetGenerator.Generate();
            return result.Success ? Ok(result.Summary()) : ToolResult.Failed(result.Summary());
        }
    }
}
