using System.Collections.Generic;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 退出条件用例（《商业级通用服务端框架总设计》§16）：
    /// **RoomRuntime 在无 Socket、无 Sleep、无文件的纯 L1 测试中完整跑一局 + 重连。**
    ///
    /// 时间只从 <see cref="RoomCommand.Tick"/> 命令进入（虚拟毫秒由用例手排，零真实等待）；
    /// 输入经 <see cref="ClientInputBatch"/> 纯数据；输出为纯事件族——全程不触传输/编码/文件/墙钟
    /// （Runtime 纯化由 L0 纪律扫描 R11 把守，见 DisciplineScannerTests）。
    /// </summary>
    public sealed class RoomRuntimeReconnectTests
    {
        private const long SharedSeed = 314L;

        /// <summary>合成"第 frame 帧玩家 playerId 的移动输入"（EntityId 按席位取——缺省 0 会被判失效实体）。</summary>
        private static ClientInputBatch MoveInput(RoomRuntime room, int playerId, int frame, float moveX)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { EntityId = room.EntityIdOf(playerId), MoveX = moveX };
            return new ClientInputBatch { Frame = frame, AckSnapshot = 0, Count = 1, Frames = frames };
        }

        private static void FeedMove(RoomRuntime room, int playerId, float moveX)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.ClientInput(playerId, MoveInput(room, playerId, room.AuthSim.Frame + 1, moveX)), outputs);
        }

        /// <summary>开局到 Running（2 人满员）。</summary>
        private static RoomRuntime StartRunning(string roomId, long timeLimitMs = 0)
        {
            var room = new RoomRuntime(new RoomConfig
            {
                RoomId = roomId, ExpectedPlayers = 2, Seed = SharedSeed, MatchTimeLimitMs = timeLimitMs,
            });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(11), outputs);
            room.Execute(RoomCommand.Join(22), outputs);
            Assert.Equal(MatchPhase.Running, room.Phase);
            return room;
        }

        [Fact]
        public void 一局加重连_纯命令面闭环_无Socket无Sleep无文件()
        {
            var room = StartRunning("Recon-Full", timeLimitMs: 2_000);
            var outputs = new List<RoomOutput>();

            // 开局实体归属固定（重连后必须不变——§5.6 权威快照恢复的前提）
            long entity0 = room.EntityIdOf(0);
            long entity1 = room.EntityIdOf(1);
            Assert.True(entity0 > 0 && entity1 > 0);

            // 对局进行中：双端喂输入 + 虚拟时间步进（50 帧）
            for (int i = 0; i < 50; i++)
            {
                FeedMove(room, 0, 0.5f);
                FeedMove(room, 1, -0.5f);
                room.Execute(RoomCommand.Tick(i * 16), outputs);
            }
            int frameAtDisconnect = room.AuthSim.Frame;
            Assert.True(frameAtDisconnect >= 50, "50 次 Tick 应推进 50 权威帧");

            // 玩家 0 掉线：席位保留（Disconnected），对局继续（§4.5-2 掉线不停帧）
            room.Execute(RoomCommand.Disconnect(11), outputs);
            Assert.Equal(SeatPhase.Disconnected, room.SeatOf(0).Phase);
            Assert.Equal(MatchPhase.Running, room.Phase);

            for (int i = 0; i < 10; i++) room.Execute(RoomCommand.Tick(1_000 + i * 16), outputs);
            Assert.True(room.AuthSim.Frame > frameAtDisconnect, "断线期间权威循环继续步进");

            // 重连重绑：新连接 33 原子接管席位 0 → Restoring（恢复完成 ACK 前的抑制相位）
            room.Execute(RoomCommand.Rebind(0, 33), outputs);
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);
            Assert.Equal(33, room.SeatOf(0).ConnectionId);
            Assert.True(room.EntityIdOf(0) == entity0, "重绑不得改实体归属");

            // 恢复完成 ACK → Active + SeatRestored 事件
            outputs.Clear();
            room.Execute(RoomCommand.RestoreAck(0), outputs);
            Assert.Equal(SeatPhase.Active, room.SeatOf(0).Phase);
            Assert.Contains(outputs, o => o is SignalOutput { Signal: SeatRestored { PlayerId: 0 } });

            // 席位侧去重水位随席位保留：陈旧帧不复活（§9.2"connectionId 复用不能复活旧身份"）
            long accepted = room.Gate.AcceptedCount;
            room.Execute(RoomCommand.ClientInput(0, MoveInput(room, 0, 1, 1f)), outputs);   // 已消费过的旧帧
            Assert.Equal(accepted, room.Gate.AcceptedCount);
            FeedMove(room, 0, 0.5f);                                                                                        // 新帧照常接受
            Assert.Equal(accepted + 1, room.Gate.AcceptedCount);

            // 跑到对局时限 → Finishing → Settling → Closed（一局完整收尾，§9.1）
            outputs.Clear();
            for (int i = 0; i < 10 && !room.Closed; i++)
                room.Execute(RoomCommand.Tick(2_000 + i * 20), outputs);

            Assert.Equal(MatchPhase.Closed, room.Phase);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Running, To: MatchPhase.Finishing });
            Assert.Contains(outputs, o => o is SettlementReadyOutput sr && sr.Summary.EndReason == ShutdownReason.TimeLimit);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Settling, To: MatchPhase.Closed });
        }

        [Fact]
        public void 恢复中断线_席位回Disconnected_可再次重绑()
        {
            var room = StartRunning("Recon-Drop");
            var outputs = new List<RoomOutput>();

            room.Execute(RoomCommand.Disconnect(11), outputs);
            Assert.Equal(SeatPhase.Disconnected, room.SeatOf(0).Phase);

            room.Execute(RoomCommand.Rebind(0, 5), outputs);           // 第一次重绑 → Restoring
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);

            room.Execute(RoomCommand.Disconnect(5), outputs);          // 恢复中再掉线 → Disconnected（席位仍保留）
            Assert.Equal(SeatPhase.Disconnected, room.SeatOf(0).Phase);
            Assert.Equal(-1, room.SeatOf(0).ConnectionId);

            room.Execute(RoomCommand.Rebind(0, 6), outputs);           // 第二次重绑（新票据场景）
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);
            Assert.Equal(6, room.SeatOf(0).ConnectionId);

            outputs.Clear();
            room.Execute(RoomCommand.RestoreAck(0), outputs);
            Assert.Equal(SeatPhase.Active, room.SeatOf(0).Phase);
            Assert.Single(outputs);                                    // 恰一次 SeatRestored

            outputs.Clear();
            room.Execute(RoomCommand.RestoreAck(0), outputs);          // 重复 ACK 幂等忽略
            Assert.Empty(outputs);
            Assert.Equal(SeatPhase.Active, room.SeatOf(0).Phase);
        }

        [Fact]
        public void 旧连接迟到断开_不误伤已重绑席位()
        {
            var room = StartRunning("Recon-Stale");
            var outputs = new List<RoomOutput>();

            room.Execute(RoomCommand.Disconnect(11), outputs);
            room.Execute(RoomCommand.Rebind(0, 5), outputs);
            room.Execute(RoomCommand.Rebind(0, 6), outputs);           // 连续两次重绑：5 的映射已被 6 顶掉

            room.Execute(RoomCommand.Disconnect(5), outputs);          // 旧连接 5 迟到断开：无映射即忽略
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);
            Assert.Equal(6, room.SeatOf(0).ConnectionId);

            room.Execute(RoomCommand.Disconnect(11), outputs);         // 最初的连接同样不复活旧身份
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);
        }

        [Fact]
        public void 终态房间拒绝重连恢复()
        {
            var room = StartRunning("Recon-Closed");
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);
            Assert.True(room.Closed);

            // 终态房间：重绑/恢复 ACK 均无害（命令幂等），席位不复活对局
            outputs.Clear();
            room.Execute(RoomCommand.Rebind(0, 99), outputs);
            room.Execute(RoomCommand.RestoreAck(0), outputs);
            Assert.True(room.Closed);
            Assert.Equal(MatchPhase.Closed, room.Phase);
            Assert.DoesNotContain(outputs, o => o is SignalOutput { Signal: SeatRestored });
        }
    }
}
