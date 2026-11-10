using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Page;
using LiteGame.Toolchain;
using NUnit.Framework;
using UnityEditor;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-5/P1-6 收口：清单能力必须有工具入口（Hub/CLI），生成物必须有可对账基线。
    /// </summary>
    public sealed class UIToolchainWiringEditModeTests
    {
        private static readonly string[] ManifestToolIds =
        {
            "ui.widget-manifest",
            "ui.page-manifest",
        };

        [Test]
        public void 工具面_清单动作已登记且可执行()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            Assert.That(catalog.Errors, Is.Empty, string.Join("\n", catalog.Errors));

            foreach (string id in ManifestToolIds)
            {
                EditorToolDescriptor descriptor = catalog.Find(id);
                Assert.That(descriptor, Is.Not.Null, id + " 未在 scripts/gate/toolchain.json 登记");
                Assert.That(descriptor.Kind, Is.EqualTo(ToolchainKinds.Action), id + " 必须是 action");
                Assert.That(descriptor.Plane, Is.EqualTo(ToolchainPlanes.Editor), id + " 必须是 plane=editor");
                Assert.That(descriptor.Execute, Is.Not.Null, id + " 缺少 handler");
                Assert.That(descriptor.Tool.Writes.Any(scope => scope != "none"), Is.True,
                    id + " 应声明写入面");
                Assert.That(descriptor.OutputPaths.Length, Is.GreaterThan(0),
                    id + " 是写入者，必须登记产物形态（outputs）");
            }
        }

        [Test]
        public void 起页对账_注入清单路径可落盘()
        {
            const string tempDir = "Assets/Tests/TempUiWiring";
            const string tempManifest = tempDir + "/UIPageManifestWiringTest.json";
            EnsureFolder(tempDir);
            try
            {
                string message = UIPageManifestSync.ReconcileToDisk(tempManifest);
                Assert.That(message, Does.Contain("已对账"), "起页对账应返回可读结论：" + message);
                Assert.That(File.Exists(UIPageManifestPaths.FullPath(tempManifest)), Is.True,
                    "对账结果应写到注入路径");

                UIPageManifest manifest = UIPageManifestStore.Load(tempManifest);
                Assert.That(manifest.Pages.Count, Is.GreaterThan(0), "对账清单不得为空");
            }
            finally
            {
                if (File.Exists(UIPageManifestPaths.FullPath(tempManifest)))
                    AssetDatabase.DeleteAsset(tempManifest);
                DeleteFolder(tempDir);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void DeleteFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
        }
    }
}
