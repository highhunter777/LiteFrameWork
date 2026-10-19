using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>提交步进结果（后台循环与测试直调共用）。</summary>
    public enum SettlementSubmitStep
    {
        /// <summary>无待提交条目（空闲）。</summary>
        Idle = 0,

        /// <summary>头部条目已提交并被 Meta 接纳（**2xx**——含 duplicate 幂等命中），已标记完成。</summary>
        Submitted = 1,

        /// <summary>提交失败（可重试面：传输异常 / 401·403·404 / 408·429 / 5xx）；条目保持待提交（重试安全）。</summary>
        Failed = 2,

        /// <summary>确定性拒绝（载荷级 4xx：400/409/413/422——重投同一载荷不可能收敛）：条目已**弃置**
        /// （Outbox 弃置标记行）并让位后续——不重投（设计 §2"提交失败处置分级"）。</summary>
        Abandoned = 3,
    }

    /// <summary>
    /// 结算提交管道（《上云测试专项设计》§2"后台服务读本地 Outbox → Meta 幂等端点 → 重试/退避/
    /// 完成标记/重启续投"；《Meta 服务专项设计》§8.2 结算链的 RoomServer 半边）。
    ///
    /// - **只读日志、不改日志语义**：逐条取 <see cref="ISettlementOutbox.ListPending"/> 头部 → POST
    ///   Meta `/matches/result`（结算实例密钥 Bearer——**与 Lobby 注册分权**，见 P1②）→ **2xx** 即
    ///   <see cref="ISettlementOutbox.TryMarkCompleted"/>（Meta 侧幂等是最终裁判：duplicate 同样算完成）；
    /// - **重启续投**：不做任何内存进度记账——启动即从日志待提交面重建（引擎语义在 Outbox 里）；
    /// - **失败处置分级（设计 §2"提交失败处置分级"）**：**可重试失败**（传输异常、401·403·404、
    ///   408·429、5xx）保持待提交 + 指数退避封顶（<see cref="Settings.MaxBackoffMs"/>）——不静默丢弃、
    ///   不无界刷屏（日志节流）；**确定性拒绝**（载荷级 4xx：400/409/413/422）**弃置**并让位
    ///   （Outbox 弃置标记行 + <see cref="Abandoned"/> 计数 + 警告日志）——防"一行毒死整条队列"；
    /// - **结算增量（P3：占位改显式接缝）**：换算归提交方（Meta 不接受客户端上报、也不重算），
    ///   经 <see cref="ISettlementRewardPolicy"/> 单点注入；当前实现 = <see cref="NoEconomyRewardPolicy"/>
    ///   （逐玩家 delta 0：账本 revision 推进、余额不变）。**不是"忘了填的 0"**：设计未定义奖励口径
    ///   （无字段可依），凭空定数等于把估算当事实——经济口径落地时换一个实现即可，管道零改动。
    ///
    /// HTTP handler 可注入——L1 用伪造 handler 断言载荷、完成标记与失败保持。
    /// </summary>
    public sealed class SettlementSubmitService : HttpSubmitWorker
    {
        /// <summary>管道参数（Program 从 RoomServerConfig + 环境密钥组装）。</summary>
        public sealed class Settings
        {
            /// <summary>Meta 结算提交端点完整 URL（如 <c>https://meta.example.com/matches/result</c>）。</summary>
            public string Url;

            /// <summary>**结算**实例密钥（Bearer 原文以其 Base64 串编码——与 Meta 的
            /// <c>SettlementInstanceKeyBase64</c> 同值；**与 Lobby 注册凭据分权**，P1②）。</summary>
            public byte[] InstanceKey;

            /// <summary>空闲轮询间隔（毫秒）。</summary>
            public int IntervalMs;

            /// <summary>连续失败退避上限（毫秒）。</summary>
            public int MaxBackoffMs;

            /// <summary>单次请求超时（毫秒）。</summary>
            public int RequestTimeoutMs;

            /// <summary>奖励换算策略（缺省 <see cref="NoEconomyRewardPolicy"/> = 逐玩家 delta 0；
            /// 经济口径落地后注入真实实现——见 <see cref="ISettlementRewardPolicy"/>）。</summary>
            public ISettlementRewardPolicy RewardPolicy;
        }

        private sealed class SubmitPlayer
        {
            public string PlayerId { get; set; }
            public string AccountId { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }

            /// <summary>奖励增量（由 <see cref="ISettlementRewardPolicy"/> 换算；当前无经济口径 → 0）。</summary>
            public long Delta { get; set; }

            /// <summary>追加模式（提交方不持有玩家修订号——幂等由 Meta 账本唯一索引承载）。</summary>
            public long ExpectedRevision { get; set; } = -1L;

            /// <summary>缺省 null：服务端按 match:{matchId}:{playerId} 确定性派生。</summary>
            public string OperationId { get; set; }
        }

        private sealed class SubmitPayload
        {
            public string RequestId { get; set; }
            public string MatchId { get; set; }
            public long Seed { get; set; }
            public int FinalFrame { get; set; }
            public int EndReason { get; set; }
            public int GameplayEndReason { get; set; }
            public long WinnerEntityId { get; set; }
            public SubmitPlayer[] Players { get; set; }
        }

        private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly Settings _settings;
        private readonly ISettlementOutbox _outbox;
        private readonly Action<string> _log;
        private readonly ISettlementRewardPolicy _rewardPolicy;
        private long _submitted;
        private long _failed;
        private long _abandoned;
        private int _consecutiveFailures;

        public SettlementSubmitService(Settings settings, ISettlementOutbox outbox, Action<string> log,
            HttpMessageHandler handler = null)
            // URL/密钥校验与线程骨架归基类（P2 批二）；实参序保证 settings 的 null 校验先于其余取值
            : base(CheckedUrl(settings), "结算提交 URL", CheckedKey(settings), "SettlementSubmit",
                   settings?.RequestTimeoutMs ?? 0, handler)
        {
            if (settings.IntervalMs < 1000)
                throw new ArgumentException("空闲轮询间隔不得低于 1000ms", nameof(settings));

            _settings = settings;
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _rewardPolicy = settings.RewardPolicy ?? NoEconomyRewardPolicy.Instance;   // 缺省 = 无经济口径
        }

        /// <summary>URL 取值（含 settings 的 null 前置校验——在 <c>base(...)</c> 实参序最前）。</summary>
        private static string CheckedUrl(Settings settings)
            => (settings ?? throw new ArgumentNullException(nameof(settings))).Url;

        private static byte[] CheckedKey(Settings settings) => settings.InstanceKey;

        /// <summary>已被 Meta 接纳的条目数（含 duplicate 幂等命中；诊断/测试断言）。</summary>
        public long Submitted
        {
            get { return Interlocked.Read(ref _submitted); }
        }

        /// <summary>失败次数（诊断；持续增长说明 Meta 不可达或凭据/部署配置不符）。</summary>
        public long Failed
        {
            get { return Interlocked.Read(ref _failed); }
        }

        /// <summary>弃置条数（确定性拒绝终态处置；增长 = 载荷侧有实缺陷，须人工查）。</summary>
        public long Abandoned
        {
            get { return Interlocked.Read(ref _abandoned); }
        }

        /// <summary>当前待提交条数（= Outbox 待提交面）。</summary>
        public int PendingCount
        {
            get { return _outbox.Count; }
        }

        /// <summary>提交循环（骨架：启动/停止/释放归 <see cref="HttpSubmitWorker"/>；此处只留策略——
        /// 排空式消费 + 失败指数退避）。</summary>
        protected override void Loop()
        {
            while (!StopRequested)
            {
                SettlementSubmitStep step = TrySubmitNext(out _);
                switch (step)
                {
                    case SettlementSubmitStep.Submitted:
                        continue;                                  // 立刻下一单（排空式消费）
                    case SettlementSubmitStep.Abandoned:
                        continue;                                  // 弃置即让位——立刻试下一条（防头阻塞）
                    case SettlementSubmitStep.Idle:
                        WaitStop(_settings.IntervalMs);
                        break;
                    default:
                        // 连续失败计数归 RecordFailure（全路径唯一递增点）
                        WaitStop(BackoffMs(_consecutiveFailures, _settings.IntervalMs, _settings.MaxBackoffMs));
                        break;
                }
            }
        }

        /// <summary>
        /// 单步提交（周期循环与测试直调共用）：取待提交头部 → POST → 200 标记完成。
        /// 失败不抛——条目保持待提交（fail-open 于对局服务，只影响落库时延）。
        /// </summary>
        public SettlementSubmitStep TrySubmitNext(out string error)
        {
            error = null;
            try
            {
                var pending = _outbox.ListPending();
                if (pending.Count == 0) return SettlementSubmitStep.Idle;

                PendingSettlement entry = pending[0];
                using var request = new HttpRequestMessage(HttpMethod.Post, _settings.Url);
                HttpWorkerSupport.AddBearer(request, _settings.InstanceKey);   // Bearer 构造单源（P2）
                request.Content = new StringContent(
                    JsonSerializer.Serialize(BuildPayload(entry), WebJson), Encoding.UTF8, "application/json");

                // **带停止令牌**（关闭链批）：Stop 时取消在途请求，让"有界等待"真的能等到线程退出
                // （否则线程停在超时请求里，Dispose 会在存活线程上释放停止位 → 未处理异常杀进程）。
                using HttpResponseMessage response = Http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, StopToken)
                    .GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    // **2xx 区间**（P1①）：只认 200 会把同义的 201/202/204 当失败反复重投。
                    // 非 2xx 的分级处置见下方（设计 §2"提交失败处置分级"：可重试保持待提交，载荷级 4xx 弃置让位）。
                    // Meta 已接纳（首次或幂等命中）——标记完成；标记失败（介质故障）则保持待提交，
                    // 重投时 Meta duplicate 兜底（不重复落库）
                    if (!_outbox.TryMarkCompleted(entry.MatchId))
                    {
                        error = "标记完成失败（介质）";
                        RecordFailure(error);
                        return SettlementSubmitStep.Failed;
                    }

                    Interlocked.Increment(ref _submitted);
                    _consecutiveFailures = 0;
                    return SettlementSubmitStep.Submitted;
                }

                error = "HTTP " + (int)response.StatusCode;
                if (IsDeterministicRejection(response.StatusCode))
                {
                    // 载荷级 4xx：重投同一载荷不可能收敛——弃置并让位（原实现下永驻队首、阻塞全部）。
                    // 介质写失败 → 按可重试失败处理（事实不丢：条目仍在待提交面）。
                    if (_outbox.TryAbandon(entry.MatchId, error))
                    {
                        Interlocked.Increment(ref _abandoned);
                        _consecutiveFailures = 0;          // 弃置 ≠ 端点不健康——不推高退避
                        _log("[Settle] 确定性拒绝（" + error + "）→ 弃置条目 " + entry.MatchId
                            + "（让位后续条目；载荷级拒绝重投不收敛）");
                        return SettlementSubmitStep.Abandoned;
                    }
                    error += "（弃置标记写入失败）";
                }
                RecordFailure(error);
                return SettlementSubmitStep.Failed;
            }
            catch (Exception ex)
            {
                // 网络面故障是预期路径；异常消息不含密钥
                error = ex.GetType().Name + ": " + ex.Message;
                RecordFailure(error);
                return SettlementSubmitStep.Failed;
            }
        }

        /// <summary>载荷组装（测试经伪造 handler 捕获 JSON 断言字段形状与账号映射对齐）。</summary>
        private SubmitPayload BuildPayload(PendingSettlement entry)
        {
            MatchResultSummary summary = entry.Summary;
            PlayerMatchResult[] results = summary.Players;
            var players = new SubmitPlayer[results.Length];
            for (int i = 0; i < players.Length; i++)
            {
                string accountId = i < entry.SeatAccountIds.Length ? entry.SeatAccountIds[i] : string.Empty;
                players[i] = new SubmitPlayer
                {
                    PlayerId = results[i].PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AccountId = accountId ?? string.Empty,
                    Kills = results[i].Kills,
                    Deaths = results[i].Deaths,
                    // 唯一的奖励换算点（P3：占位改显式接缝）——仅在此调用策略，别处不得自算 delta
                    Delta = _rewardPolicy.RewardDelta(results[i].PlayerId, results[i].Kills, results[i].Deaths),
                };
            }
            return new SubmitPayload
            {
                RequestId = "submit:" + summary.MatchId,
                MatchId = summary.MatchId,
                Seed = summary.Seed,
                FinalFrame = summary.FinalFrame,
                EndReason = (int)summary.EndReason,
                GameplayEndReason = (int)summary.GameplayEndReason,
                WinnerEntityId = summary.WinnerEntityId,
                Players = players,
            };
        }

        /// <summary>
        /// 确定性拒绝判据（设计 §2"提交失败处置分级"）：**载荷级 4xx**——重投同一载荷不可能收敛，
        /// 弃置让位。401/403/404（凭据/部署面）与 408/429、5xx 一律可重试：那些是运维可修复状态，
        /// 弃置会把"修好部署后本可收敛"的结算静默丢账。
        /// </summary>
        private static bool IsDeterministicRejection(System.Net.HttpStatusCode code)
        {
            int c = (int)code;
            return c == 400 || c == 409 || c == 413 || c == 422;
        }

        /// <summary>指数退避（首次失败 = 基础间隔；封顶 <paramref name="maxMs"/>）。</summary>
        internal static int BackoffMs(int consecutiveFailures, int baseMs, int maxMs)
        {
            int shift = Math.Min(Math.Max(consecutiveFailures - 1, 0), 16);
            long ms = (long)baseMs << shift;
            return (int)Math.Min(ms, maxMs);
        }

        private void RecordFailure(string error)
        {
            Interlocked.Increment(ref _failed);
            int consecutive = Interlocked.Increment(ref _consecutiveFailures);
            if (HttpWorkerSupport.ShouldReportFailure(consecutive))   // 节流节奏单源（P2）
            {
                _log("[Settle] 提交失败（连续 " + consecutive + " 次）：" + error
                    + "（条目保持待提交；对局服务不受影响）");
            }
        }

        /// <summary>停止超时（线程未在有界等待内退出）：如实告警——此时停止位/HttpClient 有意不释放
        /// （释放会让存活线程抛未处理异常），见 <see cref="HttpSubmitWorker.Dispose"/>。
        /// 条目仍在 Outbox 日志（write-through），下次启动续投。</summary>
        protected override void OnStopTimeout()
        {
            _log("[Settle] 停止超时：提交线程未在 " + HttpWorkerSupport.StopJoinTimeoutMs
                + "ms 内退出（在途请求未返回）——不释放其停止位/HttpClient，避免关闭阶段未处理异常");
        }

    }
}
