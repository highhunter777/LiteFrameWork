using System.Linq;
using LiteGame.Editor.UiTools.Preview;
using LiteGame.Editor.UiTools.Workbench;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 工作台设备档接线（P2-3 B3 / AC4）：偏好键往返（EditorPrefs/内存两实现）＋下拉选项面（档案单源，
    /// 「跟随预览」缺省项；纯函数断言不依赖 IMGUI 渲染）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiWorkbenchDeviceEditModeTests
    {
        [Test]
        public void 偏好_内存实现设备档键往返()
        {
            var settings = new UiWorkbenchSettingsMemory();
            Assert.AreEqual(string.Empty, settings.PreviewDeviceProfileId, "缺省＝空（跟随预览）");
            settings.PreviewDeviceProfileId = "phone.19-5-9";
            Assert.AreEqual("phone.19-5-9", settings.PreviewDeviceProfileId);
        }

        [Test]
        public void 偏好_EditorPrefs实现往返并回落默认()
        {
            var settings = new UiWorkbenchSettings();
            string original = UnityEditor.EditorPrefs.GetString(UiWorkbenchSettings.PreviewDeviceKey, string.Empty);
            try
            {
                settings.PreviewDeviceProfileId = "tablet.4-3";
                Assert.AreEqual("tablet.4-3",
                    UnityEditor.EditorPrefs.GetString(UiWorkbenchSettings.PreviewDeviceKey), "键名常量单源");
                Assert.AreEqual("tablet.4-3", settings.PreviewDeviceProfileId);
            }
            finally
            {
                UnityEditor.EditorPrefs.SetString(UiWorkbenchSettings.PreviewDeviceKey, original);
            }
        }

        [Test]
        public void 选项_档案驱动且首项为跟随预览()
        {
            UiDeviceProfileDocument doc = UiDeviceProfileCatalog.Load();
            var options = UiWorkbenchWindow.DeviceProfileOptions(doc);
            Assert.IsNotEmpty(options, "选项集非空");
            Assert.AreEqual(string.Empty, options[0], "首项＝空（跟随预览缺省档）");
            Assert.AreEqual(doc.Profiles.Count(p => p.InMatrix) + 1, options.Count,
                "其余＝入矩阵档（档案单源，不复制第二份）");
            Assert.IsTrue(options.Contains("pc.16-9"), "钦定档在选项中");
        }
    }
}
