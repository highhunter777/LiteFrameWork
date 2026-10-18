using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 快照差分的关键状态粘滞（P1 血量/复活同步）与活体位图判定：
    /// - Hp/死亡/复活等一次性变化在变化后 1s 内重复下发（丢一包也不会等到全量兜底）；
    /// - 活体数不变但位图变化（一死一生）必须转全量，不能被"数量相等"骗过。
    /// </summary>
    public sealed class SnapshotStickyTests
    {
        private static SimWorldState TwoPlayers()
        {
            var state = new SimWorldState { RngState = 1UL };
            state.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            state.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(1f, 0f, 0f) }, out _);
            return state;
        }

        private static bool HasSlot(Proto.StateSnapshot msg, int slot)
        {
            for (int i = 0; i < msg.Slots.Count; i++)
                if (msg.Slots[i].Slot == slot) return true;
            return false;
        }

        [Fact]
        public void 血量变化_丢包后仍在粘滞窗重复下发()
        {
            var state = TwoPlayers();
            var differ = new SnapshotDiffer();
            var first = differ.Build(1, state, 0, SimVector3.Zero, SimConfig.AoiRadius);
            Assert.True(first.IsFull, "首帧全量");

            state.Entities[0].Hp = 40;
            var changed = differ.Build(3, state, 0, SimVector3.Zero, SimConfig.AoiRadius);
            Assert.False(changed.IsFull);
            Assert.True(HasSlot(changed, 0), "血量变化应在增量里");

            // 模拟这一包含 Hp=40 的快照丢包：客户端没收到；下一份增量基线已推进，但粘滞窗必须重发。
            var sticky = differ.Build(5, state, 0, SimVector3.Zero, SimConfig.AoiRadius);
            Assert.False(sticky.IsFull);
            Assert.True(HasSlot(sticky, 0), "关键状态粘滞窗内必须重复下发");
            for (int i = 0; i < sticky.Slots.Count; i++)
                if (sticky.Slots[i].Slot == 0) Assert.Equal(40, sticky.Slots[i].Hp);

            var expired = differ.Build(5 + SimConfig.TickRate + 1, state, 0, SimVector3.Zero, SimConfig.AoiRadius);
            // 过期时恰好也可能撞上周期全量（FullEveryFrames=60）——全量同样覆盖；未全量时必须不再粘滞。
            if (!expired.IsFull) Assert.False(HasSlot(expired, 0), "粘滞窗过期后恢复按变化集发送");
        }

        [Fact]
        public void 活体数不变但位图变化_仍转全量()
        {
            var state = TwoPlayers();
            var differ = new SnapshotDiffer();
            differ.Build(1, state, 0, SimVector3.Zero, SimConfig.AoiRadius);

            state.Despawn(state.Entities[0].Id);                 // 死一个
            state.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(2f, 0f, 0f) }, out _); // 生一个：数量不变
            var snap = differ.Build(3, state, 0, SimVector3.Zero, SimConfig.AoiRadius);

            Assert.True(snap.IsFull, "位图变化必须全量（否则客户端把缺席槽位当未变化，留下幽灵/误杀）");
        }
    }
}
