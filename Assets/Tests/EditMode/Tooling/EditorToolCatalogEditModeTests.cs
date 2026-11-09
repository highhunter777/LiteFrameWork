using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteGame.Toolchain;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 工具平台对账：清单（scripts/gate/toolchain.json）与 C# handler 必须 1:1；
    /// 每个工具都能产出页件/窗口/动作适配；文档路径必须存在。GUI 不在此渲染。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class EditorToolCatalogEditModeTests
    {
        [Test]
        public void 清单_可加载且无错误()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            Assert.That(catalog.Manifest, Is.Not.Null, "manifest 为空");
            Assert.That(catalog.Descriptors.Count, Is.GreaterThan(0), "清单没有工具");
            Assert.That(catalog.Errors, Is.Empty, string.Join("\n", catalog.Errors));
        }

        [Test]
        public void 清单_编辑器工具都有Handler且无孤儿()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            Assert.That(catalog.OrphanHandlers, Is.Empty, string.Join(", ", catalog.OrphanHandlers));
            foreach (EditorToolDescriptor descriptor in catalog.Descriptors.Where(item => item.IsEditorKind))
            {
                Assert.That(string.IsNullOrEmpty(descriptor.Error), Is.True, descriptor.Id + ": " + descriptor.Error);
                Assert.That(descriptor.CreatePage, Is.Not.Null, descriptor.Id + " 没有页件工厂");
            }
        }

        [Test]
        public void 清单_迁移动作声明预览输出且可执行()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            foreach (string id in new[] { "baking.body", "baking.muzzle", "baking.grip", "baking.root-motion", "baking.map", "codegen.sim-trig" })
            {
                EditorToolDescriptor descriptor = catalog.Find(id);
                Assert.That(descriptor, Is.Not.Null, id + " 未登记");
                Assert.That(descriptor.SupportsDryRun, Is.True, id + " 未声明 dry-run");
                Assert.That(descriptor.SupportsFileUndo, Is.True, id + " 未声明文件回滚");
                Assert.That(descriptor.OutputPaths.Length, Is.GreaterThan(0), id + " 未声明 outputs");
                Assert.That(descriptor.Execute, Is.Not.Null, id + " 缺少执行委托");
            }
        }

        [Test]
        public void 清单_窗口与动作委托非空()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            foreach (EditorToolDescriptor descriptor in catalog.Descriptors)
            {
                if (descriptor.Kind == ToolchainKinds.Window)
                {
                    Assert.That(descriptor.OpenWindow, Is.Not.Null, descriptor.Id + " 缺少窗口打开委托");
                }
                if (descriptor.Kind == ToolchainKinds.Action)
                {
                    Assert.That(descriptor.Execute, Is.Not.Null, descriptor.Id + " 缺少动作执行委托");
                }
            }
        }

        [Test]
        public void 清单_每个工具都能创建页件()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            foreach (EditorToolDescriptor descriptor in catalog.Descriptors)
            {
                IEditorToolPage page = descriptor.CreatePage != null ? descriptor.CreatePage() : null;
                Assert.That(page, Is.Not.Null, descriptor.Id + " 页件为空");
                Assert.That(page.Title, Is.Not.Null, descriptor.Id + " 页件标题为空");
            }
        }

        [Test]
        public void 清单_工具文档存在()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            string root = ToolchainManifestLoader.ProjectRoot;
            foreach (ToolchainTool tool in catalog.Manifest.Tools)
            {
                Assert.That(File.Exists(Path.Combine(root, tool.docs)), Is.True, tool.id + " 文档不存在: " + tool.docs);
            }
        }

        [Test]
        public void 菜单路径_不得同时是菜单项与父节点()
        {
            var editorAssemblies = new HashSet<string>(StringComparer.Ordinal)
            {
                "LiteGame.Editor",
                "LiteCodeGen.Editor",
                "LiteSim.Core.Editor",
                "DisciplineScanner.Editor",
            };
            var paths = new List<string>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!editorAssemblies.Contains(assembly.GetName().Name)) continue;
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        foreach (MenuItem attribute in method.GetCustomAttributes(typeof(MenuItem), false))
                        {
                            string path = attribute.menuItem;
                            if (string.IsNullOrEmpty(path)) continue;
                            if (path.StartsWith("CONTEXT/") || path.StartsWith("Assets/")) continue;
                            paths.Add(path);
                        }
                    }
                }
            }

            foreach (string parent in paths.Distinct())
            {
                foreach (string child in paths)
                {
                    if (child == parent) continue;
                    Assert.That(child.StartsWith(parent + "/", StringComparison.Ordinal), Is.False,
                        "菜单路径冲突（既是可点击项又是父节点）：" + parent + " <- " + child);
                }
            }
        }

        [Test]
        public void 清单_Id与Handler唯一()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            var ids = catalog.Manifest.Tools.Select(tool => tool.id).ToArray();
            var handlers = catalog.Manifest.Tools
                .Where(tool => !string.IsNullOrEmpty(tool.handler))
                .Select(tool => tool.handler)
                .ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length), "工具 id 重复");
            Assert.That(handlers.Distinct().Count(), Is.EqualTo(handlers.Length), "handler 重复");
        }

        [Test]
        public void 清单_平面契约成立()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            foreach (ToolchainTool tool in catalog.Manifest.Tools)
            {
                Assert.That(ToolchainPlanes.IsValid(tool.plane), Is.True, tool.id + " plane 非法: " + tool.plane);
                foreach (string requirement in tool.Requires)
                {
                    Assert.That(ToolchainPlanes.IsRequirement(requirement), Is.True,
                        tool.id + " requires 非法: " + requirement);
                }

                if (tool.kind == ToolchainKinds.Docs)
                {
                    Assert.That(tool.plane, Is.EqualTo(ToolchainPlanes.None), tool.id + " docs 必须 plane=none");
                }
                if (tool.plane == ToolchainPlanes.Editor)
                {
                    Assert.That(tool.Requires, Is.Empty, tool.id + " plane=editor 不得声明 requires");
                }
                if (tool.plane == ToolchainPlanes.Runtime)
                {
                    Assert.That(tool.Requires.Any(ToolchainPlanes.IsRuntimeRequirement), Is.True,
                        tool.id + " plane=runtime 必须声明 play/player");
                }
                if (tool.plane == ToolchainPlanes.External && tool.kind != ToolchainKinds.Page)
                {
                    Assert.That(tool.Command.Length, Is.GreaterThan(0), tool.id + " plane=external 缺少 command");
                }
            }
        }

        [Test]
        public void 清单_产物形态契约成立()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            int withArtifacts = 0;
            foreach (ToolchainTool tool in catalog.Manifest.Tools)
            {
                foreach (ToolchainOutput output in tool.Outputs)
                {
                    Assert.That(ToolchainOutputKinds.IsValid(output.kind), Is.True,
                        tool.id + " outputs.kind 非法: " + output.kind);
                }
                if (tool.Outputs.Length > 0) withArtifacts++;

                bool writesProject = tool.Writes.Any(scope => scope != "none");
                if (writesProject && !ToolchainKinds.IsInteractive(tool.kind))
                {
                    Assert.That(tool.Outputs.Length, Is.GreaterThan(0), tool.id + " 是写入者但未登记产物形态");
                }
            }

            Assert.That(withArtifacts, Is.GreaterThan(20), "产物登记覆盖面过低");
        }

        [Test]
        public void 清单_用途契约成立()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            var counts = new Dictionary<string, int>();
            foreach (ToolchainTool tool in catalog.Manifest.Tools)
            {
                Assert.That(ToolchainPurposes.IsValid(tool.purpose), Is.True,
                    tool.id + " purpose 非法: " + tool.purpose);
                counts.TryGetValue(tool.purpose, out int count);
                counts[tool.purpose] = count + 1;
            }

            Assert.That(
                counts.Keys,
                Is.EquivalentTo(new[] { ToolchainPurposes.Debug, ToolchainPurposes.Dev, ToolchainPurposes.Delivery }),
                "三条能力线都必须有工具");
        }

        [Test]
        public void 清单_运行内载体只做说明()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            EditorToolDescriptor devhud = catalog.Find("debug.devhud");
            Assert.That(devhud, Is.Not.Null, "debug.devhud 未登记");
            Assert.That(devhud.Kind, Is.EqualTo(ToolchainKinds.Hud));
            Assert.That(devhud.Plane, Is.EqualTo(ToolchainPlanes.Runtime));
            Assert.That(devhud.Tool.Requires, Is.Not.Empty);
            Assert.That(devhud.IsEditorKind, Is.False, "hud 不是编辑器工具，不得走 Hub 适配");
            Assert.That(devhud.Execute, Is.Null, "hud 不得有执行委托");
            Assert.That(devhud.OpenWindow, Is.Null, "hud 不得有窗口委托");
            IEditorToolPage page = devhud.CreatePage();
            Assert.That(page, Is.Not.Null, "hud 必须有说明页件");
            Assert.That(page.Title, Is.Not.Null);
        }
    }
}
