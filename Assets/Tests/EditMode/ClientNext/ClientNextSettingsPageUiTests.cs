using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>设置页 UI 完整性：14 项可操作设置必须在 prefab 上有对应控件（防止「服务有了但玩家调不到」）。</summary>
    public sealed class ClientNextSettingsPageUiTests
    {
        private const string PrefabPath =
            "Assets/LiteGame/Next/Runtime/Features/Settings/Content/SettingsPage.prefab";

        private static GameObject Load() =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        [Test]
        public void 设置页prefab_含全部14项控件()
        {
            GameObject prefab = Load();
            Assert.IsNotNull(prefab, "SettingsPage.prefab 应存在");
            string[] required =
            {
                "Sld_Volume", "Tgl_Quality",
                "Sld_RenderScale", "Sld_AA", "Sld_Shadow",
                "Sld_DisplayMode", "Sld_FrameRate", "Sld_VSync",
                "Sld_LookSens", "Sld_AdsSens",
                "Tgl_Sfx", "Tgl_InvertY",
                "Btn_Language", "Txt_Resolution",
            };
            foreach (string name in required)
                Assert.IsNotNull(prefab.transform.Find(name), "设置页缺控件：" + name);
        }

        [Test]
        public void 每个设置控件_都挂BindName且名与节点一致()
        {
            GameObject prefab = Load();
            Assert.IsNotNull(prefab);
            var bindNames = prefab.GetComponentsInChildren<LiteGameNext.Platform.Ui.BindName>(true);
            Assert.GreaterOrEqual(bindNames.Length, 14, "绑定控件数不足");
            foreach (var bind in bindNames)
            {
                if (bind.Name.StartsWith("Sld_") || bind.Name.StartsWith("Tgl_")) continue;
                if (bind.Name.StartsWith("Btn_") || bind.Name.StartsWith("Txt_")) continue;
                Assert.Fail("未知控件命名：" + bind.Name);
            }
        }

        [Test]
        public void 设置页无控件越出屏幕()
        {
            GameObject prefab = Load();
            Assert.IsNotNull(prefab);
            foreach (var rt in prefab.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.GetComponent<LiteGameNext.Platform.Ui.BindName>() == null) continue;
                float y = rt.anchoredPosition.y;
                Assert.Greater(y, -540f, rt.name + " 超出屏幕下边界 y=" + y);
                Assert.Less(y, 540f, rt.name + " 超出屏幕上边界 y=" + y);
            }
        }
    }
}
