using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteNet;

namespace LiteGame.Toolchain
{
    /// <summary>
    /// IL2CPP 符号归档（T0-2）：把构建产物里的调试符号（<c>*.pdb</c>——GameAssembly.pdb 等）按
    /// buildHash 归档到 <c>Builds/symbols/</c>，分发包不带符号。
    /// 符号与产物脱节 = 崩溃堆栈不可解——归档目录即固定查找路径（&lt;hash&gt;-&lt;时间戳&gt;/manifest.json 对账）。
    /// 纯文件操作（EditMode 可测）；构建钩子见 <see cref="SymbolArchivePostBuild"/>。
    /// </summary>
    public static class SymbolArchiver
    {
        /// <summary>归档根目录（仓库相对；已被 git 忽略——符号不入库，随机构器留存）。</summary>
        public const string DefaultArchiveRoot = "Builds/symbols";

        private static readonly string[] SymbolExtensions = { ".pdb", ".dbg", ".sym" };

        /// <summary>收集构建产物内全部符号文件（递归；不存在/空目录 = 空表）。</summary>
        public static IReadOnlyList<string> CollectSymbolSources(string buildOutputPath)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(buildOutputPath) || !Directory.Exists(buildOutputPath)) return found;

            foreach (string file in Directory.EnumerateFiles(buildOutputPath, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file)?.ToLowerInvariant();
                for (int i = 0; i < SymbolExtensions.Length; i++)
                {
                    if (string.Equals(ext, SymbolExtensions[i], StringComparison.Ordinal))
                    {
                        found.Add(file);
                        break;
                    }
                }
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        /// <summary>
        /// 归档：符号文件按产物内相对路径拷贝到 <c>&lt;archiveRoot&gt;/&lt;buildHash&gt;-&lt;时间戳&gt;/</c>
        /// 并写 manifest.json（文件/大小/SHA256）。无符号文件返回 null（非 IL2CPP/无符号产物是正常形态）。
        /// </summary>
        public static string Archive(string buildOutputPath, string buildHash, string archiveRoot = null)
        {
            IReadOnlyList<string> sources = CollectSymbolSources(buildOutputPath);
            if (sources.Count == 0) return null;

            string root = string.IsNullOrEmpty(archiveRoot) ? DefaultArchiveRoot : archiveRoot;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(root, Sanitize(buildHash) + "-" + stamp);
            Directory.CreateDirectory(target);

            var entries = new List<string>(sources.Count);
            try
            {
                foreach (string source in sources)
                {
                    string relative = Path.GetRelativePath(buildOutputPath, source);
                    string destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, overwrite: true);
                    entries.Add(relative);
                }
                File.WriteAllText(Path.Combine(target, "manifest.json"),
                    BuildManifest(buildHash, buildOutputPath, target, entries), new UTF8Encoding(false));
            }
            catch
            {
                try { Directory.Delete(target, true); } catch { }
                throw;
            }
            return target;
        }

        private static string BuildManifest(string buildHash, string buildOutputPath, string target,
            List<string> relativeFiles)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            AppendField(sb, "schemaVersion", "1", first: true);
            AppendField(sb, "buildHash", buildHash);
            AppendField(sb, "archivedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            AppendField(sb, "buildOutputPath", buildOutputPath);
            sb.Append(",\"files\":[");
            for (int i = 0; i < relativeFiles.Count; i++)
            {
                string path = Path.Combine(target, relativeFiles[i]);
                var info = new FileInfo(path);
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":");
                AppendEscaped(sb, relativeFiles[i]);
                sb.Append(",\"bytes\":").Append(info.Length.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"sha256\":\"").Append(Sha256(path)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ':') chars[i] = '_';
            return new string(chars);
        }

        private static void AppendField(StringBuilder sb, string name, string value, bool first = false)
        {
            if (!first) sb.Append(',');
            AppendEscaped(sb, name);
            sb.Append(':');
            AppendEscaped(sb, value);
        }

        private static void AppendEscaped(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>
    /// 构建后钩子：产物符号自动归档。归档失败只告警——符号缺失会在排查崩溃时暴露，
    /// 不应让出包失败（诊断设施不得反噬构建链）。
    /// </summary>
    public sealed class SymbolArchivePostBuild : UnityEditor.Build.IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 100; } }

        public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            string buildOutputPath = report.summary.outputPath;
            try
            {
                string directory = SymbolArchiver.Archive(buildOutputPath, BuildHash.Value);
                UnityEngine.Debug.Log(directory == null
                    ? "[Symbols] 构建产物无符号文件，跳过归档"
                    : $"[Symbols] 构建符号已归档：{directory}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Symbols] 符号归档失败（构建不受影响）：" + ex.Message);
            }
        }
    }
}
