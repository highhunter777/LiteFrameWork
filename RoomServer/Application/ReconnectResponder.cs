using LiteNet.Protocol;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 重连响应构建（《服务端总设计》§5.6 首选路径 + §9.3 恢复顺序的服务器侧，步骤 2–5）：
    /// 版本确认（Seed/ConfigHash/BuildHash）→ 公共全量快照 → 本人私有状态 → 输入历史。
    ///
    /// **单一实现**：宿主 owner 形态（直驱路径）与 Worker 执行形态（Outbound 回传路径）共用本方法——
    /// 两种形态下都在**持有权威态的单线程**上调用（前者宿主主线程，后者房间 Worker），
    /// 因而对 AuthSim/Gate 的读取天然无并发。响应里的快照是**独立探测**（不推进广播基线）。
    /// </summary>
    internal static class ReconnectResponder
    {
        /// <summary>构建成功响应（调用方已完成票据/席位/房间状态校验）。</summary>
        public static Proto.ReconnectResponse Build(RoomRuntime room, int playerId, string buildHash)
        {
            var response = new Proto.ReconnectResponse { Ok = true };
            long viewerEntityId = room.EntityIdOf(playerId);
            response.Seed = room.Seed;
            response.ConfigHash = room.FixedConfig.Digest;
            response.BuildHash = buildHash;
            response.Snapshot = SnapshotCodec.PackFull(room.AuthSim.Frame, room.AuthSim, room.Gate.LastAcceptedFrame(playerId));
            response.Snapshot.PrivateState = SnapshotCodec.PackPrivate(room.AuthSim, viewerEntityId);
            for (int f = room.AuthSim.Frame - SimConfig.MaxInputHistory + 1; f <= room.AuthSim.Frame; f++)
            {
                if (f <= 0) continue;
                // 重连补发是"每帧一条"的完整历史（不是丢包冗余窗），viewFrame 无意义填 0；
                // 帧号字段随 Pack 写入，客户端按 frame 逐帧取用即可
                if (room.HistoryFor(f, out SimInputFrame[] inputs))
                    response.History.Add(InputPacker.Pack(f, inputs, room.AuthSim.Frame, 0));
            }
            return response;
        }
    }
}