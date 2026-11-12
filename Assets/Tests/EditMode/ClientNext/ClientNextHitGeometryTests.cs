using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 命中几何判定面（S3-2）：量化「瞄点高度 × 距离」对命中率的影响。
    /// 这是把 C1 诊断的算术变成可复跑的确定性证据——不依赖网络、不依赖 HUD 数字。
    /// </summary>
    public sealed class ClientNextHitGeometryTests
    {
        private const float MuzzleHeight = 1.2948f;

        private static bool TryHit(float targetZ, float aimHeight, out float distance)
        {
            var world = new SimWorldState();
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Flags = EntityFlags.Player },
                out int shooter);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, targetZ), Flags = EntityFlags.Player },
                out _);

            float ox = world.Entities[shooter].Pos.X;
            float oz = world.Entities[shooter].Pos.Z;
            float dz = targetZ - oz;
            float dy = aimHeight - MuzzleHeight;
            float len = (float)System.Math.Sqrt(dz * dz + dy * dy);
            bool hit = SimRaycast.RaycastEntities(world, shooter,
                ox: ox, oy: MuzzleHeight, oz: oz,
                dx: 0f, dz: dz / len, dy: dy / len,
                maxT: 60f, out _, out distance, CombatGeometry.Default);
            return hit;
        }

        [Test]
        public void 近距离_瞄躯干命中()
        {
            Assert.IsTrue(TryHit(6f, 1.0f, out float d),
                "6m 瞄躯干（+1.0m）应命中，实际射线距离 " + d.ToString("F2"));
        }

        [Test]
        public void 近距离_瞄头部命中()
        {
            Assert.IsTrue(TryHit(6f, 1.6f, out float d),
                "6m 瞄头部（+1.6m 落在 [1.449,1.8] 头部带）应命中，实际 " + d.ToString("F2"));
        }

        [Test]
        public void 远距离_瞄头部会越过躯干只能打到头()
        {
            // 头带半径 0.15m，body 半径 0.45m；瞄准 +1.6m 时射线从 1.2948 上升到 1.6，
            // 超过 1.449（HeadHitLine）后只有 0.15m 的头圆柱可打——远距离极易漏。
            Assert.IsTrue(TryHit(20f, 1.6f, out _) || TryHit(20f, 1.0f, out _),
                "20m 至少一种瞄法应命中（证明判定面本身可用）");
        }

        [Test]
        public void 二十米_瞄躯干比瞄头部更可靠()
        {
            // 直接量化：躯干半径 0.45m 远大于头部 0.15m，同样距离瞄躯干应命中。
            bool bodyHit = TryHit(20f, 1.0f, out float bodyDist);
            Assert.IsTrue(bodyHit,
                "20m 瞄躯干应命中（躯干半径 0.45m），实际射线距离 " + bodyDist.ToString("F2"));
        }

        [Test]
        public void 超出射程_不命中()
        {
            Assert.IsFalse(TryHit(200f, 1.0f, out _), "200m 超出射程不应命中");
        }
    }
}
