using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using LiteFramework.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 结构守卫（构建器产物钉住——防"顺手改坏/接线漂移"）：
    /// ① Hud_Battle——菜单角组"单独开射线"不变量（§0 事件隔离：根组不拦射线、全树唯一
    /// GraphicRaycaster 在 Cot_Settings、其 CanvasGroup ignoreParentGroups、唯一 raycastTarget=菜单钮；
    /// 引用件 _settingsButton 接线）；② Page_Settings——退场钮在位、缺省隐藏、BindName 同文（§9.1）；
    /// ③ Page_Room——房主开局钮在位、缺省隐藏（房主事实回放才显）、BindName 同文；
    /// ④ Page_Lobby——创房钮在位、BindName 同文、三钮并排；⑤ Hud_Battle 排行榜——8 格预置、引用件齐备、零新射线；
    /// ⑥ Hud_Battle 弹药读出——当前弹匣/剩余备弹/分隔符三件在位且引用件已接、零新射线。
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
            // 口径（流程批 IV 裁决）：BindName = **节点名同文**（沿用房主开局钮先例）——本断言原写裸语义名 "Exit"，
            // 与已落地的 prefab 形态（`Button_Exit`）不符，属陈旧断言（HEAD 即有红），随本批校正。
            Assert.AreEqual("Button_Exit", new SerializedObject(bind).FindProperty("BindName").stringValue,
                "BindName 与节点名同文（§9.1）");

            var label = exit.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(label, "退场钮标签在位");
            Assert.AreEqual("Txt_Exit", label.gameObject.name, "标签名随克隆同文改名");
            var labelBind = label.GetComponent("BindNode");
            Assert.IsNotNull(labelBind, "标签有 BindNode（SetTextKey 寻址）");
            Assert.AreEqual("Txt_Exit", new SerializedObject(labelBind).FindProperty("BindName").stringValue,
                "标签 BindName 与节点名同文");
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
        public void 大厅页_开房参数控件在位_引用名同文()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPath);
            Assert.IsNotNull(root, "Page_Lobby.prefab 在库");

            Transform row = FindDeep(root.transform, "Cot_CreateSetup");
            Assert.IsNotNull(row, "开房参数行在位（选图/选人数）");

            foreach (string n in new[] { "Txt_MapLabel", "Dropdown_Map", "Txt_PlayersLabel", "Dropdown_Players" })
            {
                Transform t = FindDeep(row, n);
                Assert.IsNotNull(t, $"{n} 在位");
                var bind = t.GetComponent("BindNode");
                Assert.IsNotNull(bind, $"{n} 有 BindNode（受控索引）");
                Assert.AreEqual(n, new SerializedObject(bind).FindProperty("BindName").stringValue,
                    $"{n} 的 BindName 与节点名同文（§9.1）");
            }

            // 下拉 = Tmp_Dropdown 模板实例（根挂 UIDropdownWrap——受控 SetDropdownOptions/GetDropdown 的消费面）
            foreach (string n in new[] { "Dropdown_Map", "Dropdown_Players" })
                Assert.IsNotNull(FindDeep(row, n).GetComponent("UIDropdownWrap"),
                    $"{n} 为下拉件（UIDropdownWrap——受控 API 按它取控件）");
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
            Assert.IsTrue(view.HasRankboard, "8 格根/名字/击杀/高亮引用齐备（缺件则装配期静默降级）");
            Assert.IsNotNull(view.RankboardRoot, "排行榜容器引用已接");
            Assert.IsNotNull(view.TeamScoreText, "Txt_Score（团队比分预留）引用已接");
            Assert.IsFalse(view.TeamScoreText.gameObject.activeSelf, "团队比分预留节点缺省隐藏（FFA 不显示假 0）");

            for (int i = 1; i <= BattleRankboardDriver.CellCount; i++)
            {
                Transform cell = FindDeep(board, "Cot_RankCell" + i);
                Assert.IsNotNull(cell, $"第 {i} 格在位（**预置**——运行时代码禁建视觉元素）");
                // 单元级显隐口径：**隐藏关格根，格内子件保持激活**（编辑器里可直接读/调，不必逐个点亮）
                Assert.IsFalse(cell.gameObject.activeSelf, $"第 {i} 格格根缺省非激活（驱动按人数开关；缺省不显示占位）");
                Assert.AreSame(cell.gameObject, view.RankCells[i - 1], $"第 {i} 格格根已接（BattleHudView._rankCells）");
                Transform name = FindDeep(cell, "Txt_Name");
                Transform kills = FindDeep(cell, "Txt_Kills");
                Assert.IsNotNull(name, $"第 {i} 格名字件");
                Assert.IsNotNull(kills, $"第 {i} 格击杀件");
                Assert.IsTrue(name.gameObject.activeSelf, $"第 {i} 格名字件保持激活（隐藏只关格根）");
                Assert.IsTrue(kills.gameObject.activeSelf, $"第 {i} 格击杀件保持激活（隐藏只关格根）");
                Transform plate = FindDeep(cell, "Image_Highlight");
                Assert.IsNotNull(plate, $"第 {i} 格高亮件");
                Assert.IsTrue(plate.gameObject.activeSelf, $"第 {i} 格高亮件保持激活（按位开关归驱动）");
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

        [Test]
        public void HUD_击杀提示面_预置行_引用件已接_缺省非激活()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");
            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "BattleHudView 引用件在位");

            Assert.IsTrue(view.HasKillFeed, "击杀提示行四组引用齐备（缺件则装配期静默降级）");
            Assert.IsNotNull(view.KillFeedRoot, "击杀提示容器引用已接");
            Assert.IsNotNull(view.WeaponIcons, "武器图标数组已接");
            Assert.AreEqual(BattleKillFeedDriver.SlotCount, view.WeaponIcons.Length, "图标数 = 装备槽数（下标域）");
            for (int i = 0; i < BattleKillFeedDriver.SlotCount; i++)
                Assert.IsNotNull(view.WeaponIcons[i], $"槽 {i} 武器图标在册（收集目录复制件）");

            for (int i = 1; i <= BattleKillFeedDriver.RowCount; i++)
            {
                Transform row = FindDeep(root.transform, "Cot_KillFeedRow" + i);
                Assert.IsNotNull(row, $"第 {i} 行预置（**运行时代码禁建视觉元素**）");
                Assert.IsFalse(row.gameObject.activeSelf, $"第 {i} 行缺省非激活（驱动按待展示队列开关）");
                Assert.IsFalse(row.gameObject.activeInHierarchy, $"第 {i} 行不参与渲染");

                Transform killer = FindDeep(row, "Txt_KillFeedKiller");
                Assert.IsNotNull(killer, $"第 {i} 行击杀者件");
                Transform victim = FindDeep(row, "Txt_KillFeedVictim");
                Assert.IsNotNull(victim, $"第 {i} 行死者件");
                Transform weapon = FindDeep(row, "Image_KillFeedWeapon");
                Assert.IsNotNull(weapon, $"第 {i} 行武器图标件");

                foreach (Transform t in new[] { killer, victim, weapon })
                {
                    var graphic = t.GetComponent<Graphic>();
                    Assert.IsNotNull(graphic, $"{t.name} 是 Graphic");
                    Assert.IsFalse(graphic.raycastTarget, $"{t.name} 不拦射线（HUD 唯一射线件仍是菜单钮）");
                    Assert.IsTrue(t.gameObject.activeSelf, $"{t.name} 保持激活（行的隐藏只关行根，子件可直接读/调）");
                }
            }
        }

        [Test]
        public void HUD_网络质量件_在位_引用已接_缺省非激活()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");
            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "BattleHudView 引用件在位");

            Assert.IsNotNull(FindDeep(root.transform, "Cot_NetStats"), "网络质量容器在位");
            Assert.IsNotNull(FindDeep(root.transform, "Txt_NetStats"), "网络质量文案件在位");
            Assert.IsNotNull(view.NetStatsRoot, "容器引用已接");
            Assert.IsNotNull(view.NetStatsText, "文本引用已接");
            Assert.IsFalse(view.NetStatsRoot.gameObject.activeSelf, "缺省非激活（驱动按探针样本开关）");

            var graphic = view.NetStatsText.GetComponent<Graphic>();
            Assert.IsNotNull(graphic, "文本是 Graphic");
            Assert.IsFalse(graphic.raycastTarget, "网络质量文本不拦射线（HUD 唯一射线件仍是菜单钮）");
        }

        [Test]
        public void HUD_弹药读出_当前弹匣与备弹_引用件已接_缺省在显()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(root, "Hud_Battle.prefab 在库");
            var view = root.GetComponent<BattleHudView>();
            Assert.IsNotNull(view, "BattleHudView 引用件在位");

            Transform box = FindDeep(root.transform, "Cot_Ammo");
            Assert.IsNotNull(box, "弹药读出容器在位");
            Transform ammo = FindDeep(box, "Txt_Ammo");
            Transform slash = FindDeep(box, "Txt_Slash");
            Transform reserve = FindDeep(box, "Txt_ReserveAmmo");
            Assert.IsNotNull(ammo, "当前弹匣件在位");
            Assert.IsNotNull(slash, "「/」分隔符件在位");
            Assert.IsNotNull(reserve, "剩余备弹件在位");

            Assert.AreSame(ammo.GetComponent<TextMeshProUGUI>(), view.AmmoText, "BattleHudView._ammoText 已接线（零 Find）");
            Assert.AreSame(reserve.GetComponent<TextMeshProUGUI>(), view.ReserveAmmoText,
                "BattleHudView._reserveAmmoText 已接线（备弹＝本人私有面 ReserveAmmo）");
            Assert.AreSame(slash.gameObject, view.SlashSeparator,
                "BattleHudView._slashSeparator 已接线（与备弹件同显隐——收起时读出只剩占位）");
            Assert.IsTrue(reserve.gameObject.activeSelf, "备弹件缺省激活（装备即显；未装备由驱动随占位收起）");
            Assert.IsTrue(slash.gameObject.activeSelf, "分隔符缺省激活（同上）");

            foreach (Transform t in new[] { ammo, slash, reserve })
            {
                var graphic = t.GetComponent<Graphic>();
                Assert.IsNotNull(graphic, $"{t.name} 是 Graphic");
                Assert.IsFalse(graphic.raycastTarget, $"{t.name} 不拦射线（HUD 唯一射线件仍是菜单钮）");
            }
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
