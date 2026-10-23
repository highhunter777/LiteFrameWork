using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 新建页向导的引擎面（《UI编辑器工具专项设计》§8.3）：脚手架模板、prefab/Lua 生成、
    /// tbuiform 行追加与 Luban 重生成。窗口只做输入与结果呈现；本类可脱离窗口直测。
    ///
    /// 表源是唯一数据面：写行由 `scripts/codegen/_uiform_row.py` 完成（校验/备份/回滚），
    /// 生成物由清单里的 `codegen.luban` 命令重生成。任何一步失败都显式返回，不静默成功。
    /// </summary>
    public static class UiPageScaffold
    {
        public const string TemplateDir = "Assets/LiteGame/Editor/UiTools/Templates";
        public const string TemplatePath = TemplateDir + "/Tmp_Page.prefab";
        public const string LuaDir = "Assets/LiteGame/Lua/UI";
        public const string TableRelativePath = "Luban/Data/#uiform.xlsx";
        public const string HelperRelativePath = "scripts/codegen/_uiform_row.py";
        public const string LubanToolId = "codegen.luban";
        public const int ReservedIdFloor = 201;

        public enum Step
        {
            Validate,
            CreatePrefab,
            CreateLua,
            WriteTable,
            GenerateLuban,
            OpenStage,
        }

        public sealed class Result
        {
            public bool Ok;
            public string Error;
            public string PrefabPath;
            public string LuaPath;
            public string BackupPath;
            public Step FailedAt;
            public readonly List<string> Log = new List<string>();
        }

        /// <summary>tbuiform 行投影（页面选择器用；字段名与 Python 助手 JSON 一一对应）。</summary>
        [Serializable]
        public sealed class PageRow
        {
            public int id;
            public string lua;
            public string prefab;
            public int layer;
            public bool fullScreen;

            public string PrefabPath => "Assets/" + prefab;
            public string FileName => string.IsNullOrEmpty(prefab)
                ? string.Empty
                : prefab.Substring(prefab.LastIndexOf('/') + 1);
        }

        /// <summary>已登记页面清单（按表顺序；读取失败返回空并记警告，不伪造条目）。</summary>
        public static List<PageRow> ListPages()
        {
            if (TryListPages(out List<PageRow> rows, out string error)) return rows;
            Debug.LogWarning("[UI 制作] " + error);
            return new List<PageRow>();
        }

        /// <summary>
        /// 读取页面清单并区分「读取失败」与「空表」（对账/清单工具用：失败必须显式，不得伪造空表）。
        /// </summary>
        public static bool TryListPages(out List<PageRow> rows, out string error)
        {
            var run = RunHelper("--list");
            if (run.Parsed == null || !run.Parsed.ok)
            {
                rows = null;
                error = "页面清单读取失败：" + (run.Parsed?.error ?? run.Error);
                return false;
            }
            rows = run.Parsed.rows != null ? new List<PageRow>(run.Parsed.rows) : new List<PageRow>();
            error = null;
            return true;
        }

        /// <summary>
        /// 起页后对账页面清单并落盘（《UI 编辑器工具链专项设计》§8.5）：身份字段以 `tbuiform` 为准、
        /// 维护字段保留。失败不回滚页面——清单是注册表、可重生成；结论进结果日志，不静默。
        /// </summary>
        internal static string ReconcilePageManifest(string outputPath = null)
        {
            try
            {
                UiPageManifestSyncResult sync = UiPageManifestSync.SyncToDisk(outputPath);
                if (!sync.Ok)
                {
                    return "页面清单对账失败：" + sync.ErrorText.Replace("\n", "；")
                        + "（页面已创建，可重跑 ui.page-manifest）";
                }
                int pages = sync.Manifest?.Pages?.Count ?? 0;
                int forms = sync.Manifest?.Pages?.Sum(page => page.Forms?.Count ?? 0) ?? 0;
                return "页面清单已对账：" + pages + " 页 / " + forms + " 行登记";
            }
            catch (Exception exception)
            {
                return "页面清单对账异常：" + exception.Message + "（页面已创建，可重跑 ui.page-manifest）";
            }
        }

        /// <summary>建议 id：业务页最大 id + 1（跳过系统面/静态先例保留区）。</summary>
        public static int SuggestId()
        {
            var output = RunHelper("--suggest");
            return output.Parsed != null && output.Parsed.ok ? output.Parsed.id : -1;
        }

        /// <summary>
        /// 一键起页内核（prefab + Lua + tbuiform + Luban）。**P2-2 接线**：向导退役后暂无窗口消费者，
        /// P2-2 把它拆成美术起页（仅 prefab）与工程登记两段；当前消费者＝测试与工具脚本。
        /// </summary>
        public static Result Create(string pageName, int id, int layer, bool fullScreen, bool openStage = true)
        {
            var result = new Result();
            try
            {
                if (!IsValidPageName(pageName))
                    return Fail(result, Step.Validate, "页面名须为 Page_<PascalCase>（ASCII 字母数字）");
                if (id <= 0)
                    return Fail(result, Step.Validate, "id 必须为正整数");

                string prefabPath = UiValidator.ScreensDir + "/" + pageName + ".prefab";
                string luaPath = LuaDir + "/" + pageName + ".lua";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null
                    || File.Exists(Path.Combine(ToolchainManifestLoader.ProjectRoot, prefabPath)))
                    return Fail(result, Step.Validate, "目标 prefab 已存在：" + prefabPath);
                if (File.Exists(Path.Combine(ToolchainManifestLoader.ProjectRoot, luaPath)))
                    return Fail(result, Step.Validate, "目标 Lua 已存在：" + luaPath);

                var preflight = RunHelper(
                    $"--page {Quote(pageName)} --id {id} --layer {layer} " +
                    $"--full-screen {(fullScreen ? "true" : "false")} --check");
                if (preflight.Parsed == null || !preflight.Parsed.ok)
                    return Fail(result, Step.Validate, preflight.Parsed?.error ?? preflight.Error);

                EnsureTemplate();
                if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) == null)
                    return Fail(result, Step.CreatePrefab, "脚手架模板缺失且无法创建：" + TemplatePath);

                // 复制资产而非实例化：页面产物必须是自包含 prefab，不能变成编辑器模板的 variant。
                if (!AssetDatabase.CopyAsset(TemplatePath, prefabPath))
                    return Fail(result, Step.CreatePrefab, "复制脚手架模板失败：" + TemplatePath);
                AssetDatabase.ImportAsset(prefabPath);
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    contents.name = pageName;
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                AssetDatabase.ImportAsset(prefabPath);
                result.PrefabPath = prefabPath;
                result.Log.Add("prefab：" + prefabPath);

                EnsureFolder(LuaDir);
                File.WriteAllText(
                    Path.Combine(ToolchainManifestLoader.ProjectRoot, luaPath),
                    BuildLuaSkeleton(pageName),
                    new UTF8Encoding(false));
                AssetDatabase.ImportAsset(luaPath);
                result.LuaPath = luaPath;
                result.Log.Add("Lua：" + luaPath);

                var write = RunHelper(
                    $"--page {Quote(pageName)} --id {id} --layer {layer} " +
                    $"--full-screen {(fullScreen ? "true" : "false")} --confirm");
                if (write.Parsed == null || !write.Parsed.ok)
                {
                    DeleteCreated(prefabPath, luaPath);
                    return Fail(result, Step.WriteTable, write.Parsed?.error ?? write.Error);
                }
                result.BackupPath = write.Parsed.backup;
                result.Log.Add("tbuiform 行已写入（备份：" + write.Parsed.backup + "）");

                var luban = RunToolCommand(LubanToolId);
                if (!luban.Ok)
                {
                    // 表行已写但生成失败：回滚表；prefab/Lua 保留给作者排查，明确告知未登记。
                    var rollback = string.IsNullOrEmpty(result.BackupPath)
                        ? null
                        : RunHelper("--rollback " + Quote(result.BackupPath));
                    result.Log.Add(rollback != null && rollback.Parsed != null && rollback.Parsed.ok
                        ? "Luban 失败：表行已回滚（prefab/Lua 保留，未登记）"
                        : "Luban 失败：表行回滚未完成，请按备份手动恢复：" + result.BackupPath);
                    return Fail(result, Step.GenerateLuban,
                        "Luban 重生成失败：" + luban.Error + "\n" + (luban.Output ?? string.Empty));
                }
                result.Log.Add("Luban 重生成完成");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                result.Log.Add(ReconcilePageManifest());
                if (!openStage)
                {
                    result.Ok = true;
                    result.Log.Add("未打开 Prefab Stage（测试形态）");
                    return result;
                }
                var stage = PrefabStageUtility.OpenPrefab(prefabPath);
                if (stage == null)
                {
                    result.Ok = true;
                    result.Log.Add("Prefab Stage 打开失败，请手动打开：" + prefabPath);
                    return result;
                }
                result.Ok = true;
                result.Log.Add("Prefab Stage 已打开");
                return result;
            }
            catch (Exception exception)
            {
                return Fail(result, Step.Validate, exception.Message);
            }
        }

        public static bool IsValidPageName(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return false;
            if (!UiBindingRename.TryDecomposeName(pageName, out string prefix, out string semantic)) return false;
            if (prefix != "Page_") return false;
            return UiBindingRename.TryComposeName(prefix, semantic, out string composed, out _)
                   && composed == pageName;
        }

        /// <summary>确保编辑器脚手架模板存在（结构性根，不是运行期视觉资产）。</summary>
        public static void EnsureTemplate()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) != null) return;
            EnsureFolder(TemplateDir);

            var root = new GameObject("Tmp_Page",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            try
            {
                var rt = root.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = false;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                var safe = new GameObject("Cot_SafeArea", typeof(RectTransform), typeof(SafeAreaReceiver));
                safe.transform.SetParent(root.transform, false);
                var safeRt = safe.GetComponent<RectTransform>();
                safeRt.anchorMin = Vector2.zero;
                safeRt.anchorMax = Vector2.one;
                safeRt.offsetMin = Vector2.zero;
                safeRt.offsetMax = Vector2.zero;

                PrefabUtility.SaveAsPrefabAsset(root, TemplatePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static string BuildLuaSkeleton(string pageName)
        {
            return
                $"-- UI/{pageName}.lua：页面骨架（新建页向导生成）\n" +
                "local class = require(\"Core.class\")\n\n" +
                $"local M = class(\"{pageName}\")\n\n" +
                "function M:ctor()\n" +
                $"    self._viewName = \"{pageName}\"\n" +
                "end\n\n" +
                "function M:OnShow(data)\n" +
                "end\n\n" +
                "function M:OnHide()\n" +
                "end\n\n" +
                "return M\n";
        }

        private static Result Fail(Result result, Step step, string error)
        {
            result.Ok = false;
            result.FailedAt = step;
            result.Error = error;
            result.Log.Add("失败：" + error);
            return result;
        }

        private static void DeleteCreated(string prefabPath, string luaPath)
        {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(luaPath)) AssetDatabase.DeleteAsset(luaPath);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---------------- 外部命令 ----------------

        private sealed class HelperResult
        {
            public bool ok;
            public string error;
            public bool dryRun;
            public int id;
            public string[] row;
            public PageRow[] rows;
            public string backup;
            public string table;
            public string rolledBack;
        }

        private sealed class HelperRun
        {
            public HelperResult Parsed;
            public string Error;
        }

        private static HelperRun RunHelper(string arguments)
        {
            var run = RunProcess("python", Quote(Path.Combine(ToolchainManifestLoader.ProjectRoot, HelperRelativePath)) + " " + arguments);
            if (!run.Ok)
                return new HelperRun { Error = run.Error ?? run.Output };
            try
            {
                var parsed = JsonUtility.FromJson<HelperResult>(ExtractJson(run.Output));
                if (parsed == null)
                    return new HelperRun { Error = "表行助手输出无法解析：" + run.Output };
                return new HelperRun { Parsed = parsed };
            }
            catch (Exception exception)
            {
                return new HelperRun { Error = "表行助手输出无法解析：" + exception.Message + "\n" + run.Output };
            }
        }

        private sealed class ProcessRun
        {
            public bool Ok;
            public int ExitCode;
            public string Output;
            public string Error;
        }

        private static ProcessRun RunToolCommand(string toolId)
        {
            var load = ToolchainManifestLoader.Load();
            if (!load.Ok)
                return new ProcessRun { Ok = false, Error = string.Join("; ", load.Errors) };
            var tool = load.Manifest.Tools.FirstOrDefault(item => item.id == toolId);
            if (tool == null || tool.Command.Length == 0)
                return new ProcessRun { Ok = false, Error = "工具清单缺少命令：" + toolId };

            string fileName = tool.Command[0];
            string arguments = string.Join(" ", tool.Command.Skip(1).Select(Quote));
            if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                arguments = "/c " + Quote(fileName) + (arguments.Length > 0 ? " " + arguments : string.Empty);
                fileName = "cmd";
            }
            return RunProcess(fileName, arguments);
        }

        private static ProcessRun RunProcess(string fileName, string arguments)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = ToolchainManifestLoader.ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                start.Environment["PYTHONUTF8"] = "1";

                using (var process = new Process { StartInfo = start })
                {
                    process.OutputDataReceived += (_, args) => { if (args.Data != null) output.AppendLine(args.Data); };
                    process.ErrorDataReceived += (_, args) => { if (args.Data != null) error.AppendLine(args.Data); };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(300000))
                    {
                        try { process.Kill(); } catch { /* best effort */ }
                        return new ProcessRun { Ok = false, Error = "命令超时（5 分钟）", Output = output.ToString() };
                    }
                    process.WaitForExit();
                    bool ok = process.ExitCode == 0;
                    return new ProcessRun
                    {
                        Ok = ok,
                        ExitCode = process.ExitCode,
                        Output = output.ToString(),
                        Error = ok ? null : (error.Length > 0 ? error.ToString() : "退出码 " + process.ExitCode),
                    };
                }
            }
            catch (Exception exception)
            {
                return new ProcessRun { Ok = false, Error = exception.Message, Output = output.ToString() };
            }
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "{}";
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : "{}";
        }

        private static string Quote(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}
