using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using LiteGame.Toolchain;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>外部进程结果（stdout/stderr 已解码；超时/异常都显式返回）。</summary>
    internal sealed class UiPageProcessRun
    {
        public bool Ok;
        public int ExitCode;
        public string Output;
        public string Error;
    }

    /// <summary>
    /// 页面工具的外部进程边界（《UI 编辑器工具链专项设计》§3.6）：python 表助手与 toolchain 生成命令。
    /// 只做执行、解码与 JSON 提取；不解释业务语义（语义在 <see cref="UiPageTableHelper"/> / 登记内核）。
    /// </summary>
    internal static class UiPageToolProcess
    {
        private const int ProcessTimeoutMs = 300000;

        [Serializable]
        internal sealed class HelperRow
        {
            public int id;
            public string lua;
            public string prefab;
            public int layer;
            public bool fullScreen;
        }

        [Serializable]
        internal sealed class HelperResult
        {
            public bool ok;
            public string error;
            public bool dryRun;
            public int id;
            public string[] row;
            public HelperRow[] rows;
            public string backup;
            public string table;
            public string rolledBack;
            public int deleteId;
        }

        internal sealed class HelperRun
        {
            public HelperResult Parsed;
            public string Error;
        }

        public static HelperRun RunHelper(string arguments)
        {
            var run = RunProcess("python",
                Quote(System.IO.Path.Combine(ToolchainManifestLoader.ProjectRoot, UiPageTableHelper.HelperRelativePath))
                + " " + arguments);
            string json = TryExtractJson(run.Output);
            if (json == null || !json.TrimStart().StartsWith("{\"ok\"", StringComparison.Ordinal))
            {
                // 崩溃/缺依赖时没有 JSON：必须把 stderr 交回调用方，不能解析成“ok=false + 空错误”。
                string detail = string.IsNullOrEmpty(run.Error) ? run.Output : run.Error + "\n" + run.Output;
                return new HelperRun { Error = "表行助手输出无法解析：" + (detail ?? string.Empty).Trim() };
            }

            try
            {
                var parsed = JsonUtility.FromJson<HelperResult>(json);
                if (parsed != null) return new HelperRun { Parsed = parsed };
            }
            catch (Exception exception)
            {
                return new HelperRun { Error = "表行助手输出无法解析：" + exception.Message + "\n" + json };
            }

            return new HelperRun { Error = "表行助手输出无法解析：" + json };
        }

        public static UiPageProcessRun RunToolCommand(string toolId)
        {
            ToolchainManifestLoadResult load = ToolchainManifestLoader.Load();
            if (!load.Ok)
                return new UiPageProcessRun { Ok = false, Error = string.Join("; ", load.Errors) };
            ToolchainTool tool = load.Manifest.Tools.FirstOrDefault(item => item.id == toolId);
            if (tool == null || tool.Command.Length == 0)
                return new UiPageProcessRun { Ok = false, Error = "工具清单缺少命令：" + toolId };

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

        public static UiPageProcessRun RunProcess(string fileName, string arguments)
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
                    if (!process.WaitForExit(ProcessTimeoutMs))
                    {
                        try { process.Kill(); } catch { /* best effort */ }
                        return new UiPageProcessRun
                        {
                            Ok = false,
                            Error = "命令超时（5 分钟）",
                            Output = output.ToString(),
                        };
                    }
                    process.WaitForExit();
                    return new UiPageProcessRun
                    {
                        Ok = process.ExitCode == 0,
                        ExitCode = process.ExitCode,
                        Output = output.ToString(),
                        Error = process.ExitCode == 0
                            ? null
                            : (error.Length > 0 ? error.ToString() : "退出码 " + process.ExitCode),
                    };
                }
            }
            catch (Exception exception)
            {
                return new UiPageProcessRun { Ok = false, Error = exception.Message, Output = output.ToString() };
            }
        }

        public static string ExtractJson(string text) => TryExtractJson(text) ?? "{}";

        /// <summary>只接受单一 JSON 对象（首 `{` 到末 `}`）；无对象/空输出返回 null。</summary>
        public static string TryExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : null;
        }

        public static string Quote(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}
