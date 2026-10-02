using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 分型表守卫（《实体分型表设计》§5 验收：结构段）：
    /// ① CopyTo 深拷覆盖三张分型表（新字段漏拷 = 回滚/重连静默分叉——《游戏业务总设计》§1 阻塞项②）；
    /// ② Despawn 清行（空槽校验值恒定，§3.6 全槽位参与校验的前提）；
    /// ③ checksum 双口径纳入（公共口径缺该层 = 线上分叉但和解不报）。
    /// 消费系统（Item/Projectile/Zone）落地时在此补"行数据随 kind 位生效"的判定守卫。
    /// </summary>
    public sealed class EntityKindTablesTests
    {
        [Fact]
        public void CopyTo_分型表三行深拷()
        {
            var a = new SimWorldState();
            Spawn(a, out int slot);

            a.Items[slot] = new ItemState { ItemDefId = 3, Count = 4, OwnerId = 42L, AgeFrames = 7 };
            a.Projectiles[slot] = new ProjectileState { ItemDefId = 3, Speed = 20f, DetonateFrame = 90, OwnerId = 42L };
            a.Zones[slot] = new ZoneState { ItemDefId = 4, Radius = 6f, RemainingFrames = 180, OwnerId = 42L };

            var b = new SimWorldState();
            a.CopyTo(b);

            Assert.Equal(3, b.Items[slot].ItemDefId);
            Assert.Equal(4, b.Items[slot].Count);
            Assert.Equal(42L, b.Items[slot].OwnerId);
            Assert.Equal(7, b.Items[slot].AgeFrames);
            Assert.Equal(20f, b.Projectiles[slot].Speed);
            Assert.Equal(90, b.Projectiles[slot].DetonateFrame);
            Assert.Equal(6f, b.Zones[slot].Radius);
            Assert.Equal(180, b.Zones[slot].RemainingFrames);

            // 深拷不是共享：改源不改拷贝（struct 值类型数组，Array.Copy 语义）
            a.Items[slot].Count = 99;
            a.Zones[slot].RemainingFrames = 1;
            Assert.Equal(4, b.Items[slot].Count);
            Assert.Equal(180, b.Zones[slot].RemainingFrames);
        }

        [Fact]
        public void Despawn_分型行清零_空槽校验值恒定()
        {
            var a = new SimWorldState();
            long id = Spawn(a, out _);

            // 写脏分型行 → despawn → 再 spawn：新占用者看不到上一占用者的残留（§3.6 前提）
            int slot = (int)(id & 0xFFFFL);
            a.Items[slot] = new ItemState { ItemDefId = 5, Count = 12, OwnerId = id, AgeFrames = 33 };
            a.Projectiles[slot] = new ProjectileState { ItemDefId = 5, Speed = 9f, DetonateFrame = 5, OwnerId = id };

            a.Despawn(id);

            Assert.Equal(0, a.Items[slot].ItemDefId);
            Assert.Equal(0, a.Items[slot].Count);
            Assert.Equal(0L, a.Items[slot].OwnerId);
            Assert.Equal(0, a.Projectiles[slot].ItemDefId);
            Assert.Equal(0f, a.Projectiles[slot].Speed);
            Assert.Equal(0f, a.Zones[slot].Radius);

            // 空槽校验值恒定：脏行清零后 checksum 与"从未脏过"一致
            var clean = new SimWorldState();
            Assert.Equal(SimChecksum.ComputeStateChecksum(clean), SimChecksum.ComputeStateChecksum(a));
        }

        [Fact]
        public void Checksum_双口径纳入分型表()
        {
            var a = new SimWorldState();
            var b = new SimWorldState();
            Spawn(a, out int slot);
            Spawn(a, out int slot2);
            Spawn(b, out int bSlot);
            Spawn(b, out int bSlot2);
            Assert.Equal(slot, bSlot);      // 同序分配：两端槽位一致（对拍前提）

            // 同布局同数据 → 两口径一致；改一行 → 两口径都变（漏一面 = 静默漂移）
            a.Items[slot] = new ItemState { ItemDefId = 1, Count = 1 };
            b.Items[bSlot] = new ItemState { ItemDefId = 1, Count = 1 };
            Assert.Equal(SimChecksum.ComputeStateChecksum(a), SimChecksum.ComputeStateChecksum(b));
            Assert.Equal(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));

            b.Items[bSlot2].Count = 1;   // 与 a 分叉
            Assert.NotEqual(SimChecksum.ComputeStateChecksum(a), SimChecksum.ComputeStateChecksum(b));
            Assert.NotEqual(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));

            b.Items[bSlot2].Count = 0;   // 复原
            Assert.Equal(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));

            b.Zones[bSlot].Radius = 5f;  // 区域面同判
            Assert.NotEqual(SimChecksum.ComputePublicChecksum(a), SimChecksum.ComputePublicChecksum(b));
        }

        private static long Spawn(SimWorldState s, out int slotIndex)
        {
            return s.Spawn(new EntitySlot { Hp = 100 }, out slotIndex);
        }
    }
}
