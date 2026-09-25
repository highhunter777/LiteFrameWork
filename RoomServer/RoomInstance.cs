using RoomServer.Application;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>
    /// 一个房间的**全部宿主侧状态**（《商业级通用服务端框架总设计》§6"一个 roomId 只能映射一个独立
    /// RoomActor"、§8.2"一个 Worker 顺序驱动多个 RoomActor"）。
    ///
    /// 为什么独立成类：在此之前 RoomRuntime / 快照管线 / 席位表是 <see cref="ServerHost"/> 上的三个
    /// **并列字段**，"每房间一份"这件事在类型上无处表达——加第二个房间就得让每个字段各复制一遍。
    /// 收成一个对象后，多房间 = 一张 roomId → RoomInstance 的表，"每房间一份"由类型保证。
    ///
    /// **单线程约束**：本类不自我同步。驱动它的 Worker 必须单线程（§8.2"每个 RoomActor 仍是单线程"）
    /// ——与 <see cref="RoomRuntime"/> 同一纪律。当前实现由宿主主线程驱动（单 Worker）。
    ///
    /// **所有权**：<see cref="Seats"/> 的会话引用由宿主在席位变更时写入（见
    /// <see cref="ServerHost"/> 的 ApplySignal / HandleReconnect）；房间本身不创建也不释放会话。
    /// </summary>
    internal sealed class RoomInstance
    {
        /// <summary>房间号（= <see cref="RoomConfig.RoomId"/>；路由键，创建后不变）。</summary>
        public readonly string RoomId;

        /// <summary>权威运行时（Sim + 闸门 + 席位）。</summary>
        public readonly RoomRuntime Runtime;

        /// <summary>快照管线（增量差分 + 广播节流 + 背压降档）。</summary>
        public readonly SnapshotPipeline Pipeline;

        /// <summary>席位 → 连接会话映射（广播与信号按此定位连接；与 Runtime 的席位一一对应）。</summary>
        public readonly Session[] Seats;

        /// <summary>本房间的固定配置（创建时定，此后不变——§P0-5/§4"房间创建时固定不可变玩法配置"）。</summary>
        public readonly RoomConfig Config;

        /// <summary>
        /// 本房间的席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。
        /// **按房间传**——多房间下快照管线必须看自己房间的席位，不能共用宿主的全局判定。
        /// </summary>
        public bool SeatBroadcastable(int playerId)
        {
            return Runtime.TryGetSeat(playerId, out PlayerSession seat) && seat.Phase == SeatPhase.Active;
        }

        public RoomInstance(RoomConfig config)
        {
            Config = config;
            RoomId = config.RoomId;
            Runtime = new RoomRuntime(config);
            Seats = new Session[Runtime.ExpectedPlayers];
            Pipeline = new SnapshotPipeline(Seats);
        }
    }
}
