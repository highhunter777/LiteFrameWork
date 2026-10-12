using System;
using System.Net;
using System.Text;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// /metrics 零依赖文本出口（R4 第一批）：HttpListener + 单后台线程（宿主控制台形态）。
    /// 格式化单源在 <c>Ops.FormatMetrics</c>——出口本体可被 R4 后续的 Kestrel/OTel 面**整体替换**，
    /// 计数面与调用方零改动（同 MetaServer Ops 先例的低成本文本出口裁决）。
    ///
    /// 刻意只认 GET /metrics：诊断端点不提供任何可写面（§P0-6 最小暴露）；其余路径一律 404。
    /// **关闭语义**：Stop 解除 GetContext 阻塞、服务线程退出；Dispose 幂等。
    /// **线程契约**：formatter 由调用方保证线程安全——Ops 读面全 Interlocked.Read（P4），已满足；
    /// 服务线程单条请求失败不杀出口（吞掉继续），Stop/Close 异常即退出循环。
    /// **绑定面**：缺省 <c>+</c>（全接口，跨机抓取）；Windows 非管理员绑 <c>+</c> 需 urlacl——
    /// Program 装配处对 Start 失败走**告警降级**，不让可观测面打断宿主可用性
    /// （裁决登记在 RoomServerConfig.MetricsPort 注释）；用例/本机无权限形态用 <c>localhost</c> 绑定。
    /// </summary>
    public sealed class MetricsHttpListener : IDisposable
    {
        private readonly Func<string> _formatMetrics;
        private HttpListener _listener;
        private Thread _thread;

        public MetricsHttpListener(Func<string> formatMetrics)
            => _formatMetrics = formatMetrics ?? throw new ArgumentNullException(nameof(formatMetrics));

        /// <summary>在指定端口启动指标出口（重复启动抛；端口范围由调用方（配置装载）校验）。
        /// <paramref name="host"/> = 绑定面（缺省 <c>+</c>；测试用 <c>localhost</c> 避开 urlacl 依赖）。</summary>
        public void Start(int port, string host = "+")
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "端口越界（允许 1..65535）");
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("绑定面不可为空（如 + / localhost / 具体地址）", nameof(host));
            if (_listener != null)
                throw new InvalidOperationException("metrics 出口已启动（一个宿主一个出口）");

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{host}:{port}/");
            listener.Start();
            _listener = listener;
            _thread = new Thread(ServeLoop) { IsBackground = true, Name = "RoomServer.Metrics" };
            _thread.Start();
        }

        private void ServeLoop()
        {
            HttpListener listener = _listener;
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; }                        // Stop/Close 解除阻塞——退出

                try
                {
                    HttpListenerResponse resp = ctx.Response;
                    if (string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(ctx.Request.Url.AbsolutePath, "/metrics", StringComparison.Ordinal))
                    {
                        byte[] body = Encoding.UTF8.GetBytes(_formatMetrics());
                        resp.StatusCode = 200;
                        resp.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                        resp.ContentLength64 = body.LongLength;
                        resp.OutputStream.Write(body, 0, body.Length);
                    }
                    else
                    {
                        resp.StatusCode = 404;                      // 只认 GET /metrics——其余一律 404
                    }
                }
                catch (Exception) { /* 单请求失败不杀出口线程 */ }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        public void Dispose()
        {
            HttpListener listener = _listener;
            _listener = null;
            try { listener?.Stop(); } catch { }                      // 解除 GetContext 阻塞
            try { listener?.Close(); } catch { }
            _thread = null;                                         // 后台线程自然退出（IsBackground）
        }
    }
}
