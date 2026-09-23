namespace RoomServer.Runtime
{
    /// <summary>
    /// 房间装配参数（M10 架构审查·建议 2/3 收口，2026-09-19）：
    /// 把"房间号 / 端口 / 期望人数 / seed 策略"集中到一处——房间容量不再写死，
    /// 4 人房（M11 demo §9 验收）与验收脚本并行跑不同端口都只改配置，不散改代码。
    ///
    /// R1 归属说明：本类随 Runtime 层落位（<c>RoomServer.Runtime</c>），由 Host 与 Application 消费；
    /// <see cref="Port"/> 是 Host 侧字段（Runtime 不读），随 R2 Generic Host/Options 拆分再上移。
    ///
    /// 默认值 = M10 MVP 形态（Room-A / 17777 / 2 人 / seed 随机）。
    /// </summary>
    public sealed class RoomConfig
    {
        /// <summary>房间号（无 MatchMaker：预置单房间注册）。</summary>
        public string RoomId = "Room-A";

        /// <summary>期望人数——决定席位表 / 输入槽 / 实体 Id 表的定容；满员自动 StartGame。</summary>
        public int ExpectedPlayers = 2;

        /// <summary>传输端口（Host 侧消费）。</summary>
        public int Port = 17777;

        /// <summary>
        /// 世界 seed 策略：**0 = 开局时由房间从 Tick 注入的单调时间派生**（下发客户端，§4.5）；
        /// 非 0 = 固定 seed（确定性回放 / 验收脚本）。
        ///
        /// R1 变更：原先 seed=0 时兜底读 <c>DateTime.Now</c>（系统墙钟——Runtime 禁止项）；
        /// 现改为读最近一次 Tick 命令携带的 nowMs（App 由 IMonotonicClock 派生注入），
        /// 运行期不再触碰系统时钟。二者皆为"随机的非零种子"，语义等价。
        /// </summary>
        public long Seed = 0;

        // ---- Match 生命周期时限（§9.1 超时策略；0 = 不限时，默认保持无时限的 MVP 形态）----

        /// <summary>对局时限（毫秒）：Running 期间由 Tick 驱动，到点 → Finishing（Reason=TimeLimit）。0 = 不限时。</summary>
        public long MatchTimeLimitMs = 0;

        /// <summary>等待玩家超时（毫秒）：WaitingForPlayers 超时 → Aborted（Reason=WaitingTimeout）。0 = 不限时。</summary>
        public long WaitingTimeoutMs = 0;

        public static RoomConfig Default() => new RoomConfig();
    }
}
