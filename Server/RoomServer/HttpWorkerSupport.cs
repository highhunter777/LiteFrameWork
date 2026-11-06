using System;
using System.Net.Http;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 房间侧"后台 HTTP 提交/心跳"两个客户端（<see cref="LobbyRegistrationClient"/>。）。
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
