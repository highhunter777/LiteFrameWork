using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Toolchain
{
    public sealed class EditorToolCatalog
    {
        public ToolchainManifest Manifest;
        public string ManifestPath;
        public readonly List<EditorToolDescriptor> Descriptors = new List<EditorToolDescriptor>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> OrphanHandlers = new List<string>();

        private readonly Dictionary<string, EditorToolDescriptor> _byId =
            new Dictionary<string, EditorToolDescriptor>(StringComparer.Ordinal);

        public static EditorToolCatalog Load()
        {
            var catalog = new EditorToolCatalog();
            ToolchainManifestLoadResult result = ToolchainManifestLoader.Load();
            catalog.ManifestPath = result.Path;
            catalog.Manifest = result.Manifest;
            catalog.Errors.AddRange(result.Errors);
            if (catalog.Manifest == null) return catalog;

            var registry = new EditorToolRegistry();
            try
            {
                EditorToolHandlers.RegisterAll(registry);
            }
            catch (Exception exception)
            {
                catalog.Errors.Add("handler 注册失败：" + exception.Message);
                return catalog;
            }

            var manifestHandlers = new HashSet<string>(
                catalog.Manifest.Tools.Where(tool => !string.IsNullOrEmpty(tool.handler)).Select(tool => tool.handler),
                StringComparer.Ordinal);
            foreach (string handler in registry.RegisteredHandlers)
            {
                if (!manifestHandlers.Contains(handler))
                {
                    catalog.OrphanHandlers.Add(handler);
                }
            }
            foreach (string orphan in catalog.OrphanHandlers)
            {
                catalog.Errors.Add("handler 未在清单登记：" + orphan);
            }

            foreach (ToolchainTool tool in catalog.Manifest.Tools)
            {
                var descriptor = new EditorToolDescriptor(tool);
                switch (tool.kind)
                {
                    case ToolchainKinds.Page:
                        RegisterPage(registry, descriptor);
                        break;
                    case ToolchainKinds.Window:
                        RegisterWindow(registry, descriptor);
                        break;
                    case ToolchainKinds.Action:
                        RegisterAction(registry, descriptor);
                        break;
                    case ToolchainKinds.Docs:
                        descriptor.CreatePage = () => new DocsToolPage(descriptor);
                        break;
                    case ToolchainKinds.Cli:
                    case ToolchainKinds.Gate:
                    case ToolchainKinds.Build:
                    case ToolchainKinds.Content:
                        descriptor.CreatePage = () => new CliToolPage(descriptor);
                        break;
                    default:
                        descriptor.Error = "未知工具类型：" + tool.kind;
                        descriptor.CreatePage = () => new ErrorToolPage(descriptor.Title, descriptor.Error);
                        break;
                }

                if (!string.IsNullOrEmpty(descriptor.Error))
                {
                    catalog.Errors.Add("工具 " + descriptor.Id + "：" + descriptor.Error);
                    descriptor.CreatePage = () => new ErrorToolPage(descriptor.Title, descriptor.Error);
                }

                catalog.Descriptors.Add(descriptor);
                if (!catalog._byId.ContainsKey(descriptor.Id))
                {
                    catalog._byId.Add(descriptor.Id, descriptor);
                }
            }
            return catalog;
        }

        private static void RegisterPage(EditorToolRegistry registry, EditorToolDescriptor descriptor)
        {
            Func<IEditorToolPage> factory;
            if (string.IsNullOrEmpty(descriptor.Tool.handler))
            {
                descriptor.Error = "缺少 handler";
            }
            else if (!registry.TryCreatePage(descriptor.Tool.handler, out factory))
            {
                descriptor.Error = "handler 未注册：" + descriptor.Tool.handler;
            }
            else
            {
                descriptor.CreatePage = factory;
            }
        }

        private static void RegisterWindow(EditorToolRegistry registry, EditorToolDescriptor descriptor)
        {
            Action open;
            if (string.IsNullOrEmpty(descriptor.Tool.handler))
            {
                descriptor.Error = "缺少 handler";
            }
            else if (!registry.TryOpenWindow(descriptor.Tool.handler, out open))
            {
                descriptor.Error = "handler 未注册：" + descriptor.Tool.handler;
            }
            else
            {
                descriptor.OpenWindow = open;
                descriptor.CreatePage = () => new WindowLauncherPage(descriptor);
            }
        }

        private static void RegisterAction(EditorToolRegistry registry, EditorToolDescriptor descriptor)
        {
            Func<ToolRequest, ToolResult> execute;
            if (string.IsNullOrEmpty(descriptor.Tool.handler))
            {
                descriptor.Error = "缺少 handler";
            }
            else if (!registry.TryExecute(descriptor.Tool.handler, out execute))
            {
                descriptor.Error = "handler 未注册：" + descriptor.Tool.handler;
            }
            else
            {
                descriptor.Execute = execute;
                descriptor.CreatePage = () => new ActionToolPage(descriptor);
            }
        }

        public EditorToolDescriptor Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EditorToolDescriptor descriptor;
            return _byId.TryGetValue(id, out descriptor) ? descriptor : null;
        }

        public IEnumerable<ToolchainCategory> Categories()
        {
            return Manifest == null
                ? Enumerable.Empty<ToolchainCategory>()
                : Manifest.Categories.OrderBy(category => category.SortOrder).ThenBy(category => category.title);
        }

        public IEnumerable<EditorToolDescriptor> ToolsInCategory(string categoryId)
        {
            return Descriptors
                .Where(descriptor => descriptor.Tool.category == categoryId)
                .OrderBy(descriptor => descriptor.Tool.SortOrder)
                .ThenBy(descriptor => descriptor.Tool.title);
        }

        public EditorToolDescriptor FirstTool()
        {
            ToolchainCategory category = Categories().FirstOrDefault();
            if (category == null) return null;
            return ToolsInCategory(category.id).FirstOrDefault() ?? Descriptors.FirstOrDefault();
        }
    }
}
