using LiteGameNext.Features.Battle.Hud;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>HUD 视图：prefab 必须承载 10 类读数的控件，且缺件不阻断对局。</summary>
    public sealed class ClientNextBattleHudViewTests
    {
        private const string PrefabPath =
            "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleHud.prefab";

        private static GameObject Load() =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        [Test]
        public void HUDprefab_含十类读数控件()
        {
            GameObject prefab = Load();
            Assert.IsNotNull(prefab, "BattleHud.prefab 应存在");
            string[] required =
            {
                "Txt_Health", "Txt_Ammo", "Txt_Score", "Txt_TeamScore", "Txt_Net",
                "Txt_Respawn", "Cot_Crosshair", "Cot_Hip", "Cot_Ads",
                "Cot_Respawn", "Cot_Protection", "Cot_Rankboard", "Cot_KillFeed",
            };
            foreach (string name in required)
                Assert.IsNotNull(prefab.transform.Find(name), "HUD 缺控件：" + name);
        }

        [Test]
        public void HUDprefab_含八行排行与五行击杀流()
        {
            GameObject prefab = Load();
            Assert.IsNotNull(prefab);
            for (int i = 1; i <= 8; i++)
                Assert.IsNotNull(prefab.transform.Find("Txt_Rank" + i), "缺排行行 " + i);
            for (int i = 1; i <= 5; i++)
                Assert.IsNotNull(prefab.transform.Find("Txt_Feed" + i), "缺击杀流行 " + i);
        }

        [Test]
        public void 无模型时刷新不抛()
        {
            var go = new GameObject("Hud", typeof(BattleHudView));
            try
            {
                var view = go.GetComponent<BattleHudView>();
                Assert.DoesNotThrow(() => view.Refresh());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 绑定模型后刷新不抛()
        {
            var go = new GameObject("Hud", typeof(BattleHudView));
            try
            {
                var view = go.GetComponent<BattleHudView>();
                var model = new BattleHudModel();
                model.SetVitals(80, 100, 12);
                model.SetAmmoDetail(12, 60, false);
                model.SetScore(2, 1, 5);
                model.SetLifeState(true, 3f, false);
                model.SetNetwork(40, 20);
                model.PushKillFeed(new KillFeedRow("A", "B", 1, false));
                view.Bind(model);
                // 未 Prepare 时无绑定表，Safe 封装应全部跳过而不是抛。
                Assert.DoesNotThrow(() => view.Refresh());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
