using System;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 客户端快照镜像用例（《状态同步专项设计》§5.2 分层应用 + 《M10实施指导》§3 镜像重建组）：
    /// 全量 = 整体重建（缺席槽位判死 + 清空全部运行态）；增量 = 覆盖变化槽位；
    /// 比赛状态/本人私有面随**每份**快照应用；Overlay 原语服务"无和解快照"的私有面刷新。
    /// </summary>
    public sealed class SnapshotReassemblerTests
    {
        private static SimWorldState BuildWorld()
        {
            var s = new SimWorldState { RngState = 0x1234UL };
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(-10f, 0f, 0f) }, out int slotA);
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(10f, 0f, 0f) }, out int slotB);

            // 公共战斗面 + 主动作摘要
            s.Entities[slotA].Shield = 60;
            s.Entities[slotA].Kills = 3;
            s.Entities[slotA].SelectedWeapon = 1;
            s.Actions[slotA * SimConfig.ActionSlotsPerEntity] = new ActionRuntime
            {
                ActionId = 301, StartFrame = 120, Phase = ActionPhase.Windup, CastToken = 7,
            };

            // A 的私有面（弹药/技能 CD/状态/背包/资源）
            s.Resources[slotA] = 42;
            s.Weapons[slotA * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;
            s.Actions[slotA * SimConfig.ActionSlotsPerEntity + 2].CooldownEnd = 600;
            s.Status[slotA * SimConfig.StatusSlotsPerEntity].EffectId = 51;
            s.MatchBag[slotA * SimConfig.MatchBagSlotsPerEntity].ItemDefId = 701;

            s.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };
            return s;
        }

        private static long IdOf(SimWorldState s, int slot) => s.Entities[slot].Id;

        [Fact]
        public void 全量快照_公共战斗面主动作摘要比赛状态全量重建()
        {
            SimWorldState s = BuildWorld();
            long idA = IdOf(s, 0);

            var msg = SnapshotCodec.PackFull(s.Frame, s, ackInput: 0);
            msg.PrivateState = SnapshotCodec.PackPrivate(s, idA);           // 广播/重连路径按会话附私有面

            var mirror = new SimWorldState();
            SnapshotReassembler.Apply(msg, mirror, out uint checksum);
            Assert.Equal(SimChecksum.ComputePublicChecksum(s), checksum);

            // 公共战斗面
            Assert.Equal(60, mirror.Entities[0].Shield);
            Assert.Equal(3, mirror.Entities[0].Kills);
            Assert.Equal(1, mirror.Entities[0].SelectedWeapon);

            // 主动作摘要（Actions[slot*4+0]；技能槽明细不进公共面）
            ActionRuntime active = mirror.Actions[0 * SimConfig.ActionSlotsPerEntity];
            Assert.Equal(301, active.ActionId);
            Assert.Equal(120, active.StartFrame);
            Assert.Equal(ActionPhase.Windup, active.Phase);
            Assert.Equal(0, active.CastToken);

            // 比赛状态层
            Assert.Equal(1, mirror.Match.Phase);
            Assert.Equal(10800, mirror.Match.Timer);
            Assert.Equal(1, mirror.Match.Round);

            // 本人私有面
            Assert.Equal(42, mirror.Resources[0]);
            Assert.Equal(30, mirror.Weapons[0].MagAmmo);
            Assert.Equal(600, mirror.Actions[0 * SimConfig.ActionSlotsPerEntity + 2].CooldownEnd);
            Assert.Equal(51, mirror.Status[0].EffectId);
            Assert.Equal(701, mirror.MatchBag[0].ItemDefId);

            // 他人私有面不随快照走（B 的武器槽恒零——只发本人）
            Assert.Equal(0, mirror.Resources[1]);
            Assert.Equal(0, mirror.Weapons[1 * SimConfig.WeaponSlotsPerEntity].MagAmmo);
        }

        [Fact]
        public void 全量快照_清空镜像残留运行态()
        {
            SimWorldState s = BuildWorld();
            long idA = IdOf(s, 0);

            // 镜像预置"历史残留"（上一局/上一状态的私有面 + 已死槽位的残留）
            var mirror = new SimWorldState();
            mirror.Entities[9] = new EntitySlot { Id = 123456L, Hp = 99, Shield = 77 };
            mirror.AliveBitmap[0] = 0xFFFFFFFFu;                              // 全槽位"活着"的脏位图
            mirror.Resources[9] = 55;
            mirror.Weapons[9 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 7;
            mirror.MatchBag[9 * SimConfig.MatchBagSlotsPerEntity].ItemDefId = 13;
            mirror.Match = new MatchStateData { Phase = 2, Timer = 1 };

            var msg = SnapshotCodec.PackFull(s.Frame, s, ackInput: 0);
            msg.PrivateState = SnapshotCodec.PackPrivate(s, idA);
            SnapshotReassembler.Apply(msg, mirror, out _);

            // 全量 = 整体重建：缺席槽位判死、运行态残留清零（"服务端有、客户端被清零"分叉的反向修复）
            Assert.False(mirror.IsAlive(9));
            Assert.Equal(0, mirror.Entities[9].Hp);
            Assert.Equal(0, mirror.Resources[9]);
            Assert.Equal(0, mirror.Weapons[9 * SimConfig.WeaponSlotsPerEntity].MagAmmo);
            Assert.Equal(0, mirror.MatchBag[9 * SimConfig.MatchBagSlotsPerEntity].ItemDefId);
            Assert.Equal(s.AliveCount(), mirror.AliveCount());
            Assert.Equal(1, mirror.Match.Phase);                              // 比赛状态以本包为准
        }

        [Fact]
        public void 增量快照_只覆盖变化槽位_私有与比赛仍随包刷新()
        {
            SimWorldState s = BuildWorld();
            long idA = IdOf(s, 0);
            var differ = new SnapshotDiffer();
            var mirror = new SimWorldState();

            SnapshotReassembler.Apply(differ.Build(s.Frame, s, 0), mirror, out _);   // 首帧全量建基线

            // 权威推进：A 护盾变化（公共）+ A 弹药消耗（私有）+ 倒计时递减（比赛）
            s.Frame++;
            s.Entities[0].Shield = 30;
            s.Weapons[0].MagAmmo = 29;
            s.Match.Timer = 10799;
            Proto.StateSnapshot delta = differ.Build(s.Frame, s, 0,
                new SimVector3(0f, 0f, 0f), 0f, forceFull: false);
            Assert.False(delta.IsFull);
            delta.PrivateState = SnapshotCodec.PackPrivate(s, idA);

            SnapshotReassembler.Apply(delta, mirror, out _);

            Assert.Equal(30, mirror.Entities[0].Shield);                       // 变化公共面覆盖
            Assert.Equal(CombatConfig.EntityHp, mirror.Entities[1].Hp);         // 未变化槽位不动（缺席 = 未变化）
            Assert.Equal(29, mirror.Weapons[0].MagAmmo);                        // 私有面随增量包刷新
            Assert.Equal(10799, mirror.Match.Timer);                            // 比赛状态随增量包刷新
        }

        [Fact]
        public void Overlay原语_只刷私有与比赛_不动公共面()
        {
            SimWorldState s = BuildWorld();
            long idA = IdOf(s, 0);
            var differ = new SnapshotDiffer();
            Proto.StateSnapshot msg = differ.Build(s.Frame, s, 0);
            msg.PrivateState = SnapshotCodec.PackPrivate(s, idA);

            // "本地预测态"：公共面是客户端自己的预测值（与权威不同），私有/比赛是服务器事实
            var local = new SimWorldState();
            local.Spawn(new EntitySlot { Hp = 12, Pos = new SimVector3(99f, 0f, 0f) }, out int localSlot);
            local.Entities[localSlot].Shield = 7;

            SnapshotReassembler.OverlayPrivateAndMatch(msg, local);

            Assert.Equal(7, local.Entities[localSlot].Shield);                  // 公共面不被覆盖（预测正确时的前提）
            Assert.Equal(12, local.Entities[localSlot].Hp);
            Assert.Equal(42, local.Resources[localSlot]);                       // 私有面刷到本地（本人槽位段）
            Assert.Equal(30, local.Weapons[localSlot * SimConfig.WeaponSlotsPerEntity].MagAmmo);
            Assert.Equal(1, local.Match.Phase);                                 // 比赛状态刷到本地
        }
    }
}
