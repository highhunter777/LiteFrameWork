using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LiteGame.Toolchain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// `#combatgeom` 表行的编辑器侧读写边界（烘焙工具箱三件几何工具与地图烘焙的唯一表源入口）： 进程边界 `<c>python scripts/codegen/_combatgeom_row.py</c>`。
    /// </summary>
    public static class CombatGeomTableWriter
    {
        /// <summary>判定几何表源（单行 id=1；13 列与 <see cref="LiteSim.CombatGeometry"/> 一一对应）。</summary>
        public const string CombatGeomTablePath = "Luban/Data/#combatgeom.xlsx";

        /// <summary>单行表主键。</summary>
        public const int RowId = 1;

        private const string HelperRelativePath = "scripts/codegen/_combatgeom_row.py";
        private const int ProcessTimeoutMs = 300000;
        private const string NextStep = "下一步：Luban/gen.bat → python scripts/codegen/toolbox.py build-hash";

        /// <summary>读表源当前数据行（列名 → 数值文本，InvariantCulture）；失败返回 false 并给出原因。</summary>
        public static bool TryReadRow(out Dictionary<string, string> row, out string error)
        {
            row = null;
            error = null;

            string output = RunHelper("--list --id " + RowId.ToString(CultureInfo.InvariantCulture), out string stderr, out error);
            if (output == null) return false;
            if (!TryParsePayload(output, stderr, out JObject payload, out error)) return false;

            var source = payload["row"] as JObject;
            if (source == null)
            {
                error = "表行助手输出缺少 row：" + output.Trim();
                return false;
            }

            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JProperty property in source.Properties())
                parsed[property.Name] = ToInvariantText(property.Value);
            row = parsed;
            return true;
        }

        /// <summary>按列名写数据行（dry-run 缺省；<paramref name="confirm"/> 才落盘）。</summary>
        public static bool TryWrite(string[][] sets, bool confirm, out string report, out string error)
        {
            return TryWrite(sets, confirm, null, out report, out error);
        }

        /// <summary>
        /// 带写盘 scope 的表行写入：scope 存在时以其预览标志为准（预览模式强制 `--dry-run`）； 无 scope 时由 <paramref name="confirm"/> 决定。
        /// </summary>
        public static bool TryWrite(string[][] sets, bool confirm, ToolWriteScope scope,
            out string report, out string error)
        {
            report = null;
            error = null;
            if (!ValidateSets(sets, out error)) return false;

            bool dryRun = scope != null ? scope.DryRun : !confirm;
            string arguments = "--id " + RowId.ToString(CultureInfo.InvariantCulture)
                + " --set " + Quote(string.Join(",", sets.Select(pair => pair[0] + "=" + pair[1])))
                + (dryRun ? string.Empty : " --confirm");

            string output = RunHelper(arguments, out string stderr, out error);
            if (output == null) return false;
            if (!TryParsePayload(output, stderr, out JObject payload, out error)) return false;

            var diff = payload["diff"] as JObject;
            var lines = new List<string>();
            string label = payload.Value<bool?>("dryRun") == true ? "planned：" : "written：";
            if (diff != null)
            {
                foreach (JProperty property in diff.Properties())
                {
                    var values = property.Value as JArray;
                    string before = values != null && values.Count > 0 ? ToInvariantText(values[0]) : "?";
                    string after = values != null && values.Count > 1 ? ToInvariantText(values[1]) : "?";
                    lines.Add(label + CombatGeomTablePath + " · " + property.Name + " " + before + " → " + after);
                }
            }

            if (lines.Count == 0)
                report = "up to date：" + CombatGeomTablePath
                    + "（" + string.Join("/", sets.Select(pair => pair[0])) + " 未变）\n" + NextStep;
            else
                report = string.Join("\n", lines) + "\n" + NextStep;
            return true;
        }

        /// <summary>读入库身位夹具（<see cref="BakeTargets.BodyCcFixturePath"/>，Assets 之外的普通文件）；
        /// 缺失/不可解析返回 false（调用方出 ABORT 报告）。</summary>
        public static bool TryReadBodyCcFixture(out string sourcePrefab, out float radius, out float height, out string error)
        {
            sourcePrefab = null;
            radius = 0f;
            height = 0f;
            error = null;
            string path = FixtureFullPath;
            if (!File.Exists(path))
            {
                error = "缺身位夹具：" + BakeTargets.BodyCcFixturePath + "（先跑身位圆柱烘焙）";
                return false;
            }

            BodyCcFixture fixture;
            try
            {
                fixture = JsonUtility.FromJson<BodyCcFixture>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                error = "身位夹具无法解析：" + BakeTargets.BodyCcFixturePath + "（" + exception.Message + "）";
                return false;
            }

            if (fixture == null || string.IsNullOrEmpty(fixture.sourcePrefab))
            {
                error = "身位夹具内容不完整：" + BakeTargets.BodyCcFixturePath;
                return false;
            }

            sourcePrefab = fixture.sourcePrefab;
            radius = fixture.radius;
            height = fixture.height;
            return true;
        }

        /// <summary>夹具的工程内绝对路径（普通文件 IO，不经 AssetDatabase）。</summary>
        public static string FixtureFullPath =>
            Path.Combine(ToolchainManifestLoader.ProjectRoot, BakeTargets.BodyCcFixturePath);

        /// <summary>按列名取 float（InvariantCulture）；缺列/非数值返回 false。</summary>
        public static bool TryGetFloat(Dictionary<string, string> row, string column, out float value, out string error)
        {
            value = 0f;
            error = null;
            if (row == null || !row.TryGetValue(column, out string text))
            {
                error = "表行缺列：" + column;
                return false;
            }
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = "表列不是合法数值：" + column + "=" + text;
                return false;
            }
            return true;
        }

        /// <summary>按列名取 int（InvariantCulture）；缺列/非整数返回 false。</summary>
        public static bool TryGetInt(Dictionary<string, string> row, string column, out int value, out string error)
        {
            value = 0;
            error = null;
            if (row == null || !row.TryGetValue(column, out string text))
            {
                error = "表行缺列：" + column;
                return false;
            }
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = "表列不是合法整数：" + column + "=" + text;
                return false;
            }
            return true;
        }

        private static bool ValidateSets(string[][] sets, out string error)
        {
            error = null;
            if (sets == null || sets.Length == 0)
            {
                error = "缺少写入列";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] pair in sets)
            {
                if (pair == null || pair.Length != 2)
                {
                    error = "写入列须为 [列名, 值] 二元组";
                    return false;
                }
                if (string.IsNullOrEmpty(pair[0]))
                {
                    error = "写入列名不得为空";
                    return false;
                }
                if (string.IsNullOrEmpty(pair[1]) || pair[1].IndexOf(',') >= 0 || pair[1].IndexOf('=') >= 0)
                {
                    error = "写入值不得为空或含 ','/'='：" + pair[0];
                    return false;
                }
                if (!seen.Add(pair[0]))
                {
                    error = "写入列重复：" + pair[0];
                    return false;
                }
            }
            return true;
        }

        private static bool TryParsePayload(string output, string stderr, out JObject payload, out string error)
        {
            payload = null;
            error = null;
            string json = ExtractJson(output);
            if (json == null)
            {
                string detail = string.IsNullOrEmpty(stderr) ? output : stderr + "\n" + output;
                error = "表行助手输出无法解析：" + (detail ?? string.Empty).Trim();
                return false;
            }

            try
            {
                payload = JObject.Parse(json);
            }
            catch (Exception exception)
            {
                error = "表行助手输出无法解析：" + exception.Message + "\n" + json;
                return false;
            }

            if (payload.Value<bool?>("ok") != true)
            {
                string message = payload.Value<string>("error");
                error = string.IsNullOrEmpty(message) ? "表行助手返回失败（无错误文案）\n" + json : message;
                return false;
            }
            return true;
        }

        /// <summary>进程边界执行：stdout 交回；连 stdout 都没有时把 stderr 原样回传（禁止"空错误"）。</summary>
        private static string RunHelper(string arguments, out string stderr, out string error)
        {
            stderr = null;
            error = null;
            var output = new StringBuilder();
            var errorText = new StringBuilder();
            string script = Path.Combine(ToolchainManifestLoader.ProjectRoot, HelperRelativePath);
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = Quote(script) + (string.IsNullOrEmpty(arguments) ? string.Empty : " " + arguments),
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
                    process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorText.AppendLine(args.Data); };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(ProcessTimeoutMs))
                    {
                        try { process.Kill(); } catch { /* best effort */ }
                        error = "表行助手超时（5 分钟）";
                        return null;
                    }
                    process.WaitForExit();
                }
            }
            catch (Exception exception)
            {
                error = "表行助手启动失败：" + exception.Message;
                return null;
            }

            stderr = errorText.ToString();
            if (output.Length == 0)
            {
                error = "表行助手无输出：" + (string.IsNullOrEmpty(stderr) ? "(stderr 为空)" : stderr.Trim());
                return null;
            }
            return output.ToString();
        }

        /// <summary>只接受单一 JSON 对象（首 `{` 到末 `}`）；无对象/空输出返回 null。</summary>
        private static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : null;
        }

        private static string ToInvariantText(JToken token)
        {
            if (token == null) return string.Empty;
            object value = token is JValue jValue ? jValue.Value : token.ToString();
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        /// <summary>夹具文件形态（与 <c>BodyCylinderBaker</c> 的写出格式一一对应）。</summary>
        [Serializable]
        private sealed class BodyCcFixture
        {
            public string sourcePrefab;
            public float radius;
            public float height;
        }
    }
}
