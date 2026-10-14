using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 结构守卫（构建器产物钉住——防"顺手改坏/接线漂移"）：
    /// ① Hud_Battle——菜单角组"单独开射线"不变量（§0 事件隔离：根组不拦射线、全树唯一
    /// GraphicRaycaster 在 Cot_Settings、其 CanvasGroup ignoreParentGroups、唯一 raycastTarget=菜单钮；
    /// 引用件 _settingsButton 接线）；② Page_Settings——退场钮在位、缺省隐藏、BindName 同文（§9.1）；
    /// ③ Page_Room——房主开局钮在位、缺省隐藏（房主事实回放才显）、BindName 同文；
    /// ④ Page_Lobby——创房钮在位、BindName 同文、三钮并排；⑤ Hud_Battle 排行榜——8 格预置、引用件齐备、零新射线。
    /// 直接读 prefab 资产（不实例化——结构与接线是资产事实）。
    /// </summary>
    public sealed class BattleHudMenuEditModeTests : UnityTestBase
    {
        private const string HudPath = "Assets/ShareResource/UI/Hud/Hud_Battle.prefab";
        private const string SettingsPath = "Assets/ShareResource/UI/Screens/Page_Settings.prefab";
        private const string RoomPath = "Assets/ShareResource/UI/Screens/Page_Room.prefab";
        private const string LobbyPath = "Assets/ShareResource/UI/Screens/Page_Lobby.prefab";

        [Test]
        public void HUD_菜单角组_单独开射线_其余零射线()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");

            var rootGroup = root.GetComponent<CanvasGroup>();
            Assert.IsNotNull(rootGroup, "根组在位");
            Assert.IsFalse(rootGroup.blocksRaycasts, "根组不拦射线（§0 事件隔离——HUD 不进事件系统）");

            Transform menu = root.transform.Find("Cot_Settings");
            Assert.IsNotNull(menu, "菜单角组在位");

            var raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);
            Assert.AreEqual(1, raycasters.Length, "全树仅菜单角组持 GraphicRaycaster（单独开射线——不给根开）");
            Assert.AreSame(menu, raycasters[0].transform, "GraphicRaycaster 挂在 Cot_Settings");

            var group = menu.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group, "菜单组 CanvasGroup 在位");
            Assert.IsTrue(group.ignoreParentGroups, "菜单组须忽略父组（根组 blocksRaycasts=false 下本子树仍可命中——单独开射线机制）");
            Assert.IsTrue(group.blocksRaycasts, "菜单组自身拦射线");
            Assert.IsTrue(group.interactable, "菜单组可交互");

            Transform button = menu.Find("Button_Settings");
            Assert.IsNotNull(button, "设置入口钮在位");
            var image = button.GetComponent<Image>();
            Assert.IsNotNull(image, "钮底图在位");
            Assert.IsTrue(image.raycastTarget, "菜单钮是 HUD 的射线命中面");
            Assert.IsNotNull(button.GetComponent<Button>(), "Button 组件在位");

            Transform label = button.Find("Txt_Settings");
            Assert.IsNotNull(label, "钮标签在位（HUD 固定英文口径）");
            var tmp = label.GetComponent<TMP_Text>();
            Assert.IsNotNull(tmp, "标签为 TMP");
            Assert.IsFalse(tmp.raycastTarget, "标签不参与射线");

            int raycastTargets = 0;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                if (graphic.raycastTarget) raycastTargets++;
            Assert.AreEqual(1, raycastTargets, "全 HUD 恰 1 个 raycastTarget=true（菜单钮）——§0 双保险（历史违规面由构建器归零，本守卫钉住）");

            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "引用件在位（序列化引用即契约）");
            Assert.AreSame(button.GetComponent<Button>(), view.SettingsButton, "BattleHudView._settingsButton 已接线（零 Find）");
        }

        [Test]
        public void 设置页_退场钮在位_缺省隐藏_BindName同文()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPath);
            Assert.IsNotNull(root, "Page_Settings.prefab 在库");

            Transform exit = FindDeep(root.transform, "Button_Exit");
            Assert.IsNotNull(exit, "退场钮在位");
            Assert.IsFalse(exit.gameObject.activeSelf, "缺省隐藏（显隐归 Lua battleExit 门控——非对局入口不闪现）");
            Assert.IsNotNull(exit.GetComponent<Button>(), "Button 组件在位（克隆 Button_Close 形态）");

            var bind = exit.GetComponent("BindNode");
            Assert.IsNotNull(bind, "退场钮有 BindNode 标记（路径 A 索引）");
            Assert.AreEqual("Exit", new SerializedObject(bind).FindProperty("BindName").stringValue,
                "BindName 与节点语义同文（§9.1）");

            var label = exit.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(label, "退场钮标签在位");
            Assert.AreEqual("Txt_Exit", label.gameObject.name, "标签名随克隆同文改名");
            var labelBind = label.GetComponent("BindNode");
            Assert.IsNotNull(labelBind, "标签有 BindNode（SetTextKey 寻址）");
            Assert.AreEqual("Exit", new SerializedObject(labelBind).FindProperty("BindName").stringValue,
                "标签 BindName 同文");
        }

        [Test]
        public void 房间页_开局钮在位_缺省隐藏_BindName同文()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath);
            Assert.IsNotNull(root, "Page_Room.prefab 在库");

            Transform start = FindDeep(root.transform, "Button_StartMatch");
            Assert.IsNotNull(start, "房主开局钮在位（克隆 Button_Leave 形态）");
            Assert.IsFalse(start.gameObject.activeSelf, "缺省隐藏——房主事实（Bridge.room 回放）才显，非房主/未进房不闪现");
            Assert.IsNotNull(start.GetComponent<Button>(), "Button 组件在位");

            var bind = start.GetComponent("BindNode");
            Assert.IsNotNull(bind, "开局钮有 BindNode 标记（路径 A 索引）");
            Assert.AreEqual("Button_StartMatch", new SerializedObject(bind).FindProperty("BindName").stringValue,
                "BindName 与节点名同文（UIBindIndex 只按 BindName 寻址——Lua `OnButton(\"Button_StartMatch\")`；"
                + "原为 `StartMatch` 致运行时 KeyNotFound，创房批顺手修）");

            var label = start.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(label, "开局钮标签在位");
            Assert.AreEqual("Txt_StartMatch", label.gameObject.name, "标签名随克隆同文改名");
            var labelBind = label.GetComponent("BindNode");
            Assert.IsNotNull(labelBind, "标签有 BindNode（SetTextKey 寻址）");
            Assert.AreEqual("Txt_StartMatch", new SerializedObject(labelBind).FindProperty("BindName").stringValue,
                "标签 BindName 与节点名同文（Lua `SetTextKey(\"Txt_StartMatch\")`）");
        }

        [Test]
        public void 大厅页_创房钮在位_BindName同文_三钮并排()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPath);
            Assert.IsNotNull(root, "Page_Lobby.prefab 在库");

            Transform create = FindDeep(root.transform, "Button_CreateRoom");
            Assert.IsNotNull(create, "创房钮在位（克隆 Button_QuickEnter 形态）");
            Assert.IsTrue(create.gameObject.activeSelf, "创房钮缺省可见（大厅常驻入口——无房主式门控）");
            Assert.IsNotNull(create.GetComponent<Button>(), "Button 组件在位");

            var bind = create.GetComponent("BindNode");
            Assert.IsNotNull(bind, "创房钮有 BindNode 标记（路径 A 索引）");
            Assert.AreEqual("Button_CreateRoom", new SerializedObject(bind).FindProperty("BindName").stringValue,
                "BindName 与节点名同文（UIBindIndex 按该名寻址——Lua `OnButton(\"Button_CreateRoom\")`）");

            var label = create.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(label, "创房钮标签在位");
            Assert.AreEqual("Txt_CreateRoom", label.gameObject.name, "标签名随克隆同文改名");
            var labelBind = label.GetComponent("BindNode");
            Assert.IsNotNull(labelBind, "标签有 BindNode（SetTextKey 寻址）");
            Assert.AreEqual("Txt_CreateRoom", new SerializedObject(labelBind).FindProperty("BindName").stringValue,
                "标签 BindName 与节点名同文");

            // 三钮并排（-420/0/+420，各 400 宽 + 20 间距）：QuickEnter 与 Records 同批重排
            var quick = (RectTransform)FindDeep(root.transform, "Button_QuickEnter");
            var records = (RectTransform)FindDeep(root.transform, "Button_Records");
            Assert.AreEqual(0f, ((RectTransform)create).anchoredPosition.x, 0.01f, "创房钮居中位");
            Assert.AreEqual(-420f, quick.anchoredPosition.x, 0.01f, "快速进入左位（同批重排）");
            Assert.AreEqual(420f, records.anchoredPosition.x, 0.01f, "战绩右位（同批重排）");
        }

        [Test]
        public void HUD_排行榜_八格预置_引用件已接_零新射线()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");

            Transform board = FindDeep(root.transform, "Cot_Rankboard");
            Assert.IsNotNull(board, "排行榜容器在位（FFA 比分＝击杀）");

            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "BattleHudView 引用件在位");
            Assert.IsTrue(view.HasRankboard, "8 格名字/击杀/高亮引用齐备（缺件则装配期静默降级）");
            Assert.IsNotNull(view.RankboardRoot, "排行榜容器引用已接");
            Assert.IsNotNull(view.TeamScoreText, "Txt_Score（团队比分预留）引用已接");
            Assert.IsFalse(view.TeamScoreText.gameObject.activeSelf, "团队比分预留节点缺省隐藏（FFA 不显示假 0）");

            for (int i = 1; i <= BattleRankboardDriver.CellCount; i++)
            {
                Transform cell = FindDeep(board, "Cot_RankCell" + i);
                Assert.IsNotNull(cell, $"第 {i} 格在位（**预置**——运行时代码禁建视觉元素）");
                Assert.IsNotNull(FindDeep(cell, "Txt_Name"), $"第 {i} 格名字件");
                Assert.IsNotNull(FindDeep(cell, "Txt_Kills"), $"第 {i} 格击杀件");
                Transform plate = FindDeep(cell, "Image_Highlight");
                Assert.IsNotNull(plate, $"第 {i} 格高亮件");
                var graphic = plate.GetComponent<Graphic>();
                Assert.IsNotNull(graphic, $"第 {i} 格高亮件是 Graphic");
                Assert.IsFalse(graphic.raycastTarget, $"第 {i} 格高亮不拦射线（HUD 唯一射线件仍是菜单钮）");
            }
        }

        [Test]
        public void HUD_死亡复活面_预置_引用件已接_缺省非激活()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");
            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "BattleHudView 引用件在位");

            Assert.IsTrue(view.HasRespawnHud, "死亡/复活件齐备（中央面板 3 件）");
            Assert.IsFalse(view.RespawnRoot.gameObject.activeSelf, "中央面板缺省非激活（驱动按事实开关——避免加载闪一下）");
            Assert.IsNotNull(FindDeep(root.transform, "Txt_RespawnTitle"), "阵亡标题件在位");
            Assert.IsNotNull(FindDeep(root.transform, "Txt_RespawnCountdown"), "复活倒计物件在位");
            Assert.IsNotNull(view.ProtectionRoot, "保护角标引用已接");
            Assert.IsFalse(view.ProtectionRoot.gameObject.activeSelf, "保护角标缺省非激活");
            Assert.IsNotNull(FindDeep(root.transform, "Txt_Protection"), "保护角标文案件在位");
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
