using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteGame;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UIMain 退役守卫（《UI 系统 UIMain 退役》方案 AC1/AC2/AC3）：
    /// 页面清单不再含样例页、LuaKeys 不再产 UIMain、启动锚迁到 Page_Login、main.lua 去样例页、
    /// 测试载体归位到 Tmp_UiTestPage/Tests.UiTestPage；常驻壳判据仍拒 UIMain。
    /// 先红：实施前这些断言全部指向目标态，故失败于“目标态尚未落地”，不是编译/环境错误。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiUimainRetirementEditModeTests
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static string ReadProjectFile(string relativePath)
            => File.ReadAllText(Path.Combine(ProjectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        [Test]
        public void 页面清单_无UIMain且为11页12行()
        {
            UiPageManifest manifest = UiPageManifestStore.Load();

            Assert.IsFalse(manifest.Pages.Any(page => page.Name == "UIMain"),
                "UIMain 已退役：页面清单不得再登记该样例页");
            Assert.AreEqual(11, manifest.Pages.Count, "tbuiform 删 id=1 后应为 11 个页面");
            Assert.AreEqual(12, manifest.Pages.Sum(page => page.Forms?.Count ?? 0), "登记行应为 12 行（Tmp_Dialog 双行）");
        }

        [Test]
        public void LuaKeys_UI常量不含UIMain且保留PageLogin()
        {
            var values = typeof(LuaKeys.UI)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.GetValue(null) as string)
                .Where(value => value != null)
                .ToList();

            Assert.That(values, Does.Not.Contain("UI.UIMain"), "LuaKeys 生成物不得再产样例页常量");
            Assert.That(values, Does.Contain("UI.Page_Login"), "真实产品页常量必须保留");
            Assert.AreEqual(6, values.Count, "UI 常量应为 6 个产品页（UIMain 退役后）");
        }

        [Test]
        public void 启动锚_RegistryFiller指向PageLogin且源码无UIMain()
        {
            FieldInfo knownKey = typeof(RegistryFiller).GetField("KnownUiKey",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(knownKey, "RegistryFiller 应暴露单源 KnownUiKey 供启动校验与守卫共用");
            Assert.AreEqual("UI.Page_Login", knownKey.GetValue(null), "启动校验锚应指向真实产品页");

            string source = ReadProjectFile("Assets/LiteGame/Adapters/LuaBridge/RegistryFiller.cs");
            StringAssert.DoesNotContain("LuaKeys.UI.UIMain", source,
                "启动校验不得再引用样例页常量");
        }

        [Test]
        public void mainLua_不再requireUIMain()
        {
            string source = ReadProjectFile("Assets/LiteGame/Lua/main.lua");
            StringAssert.DoesNotContain("UIMain", source, "main.lua 不得再 require/暴露样例页");
        }

        [Test]
        public void 测试载体_旧路径移除且新路径存在()
        {
            const string oldPrefab = "Assets/ShareResource/UI/Screens/UIMain.prefab";
            const string oldLua = "Assets/LiteGame/Lua/UI/UIMain.lua";
            const string newPrefab = "Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab";
            const string newLua = "Assets/LiteGame/Lua/Tests/UiTestPage.lua";

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(oldPrefab), "旧 UIMain prefab 应已改名");
            Assert.IsFalse(File.Exists(Path.Combine(ProjectRoot, oldLua)), "旧 UIMain Lua 应已移走");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(newPrefab), "新测试载体 prefab 必须存在");
            Assert.IsTrue(File.Exists(Path.Combine(ProjectRoot, newLua)), "新测试载体 Lua 必须存在");
        }

        [Test]
        public void 契约面_常驻壳根名不产契约()
        {
            Assert.IsFalse(UiContractGenerator.IsContractPageName("UIMain"),
                "UIMain 不属 Page_/Hud_/Tmp_ 契约面——常驻壳跳过判据不得回归");
            Assert.IsTrue(UiContractGenerator.IsContractPageName("Page_Login"), "产品页仍在契约面");
        }
    }
}
