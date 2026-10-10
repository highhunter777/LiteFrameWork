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

        /// <summary>头部条目已提交并被 Meta 接纳（200——含 duplicate 幂等命中），已标记完成。</summary>
        Submitted = 1,

        /// <summary>提交失败（HTTP 非 200 / 传输异常）；条目保持待提交（重试安全）。</summary>
        Failed = 2,
    }

    /// <summary>
    /// 结算提交管道（《上云测试专项设计》§2"后台服务读本地 Outbox → Meta 幂等端点 → 重试/退避/
    /// 完成标记/重启续投"；《Meta 服务专项设计》§8.2 结算链的 RoomServer 半边）。
    ///
    /// - **只读日志、不改日志语义**：逐条取 <see cref="ISettlementOutbox.ListPending"/> 头部 → POST
    ///   Meta `/matches/result`（实例密钥 Bearer——与 Lobby 注册共用服务凭据）→ 200 即
    ///   <see cref="ISettlementOutbox.TryMarkCompleted"/>（Meta 侧幂等是最终裁判：duplicate 同样算完成）；
    /// - **重启续投**：不做任何内存进度记账——启动即从日志待提交面重建（引擎语义在 Outbox 里）；
    /// - **重试与退避**：失败条目保持待提交，下一轮重试；连续失败按指数退避封顶
    ///   （<see cref="Settings.MaxBackoffMs"/>）——不静默丢弃、不无界刷屏（日志节流）；
    /// - **结算增量**：封闭测试无经济系统——逐玩家 delta = 0 占位（账本 revision 推进、
    ///   余额不变）；真实 RewardDelta 换算归 P4，换算在提交方完成（Meta 不接受客户端上报）。
    ///
    /// HTTP handler 可注入——L1 用伪造 handler 断言载荷、完成标记与失败保持。
    /// </summary>
    public sealed class SettlementSubmitService : IDisposable
    {
        /// <summary>管道参数（Program 从 RoomServerConfig + 环境密钥组装）。</summary>
        public sealed class Settings
        {
            /// <summary>Meta 结算提交端点完整 URL（如 <c>https://meta.example.com/matches/result</c>）。</summary>
            public string Url;

            /// <summary>实例密钥（Bearer 原文以其 Base64 串编码——与 Meta 配置同值）。</summary>
            public byte[] InstanceKey;

            /// <summary>空闲轮询间隔（毫秒）。</summary>
            public int IntervalMs;

            /// <summary>连续失败退避上限（毫秒）。</summary>
            public int MaxBackoffMs;

            /// <summary>单次请求超时（毫秒）。</summary>
            public int RequestTimeoutMs;
        }

        private sealed class SubmitPlayer
        {
            public string PlayerId { get; set; }
            public string AccountId { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }

            /// <summary>奖励增量（无经济占位 0——见类注释）。</summary>
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
        private readonly HttpClient _http;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private Thread _thread;
        private long _submitted;
        private long _failed;
        private int _consecutiveFailures;
        private bool _disposed;

        public SettlementSubmitService(Settings settings, ISettlementOutbox outbox, Action<string> log,
            HttpMessageHandler handler = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.Url)
                || !Uri.TryCreate(settings.Url, UriKind.Absolute, out Uri parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("结算提交 URL 必须是绝对 http/https 地址", nameof(settings));
            if (settings.InstanceKey == null || settings.InstanceKey.Length == 0)
                throw new ArgumentException("实例密钥不能为空", nameof(settings));
            if (settings.IntervalMs < 1000)
                throw new ArgumentException("空闲轮询间隔不得低于 1000ms", nameof(settings));

            _settings = settings;
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
            _http.Timeout = TimeSpan.FromMilliseconds(settings.RequestTimeoutMs > 0 ? settings.RequestTimeoutMs : 5000);
        }

        /// <summary>已被 Meta 接纳的条目数（含 duplicate 幂等命中；诊断/测试断言）。</summary>
        public long Submitted
        {
            get { return Interlocked.Read(ref _submitted); }
        }

        /// <summary>失败次数（诊断；持续增长说明 Meta 不可达或载荷/凭据不符）。</summary>
        public long Failed
        {
            get { return Interlocked.Read(ref _failed); }
        }

        /// <summary>当前待提交条数（= Outbox 待提交面）。</summary>
        public int PendingCount
        {
            get { return _outbox.Count; }
        }

        /// <summary>启动后台提交循环（启动即刻消费；重复调用无操作）。</summary>
        public void Start()
        {
            if (_disposed) return;
            if (_thread != null) return;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "SettlementSubmit",
            };
            _thread.Start();
        }

        /// <summary>停止提交循环（有界等待——不因网络卡死拖住进程退出；幂等）。</summary>
        public void Stop()
        {
            _stop.Set();
            Thread thread = _thread;
            _thread = null;
            thread?.Join(TimeSpan.FromSeconds(2));
        }

        private void Loop()
        {
            while (!_stop.IsSet)
            {
                SettlementSubmitStep step = TrySubmitNext(out _);
                switch (step)
                {
                    case SettlementSubmitStep.Submitted:
                        continue;                                  // 立刻下一单（排空式消费）
                    case SettlementSubmitStep.Idle:
                        _stop.Wait(_settings.IntervalMs);
                        break;
                    default:
                        // 连续失败计数归 RecordFailure（全路径唯一递增点）
                        _stop.Wait(BackoffMs(_consecutiveFailures, _settings.IntervalMs, _settings.MaxBackoffMs));
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
                request.Headers.TryAddWithoutValidation("Authorization",
                    "Bearer " + Convert.ToBase64String(_settings.InstanceKey));
                request.Content = new StringContent(
                    JsonSerializer.Serialize(BuildPayload(entry), WebJson), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = _http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead)
                    .GetAwaiter().GetResult();

                if ((int)response.StatusCode == 200)
                {
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
        private static SubmitPayload BuildPayload(PendingSettlement entry)
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
                    Delta = 0L,
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
            if (consecutive == 1 || consecutive % 20 == 0)
            {
                _log("[Settle] 提交失败（连续 " + consecutive + " 次）：" + error
                    + "（条目保持待提交；对局服务不受影响）");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _http.Dispose();
            _stop.Dispose();
        }
    }
}
