using LiteGameNext.Features.Matchmaking;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>大厅旁路入口：设置/战绩按钮必须存在于 prefab——防止「功能已写但玩家点不到」。</summary>
    public sealed class ClientNextLobbyEntryTests
    {
        [Test]
        public void 大厅prefab_含设置与战绩按钮()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/LiteGame/Next/Runtime/Features/Matchmaking/Content/LobbyPage.prefab");
            Assert.IsNotNull(prefab, "LobbyPage.prefab 应存在");
            Assert.IsNotNull(prefab.transform.Find("Btn_Settings"), "大厅必须有设置入口按钮");
            Assert.IsNotNull(prefab.transform.Find("Btn_Records"), "大厅必须有战绩入口按钮");
        }

        [Test]
        public void 两个入口按钮_各自挂BindName且名字正确()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/LiteGame/Next/Runtime/Features/Matchmaking/Content/LobbyPage.prefab");
            Assert.IsNotNull(prefab);
            AssertBindName(prefab, "Btn_Settings");
            AssertBindName(prefab, "Btn_Records");
        }

        private static void AssertBindName(GameObject prefab, string nodeName)
        {
            Transform node = prefab.transform.Find(nodeName);
            Assert.IsNotNull(node, "缺节点：" + nodeName);
            var bind = node.GetComponent<LiteGameNext.Platform.Ui.BindName>();
            Assert.IsNotNull(bind, nodeName + " 必须挂 BindName（PageBase 按名取控件）");
            Assert.AreEqual(nodeName, bind.Name, nodeName + " 的 BindName.Name 应与节点名一致");
        }
    }
}
