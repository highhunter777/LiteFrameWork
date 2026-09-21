using System;
using System.Collections.Generic;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using RoomServer;

namespace LiteNet.Tests
{
    /// <summary>
    /// 无头客户端（M10 验收 harness；M10 决策⑥ 全量预测形态）：
    /// RoomClient（网络面）+ RollbackSim（全量预测/和解，惰性建——StartGame.Seed 下发后与服务器同构建世界）。
    ///
    /// 和解（决策⑥ 全量预测形态）：远端玩家输入客户端未知（沿用零）→ 权威与预测必分叉 → 每次快照
    /// OnAuthoritativeSnapshot 恢复权威 + 重放本地历史 → 不发散（有界偏差）。和解率 = "权威与预测差异率"
    /// （全量预测形态下预期偏高属机制正确；混合形态调优留后——《M10 实施指导》决策⑥）。
    /// </summary>
    public sealed class HeadlessClient : IDisposable
    {
        public readonly RoomClient Client;
        public readonly string ClientName;
        public int PlayerId = -1;
        public long LocalEntityId;
        public int LastSnapshotFrame;
        public long MismatchReports;      // 上报的和解次数（和解率分子）
        public long InputsSent;

        private readonly KcpTransportClient _transport;   // 本 harness 创建并拥有（生产路径由 KcpNetworkService 持有）
        private RollbackSim _sim;
        private SimMapData _map;
        private SimWorldState _mirror;             // 持久权威镜像（SnapshotReassembler 应用目标，§5.5——增量快照只在它上面累积才完整）
        private readonly Queue<SimInputFrame> _pending = new Queue<SimInputFrame>();
        private SimInputFrame _lastInput;

        public RollbackSim Sim => _sim;

        public HeadlessClient(string name, SimMapData map, KcpTransportClient transport)
        {
            ClientName = name;
            _map = map;
            _transport = transport;
            Client = new RoomClient(transport);
            Client.OnStartGame += OnStartGame;
            Client.OnSnapshot += OnSnapshot;
            // 连接即自动请求进房：Join 延迟到 transport OnConnected（kcp2k cookie 握手完成后）再发
            transport.OnConnected += () => Client.SendJoin(RoomConfig.Default().RoomId, "harness", RoomServer.ServerHost.ServerBuildHash);
            Client.Connect("127.0.0.1", 27778);
        }

        /// <summary>StartGame：按服务器下发的 seed 重建同构世界（双玩家 = 与 Room.Start 的玩家段一致）→ 建 RollbackSim。</summary>
        private void OnStartGame(Proto.StartGame sg)
        {
            if (_sim != null) return;   // 幂等

            var world = new SimWorldState { RngState = (ulong)sg.Seed };
            for (int i = 0; i < 2; i++)
            {
                SimVector3 spawn = _map.SpawnPoints[i % _map.SpawnPointCount];
                world.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = spawn, Yaw = 0f }, out int _);
            }
            _sim = new RollbackSim(world, _map, IdentityTemplateFor(PlayerId, 2));
        }

        /// <summary>身份模板：EntityId = 各玩家槽位实体（占位——JoinAck 后按 PlayerId 对齐；真实 Id 由快照 SlotDelta 携带）。</summary>
        private static SimInputFrame[] IdentityTemplateFor(int playerId, int playerCount)
        {
            var template = new SimInputFrame[playerCount];
            for (int i = 0; i < 2; i++) template[i].EntityId = i;   // 服务器覆写防伪；本地预测按 playerId 对齐
            return template;
        }

        /// <summary>注入本地意图输入（对跑脚本生成；下一 Tick 发出并预测消费）。</summary>
        public void EnqueueLocalInput(SimInputFrame input) => _pending.Enqueue(input);

        /// <summary>推进（泵内调用）：发本地输入 → 预测推进。</summary>
        public void Tick(float realDelta)
        {
            if (_sim == null) return;

            // 本地输入 EntityId 对齐本客户端（PlayerId → 槽位实体；服务器侧 InputGate 会覆写防伪，本地预测保持同 Id）
            var local = _pending.Count > 0 ? _pending.Dequeue() : default;
            local.EntityId = LocalEntityId;

            Client.SendInput(_sim.State.Frame + 1, local, viewFrame: 0);
            InputsSent++;

            Sim.Tick(realDelta);
            _lastInput = local;
        }

        /// <summary>快照处理：持久镜像重建（协议单源 SnapshotReassembler）→ 和解（公共口径 checksum 比对/覆盖/重放）
        /// → 无和解时把比赛状态/本人私有面覆盖到本地态（客户端不预测的量须随包刷新）。
        /// 首次快照对齐本地实体 Id（按 Slot==PlayerId）。
        /// StartGame 未达（Unreliable 快照可能先于 Reliable 信令到达）时丢弃快照——Sim 惰性建后下一快照即正常。</summary>
        private void OnSnapshot(Proto.StateSnapshot snapshot)
        {
            LastSnapshotFrame = snapshot.Frame;
            if (_sim == null) return;   // Sim 未建：丢弃（Reliable 的 StartGame 随后即到，下一快照恢复正常）

            if (LocalEntityId == 0 && Client.PlayerId >= 0)
            {
                foreach (var slot in snapshot.Slots)
                {
                    if (slot.Slot == Client.PlayerId) { LocalEntityId = slot.Id; break; }
                }
            }

            _mirror = _mirror ?? new SimWorldState();
            SnapshotReassembler.Apply(snapshot, _mirror, out uint checksum);
            bool reconciled = _sim.OnAuthoritativeSnapshot(snapshot.Frame, _mirror, checksum);
            if (reconciled)
            {
                MismatchReports++;
                Client.SendMismatch(snapshot.Frame);
            }
            else
            {
                // 预测正确：本地态不被权威覆盖——但弹药/技能 CD/背包/比赛状态这些**不预测量**
                // 只能从快照来，必须随每包刷新（P0 分层应用，否则 HUD 私有面永远停在初值）
                SnapshotReassembler.OverlayPrivateAndMatch(snapshot, _sim.State);
            }
        }

        /// <summary>
        /// 释放客户端与**其传输**：本 harness 是传输的创建者（生产路径里 KcpNetworkService 才是所有者，
        /// 所以 `RoomClient.Dispose` 不再代管传输——见其类注释）。
        /// </summary>
        public void Dispose()
        {
            Client.Dispose();
            _transport.Dispose();
        }
    }
}
