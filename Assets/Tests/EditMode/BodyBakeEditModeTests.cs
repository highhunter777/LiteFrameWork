using System.IO;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 身位烘焙对账守卫（防"美术改 CC 忘重烘"）：prefab 的 CharacterController 必须等于
    /// 生成常量 <see cref="BodyBake"/>（移动去穿插/视图 CC 消费 <see cref="CombatConfig.BodyRadius"/>/<c>HitscanHeight</c>；
    /// 命中柱半径 <c>HitscanRadius</c> 是判定宽容裁决常量、不参与本对账）。
    ///
    /// **背景**：身位几何的单源 = `Player(Rifle).prefab` 的 CC（经 <c>BodyCylinderBaker</c> 烘成
    /// `BodyBake.g.cs`）；CC 是美术可调面——改了 prefab 不重跑工具，两端表现与判定就会分叉。
    /// 本用例把这条纪律钉在夜间 L2（EditMode）：漂移即红，重跑 `LiteGame/烘焙身位圆柱` 即绿。
    /// </summary>
    public sealed class BodyBakeEditModeTests
    {
        private const string PrefabPath = "Assets/Prefab/Player(Rifle).prefab";

        [Test]
        [Category(TestCategory.Contract)]
        public void 对账_prefab的CC等于烘焙常量()
        {
            Assert.IsTrue(File.Exists(PrefabPath), "缺玩家 prefab（不入 VCS——本地克隆需从派发渠道还原）");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "prefab 载入失败：" + PrefabPath);

            var cc = prefab.GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "prefab 根缺 CharacterController——身位烘焙的唯一来源");

            Assert.AreEqual(BodyBake.Radius, cc.radius, 1e-5f,
                "CC 半径 ≠ BodyBake.Radius——改了 prefab 未重跑 LiteGame/烘焙身位圆柱");
            Assert.AreEqual(BodyBake.Height, cc.height, 1e-5f,
                "CC 高度 ≠ BodyBake.Height——改了 prefab 未重跑 LiteGame/烘焙身位圆柱");
            Assert.AreEqual(cc.height * 0.5f, cc.center.y, 1e-5f,
                "CC center.y ≠ height/2（圆柱底须在脚底——烘焙工具的前置校验同款）");

            Assert.AreEqual(BodyBake.Radius, CombatConfig.BodyRadius, 0f);
            Assert.AreEqual(BodyBake.Height, CombatConfig.HitscanHeight, 0f);
        }
    }
}
