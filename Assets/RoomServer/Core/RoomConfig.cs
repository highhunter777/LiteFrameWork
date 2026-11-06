using LiteSync.Shared;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间装配参数（M10 架构审查·建议 2/3 收口，2026-09-19）： 把"房间号 / 端口 / 期望人数 / seed 策略"集中到一处——房间容量不再写死。
    /// </summary>
    public sealed class RoomConfig
    {
        /// <summary>房间号（无 MatchMaker：预置单房间注册）。</summary>
        public string RoomId = "Room-A";

        /// <summary>期望人数——决定席位表 / 输入槽 / 实体 Id 表的定容；达此数后再入座被拒（"房间已满"）。
        /// 开局由房主触发（满员/未满皆可——取消满员自动开局）。</summary>
        public int ExpectedPlayers = 2;

        /// <summary>传输端口（Host 侧消费）。</summary>
        public int Port = 17777;

        /// <summary>
        /// 世界 seed 策略：**0 = 开局时由房间从 Tick 注入的单调时间派生**（下发客户端，§4.5）； 非 0 = 固定 seed（确定性回放 / 验收脚本）。
        /// </summary>
        public long Seed = 0;

        // ---- Match 生命周期时限（§9.1 超时策略；0 = 不限时，默认保持无时限的 MVP 形态）----

        /// <summary>对局时限（毫秒）：Running 期间由 Tick 驱动，到点 → Finishing（Reason=TimeLimit）。0 = 不限时。</summary>
        public long MatchTimeLimitMs = 0;

        /// <summary>等待玩家超时（毫秒）：WaitingForPlayers 超时 → Aborted（Reason=WaitingTimeout）。0 = 不限时。</summary>
        public long WaitingTimeoutMs = 0;

        /// <summary>玩法规则；创建时复制到 Runtime，再通过开局/公共快照下发。</summary>
        public LiteSim.MatchRules Rules = LiteSim.MatchRules.Default;

        /// <summary>
        /// 判定地图（**必填注入**——D22：回退位已删）。
        /// </summary>
        public LiteSim.SimMapData Map;

        /// <summary>
        /// 本房地图行键（多图批；`Map/tbmap` 行键）。
        /// </summary>
        public int MapId;

        /// <summary>
        /// 快照广播频率（Hz，房级参数）：权威循环每 <c>TickRate / 本值</c> 个逻辑帧广播一次。
        /// </summary>
        public int SnapshotHz = ProtocolConstants.DefaultSnapshotHz;

        /// <summary>
        /// AOI 广播裁剪参数（房级，只影响广播、不影响判定与回滚重放）。
        /// 缺省 = <see cref="AoiSettings.Default"/>；按房间覆盖时经构造校验（坏值裸抛）。
        /// </summary>
        public AoiSettings Aoi = AoiSettings.Default;

        public static RoomConfig Default() => new RoomConfig();
    }
}
