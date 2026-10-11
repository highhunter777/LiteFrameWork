using System;
using System.Net.Http;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 房间侧"后台 HTTP 提交/心跳"两个客户端（<see cref="LobbyRegistrationClient"/>、
    /// <see cref="SettlementSubmitService"/>）的**共用机制单源**（P2 单源收口）。
    ///
    /// **为什么要有它**：这两个类是同一套骨架的两份手抄——超时默认值、`Join` 等待上限、
    /// 失败日志节流节奏、Bearer 头构造、构造期校验口径，都在两处各写一遍。骨架手抄的真实代价
    /// 不是冗长，而是**单边调参**：改一处默认超时/节流节奏，另一条链静默保持旧值，
    /// 表现为"同为房间侧后台客户端，行为却不同"。
    ///
    /// 本类只收**值常量与无状态机制**；保留在各自类里的部分（线程循环策略、退避算法、载荷形状、
    /// 快照回调）是两条链**真正的差异**，不强行合并（合并它们会把差异藏进 if）。
    /// </summary>
    internal static class HttpWorkerSupport
    {
        /// <summary>单次请求超时缺省（毫秒）：配置未给或非正数时兜底。</summary>
        public const int DefaultRequestTimeoutMs = 5000;

        /// <summary>停止后台线程时的有界等待（毫秒）：不因网络卡死拖住进程退出。</summary>
        public const int StopJoinTimeoutMs = 2000;

        /// <summary>失败日志节流步长：首败必报，其后每 N 次一行（不刷屏也不静默）。</summary>
        public const int FailureReportEvery = 20;

        /// <summary>建 HTTP 客户端（handler 可注入：L1 用伪造 handler 断言载荷与失败路径）。</summary>
        public static HttpClient NewClient(HttpMessageHandler handler, int requestTimeoutMs)
        {
            var http = handler != null ? new HttpClient(handler) : new HttpClient();
            http.Timeout = TimeSpan.FromMilliseconds(
                requestTimeoutMs > 0 ? requestTimeoutMs : DefaultRequestTimeoutMs);
            return http;
        }

        /// <summary>挂实例密钥 Bearer（**Base64 原文**编码——与 Meta 侧配置同值；密钥不进日志）。</summary>
        public static void AddBearer(HttpRequestMessage request, byte[] instanceKey)
        {
            request.Headers.TryAddWithoutValidation("Authorization",
                "Bearer " + Convert.ToBase64String(instanceKey));
        }

        /// <summary>失败日志节流判据（首败 + 每 <see cref="FailureReportEvery"/> 次）。</summary>
        public static bool ShouldReportFailure(int consecutiveFailures)
        {
            return consecutiveFailures == 1 || consecutiveFailures % FailureReportEvery == 0;
        }

        /// <summary>有界停止等待（幂等；线程 null 直接返回）。</summary>
        public static void JoinBounded(Thread thread)
        {
            thread?.Join(TimeSpan.FromMilliseconds(StopJoinTimeoutMs));
        }
    }
}
