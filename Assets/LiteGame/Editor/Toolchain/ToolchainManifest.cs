using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LiteGame.Toolchain
{
    public static class ToolchainKinds
    {
        public const string Page = "page";
        public const string Window = "window";
        public const string Action = "action";
        public const string Cli = "cli";
        public const string Gate = "gate";
        public const string Build = "build";
        public const string Content = "content";
        public const string Docs = "docs";

        public static bool IsEditor(string kind)
        {
            return kind == Page || kind == Window || kind == Action;
        }

        public static bool NeedsCommand(string kind)
        {
            return kind == Cli || kind == Gate || kind == Build || kind == Content;
        }
    }

    [Serializable]
    public sealed class ToolchainCategory
    {
        public string id;
        public string title;
        public int order;

        public int SortOrder { get { return order == 0 ? 1000 : order; } }
    }

    [Serializable]
    public sealed class ToolchainSurface
    {
        public string id;
        public string kind;
        public string path;
        public string name;
        public string script;
        public string runner;
        public bool required;
    }

    [Serializable]
    public sealed class ToolchainOutput
    {
        public string path;
        public string kind;
    }

    [Serializable]
    public sealed class ToolchainSafety
    {
        public string dryRun;
        public string undo;
        public string confirmation;
    }

    [Serializable]
    public sealed class ToolchainTool
    {
        public string id;
        public string title;
        public string category;
        public string kind;
        public string handler;
        public string summary;
        public string docs;
        public string menu;
        public string[] writes;
        public bool destructive;
        public int order;
        public string[] command;
        public ToolchainOutput[] outputs;
        public ToolchainSafety safety;

        public int SortOrder { get { return order == 0 ? 1000 : order; } }

        public string[] Writes { get { return writes ?? Array.Empty<string>(); } }

        public string[] Command { get { return command ?? Array.Empty<string>(); } }

        public ToolchainOutput[] Outputs { get { return outputs ?? Array.Empty<ToolchainOutput>(); } }

        public bool SupportsDryRun
        {
            get { return safety != null && safety.dryRun == "supported"; }
        }

        public bool SupportsFileUndo
        {
            get { return safety != null && safety.undo == "file"; }
        }
    }

    [Serializable]
    public sealed class ToolchainManifest
    {
        public int schemaVersion;
        public ToolchainCategory[] categories;
        public ToolchainTool[] tools;
        public ToolchainSurface[] surfaces;

        public ToolchainCategory[] Categories { get { return categories ?? Array.Empty<ToolchainCategory>(); } }

        public ToolchainTool[] Tools { get { return tools ?? Array.Empty<ToolchainTool>(); } }

        public ToolchainSurface[] Surfaces { get { return surfaces ?? Array.Empty<ToolchainSurface>(); } }
    }

    public sealed class ToolchainManifestLoadResult
    {
        public ToolchainManifest Manifest;
        public string Path;
        public readonly List<string> Errors = new List<string>();

        public bool Ok { get { return Errors.Count == 0 && Manifest != null; } }
    }

    public static class ToolchainManifestLoader
    {
        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        public static string ManifestPath
        {
            get { return Path.Combine(ProjectRoot, "scripts", "gate", "toolchain.json"); }
        }

        public static ToolchainManifestLoadResult Load(string path = null)
        {
            string manifestPath = string.IsNullOrEmpty(path) ? ManifestPath : path;
            var result = new ToolchainManifestLoadResult { Path = manifestPath };
            if (!File.Exists(manifestPath))
            {
                result.Errors.Add("清单不存在：" + manifestPath);
                return result;
            }

            try
            {
                string json = File.ReadAllText(manifestPath, Encoding.UTF8);
                result.Manifest = JsonUtility.FromJson<ToolchainManifest>(json);
            }
            catch (Exception exception)
            {
                result.Errors.Add("清单读取失败：" + exception.Message);
                return result;
            }

            if (result.Manifest == null)
            {
                result.Errors.Add("清单反序列化为空：" + manifestPath);
                return result;
            }

            Validate(result, ProjectRoot);
            return result;
        }

        private static void Validate(ToolchainManifestLoadResult result, string projectRoot)
        {
            ToolchainManifest manifest = result.Manifest;
            if (manifest.schemaVersion != 3)
            {
                result.Errors.Add("schemaVersion 必须为 3（当前 " + manifest.schemaVersion + "）");
            }

            var categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (ToolchainCategory category in manifest.Categories)
            {
                if (category == null || string.IsNullOrEmpty(category.id) || string.IsNullOrEmpty(category.title))
                {
                    result.Errors.Add("分类缺少 id/title");
                    continue;
                }
                if (!categories.Add(category.id))
                {
                    result.Errors.Add("分类 id 重复：" + category.id);
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var handlers = new HashSet<string>(StringComparer.Ordinal);
            var menus = new HashSet<string>(StringComparer.Ordinal);
            foreach (ToolchainTool tool in manifest.Tools)
            {
                if (tool == null || string.IsNullOrEmpty(tool.id))
                {
                    result.Errors.Add("工具缺少 id");
                    continue;
                }
                if (!ids.Add(tool.id))
                {
                    result.Errors.Add("工具 id 重复：" + tool.id);
                }
                if (string.IsNullOrEmpty(tool.title) || string.IsNullOrEmpty(tool.summary) || string.IsNullOrEmpty(tool.docs))
                {
                    result.Errors.Add("工具缺少 title/summary/docs：" + tool.id);
                }
                if (!string.IsNullOrEmpty(tool.category) && !categories.Contains(tool.category))
                {
                    result.Errors.Add("工具分类未声明：" + tool.id + " -> " + tool.category);
                }
                if (!string.IsNullOrEmpty(tool.handler) && !handlers.Add(tool.handler))
                {
                    result.Errors.Add("handler 重复：" + tool.handler);
                }
                if (!string.IsNullOrEmpty(tool.menu) && !menus.Add(tool.menu))
                {
                    result.Errors.Add("menu 重复：" + tool.menu);
                }
                if (ToolchainKinds.IsEditor(tool.kind) && string.IsNullOrEmpty(tool.handler))
                {
                    result.Errors.Add("编辑器工具缺少 handler：" + tool.id);
                }
                if (ToolchainKinds.NeedsCommand(tool.kind) && tool.Command.Length == 0)
                {
                    result.Errors.Add("CLI 工具缺少 command：" + tool.id);
                }
                if (!string.IsNullOrEmpty(tool.docs) && !File.Exists(Path.Combine(projectRoot, tool.docs)))
                {
                    result.Errors.Add("工具文档不存在：" + tool.id + " -> " + tool.docs);
                }
                foreach (ToolchainOutput output in tool.Outputs)
                {
                    if (output == null || string.IsNullOrEmpty(output.path) || Path.IsPathRooted(output.path))
                    {
                        result.Errors.Add("工具 outputs 路径非法：" + tool.id);
                    }
                }
                if (tool.safety != null)
                {
                    if (tool.safety.dryRun != null && tool.safety.dryRun != "supported" && tool.safety.dryRun != "unsupported")
                    {
                        result.Errors.Add("工具 safety.dryRun 非法：" + tool.id);
                    }
                    if (tool.safety.undo != null && tool.safety.undo != "file" && tool.safety.undo != "asset" && tool.safety.undo != "none")
                    {
                        result.Errors.Add("工具 safety.undo 非法：" + tool.id);
                    }
                    if (tool.safety.confirmation != null && tool.safety.confirmation != "none" && tool.safety.confirmation != "required")
                    {
                        result.Errors.Add("工具 safety.confirmation 非法：" + tool.id);
                    }
                }
            }

            foreach (ToolchainSurface surface in manifest.Surfaces)
            {
                if (surface == null || string.IsNullOrEmpty(surface.id))
                {
                    result.Errors.Add("注册面缺少 id");
                }
            }
        }
    }
}
