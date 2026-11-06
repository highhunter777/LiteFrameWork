using System.IO;
using LiteGame.EditorTools;
using LiteSim;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 身位几何三方对账守卫（防"美术改 CC 忘重烘"）：prefab 的 CharacterController ≡ 判定几何表 `#combatgeom` 的 <c>body_radius</c>/<c>hitscan_h。
    /// </summary>
    public sealed class BodyBakeEditModeTests
    {
        private const string PrefabPath = "Assets/LiteGame/Modules/Battle/Content/Characters/Player(Rifle).prefab";

        [Test]
        [Category(TestCategory.Contract)]
        public void 对账_prefab的CC等于表行与入库夹具()
        {
            Assert.IsTrue(File.Exists(PrefabPath), "缺玩家 prefab（不入 VCS——本地克隆需从派发渠道还原）");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "prefab 载入失败：" + PrefabPath);

            var cc = prefab.GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "prefab 根缺 CharacterController——身位烘焙的唯一来源");

            Assert.IsTrue(CombatGeomTableWriter.TryReadRow(out var row, out string rowError), rowError);
            Assert.IsTrue(CombatGeomTableWriter.TryGetFloat(row, "body_radius", out float tableRadius, out rowError), rowError);
            Assert.IsTrue(CombatGeomTableWriter.TryGetFloat(row, "hitscan_height", out float tableHeight, out rowError), rowError);

            Assert.IsTrue(CombatGeomTableWriter.TryReadBodyCcFixture(out string sourcePrefab,
                out float fixtureRadius, out float fixtureHeight, out string fixtureError), fixtureError);

            Assert.AreEqual(PrefabPath, sourcePrefab, "身位夹具来源 prefab 不匹配——重跑身位圆柱烘焙");

            Assert.AreEqual(cc.radius, tableRadius, 0f,
                "CC 半径 ≠ #combatgeom.body_radius——改了 prefab 未重跑烘焙工具箱「身位圆柱」");
            Assert.AreEqual(cc.height, tableHeight, 0f,
                "CC 高度 ≠ #combatgeom.hitscan_height——改了 prefab 未重跑烘焙工具箱「身位圆柱」");
            Assert.AreEqual(cc.radius, fixtureRadius, 0f,
                "CC 半径 ≠ 入库夹具 radius——改了 prefab 未重跑烘焙工具箱「身位圆柱」");
            Assert.AreEqual(cc.height, fixtureHeight, 0f,
                "CC 高度 ≠ 入库夹具 height——改了 prefab 未重跑烘焙工具箱「身位圆柱」");
            Assert.AreEqual(cc.height * 0.5f, cc.center.y, 1e-5f,
                "CC center.y ≠ height/2（圆柱底须在脚底——烘焙工具的前置校验同款）");

            // 判定面投影与表值同源（未发布时展示兜底也须与表一致）。
            Assert.AreEqual(tableRadius, CombatConfig.Geometry.BodyRadius, 0f);
            Assert.AreEqual(tableHeight, CombatConfig.Geometry.HitscanHeight, 0f);
        }
    }
}
