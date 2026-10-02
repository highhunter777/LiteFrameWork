using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// EditMode 测试用**本地 HTTP 监听**（真实传输载体，替"外部 CDN"）——
    /// 走 UnityWebRequest 的 TCP→HTTP 全链路，不是替身：能抓到替身测试断言不了的
    /// 传输层与 PlayerLoop 依赖问题。异步用例仍须 [UnityTest] + IEnumerator
    /// （EditMode 无 PlayerLoop，SendWebRequest 的续延由 Test Framework 在帧间推进）。
    ///
    /// 路由按路径字典应答；**未注册路径一律 404**（与真实 CDN"无该资源"同语义）。
    /// <see cref="RouteFlaky"/> 支持"前 N 次请求回 500 后成功"——重试/换源语义的故障注入锚点；
    /// <see cref="HitCount"/> 供断言"实际尝试了几次"（重试上限/确定性失败即止的验证依据）。
    /// </summary>
    public sealed class LocalHttpTestServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Dictionary<string, (int status, byte[] body)> _routes =
            new Dictionary<string, (int, byte[])>();
        private readonly Dictionary<string, int> _hits = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _failTimes = new Dictionary<string, int>();

        /// <summary>监听基址（末尾不带 /）。</summary>
        public string BaseUrl { get; }

        public LocalHttpTestServer()
        {
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _listener.BeginGetContext(OnRequest, null);
        }

        /// <summary>注册固定应答（文本体）。</summary>
        public void Route(string path, string content, int status = 200)
            => _routes[path] = (status, Encoding.UTF8.GetBytes(content));

        /// <summary>注册固定应答（原始字节体）。</summary>
        public void Route(string path, byte[] body, int status = 200)
            => _routes[path] = (status, body);

        /// <summary>前 <paramref name="failTimes"/> 次请求回 500（暂态故障注入），之后回 200 文本内容。</summary>
        public void RouteFlaky(string path, string content, int failTimes = 1)
        {
            Route(path, content);
            _failTimes[path] = failTimes;
        }

        /// <summary>该路径已被请求的次数（未请求过 = 0）。</summary>
        public int HitCount(string path) => _hits.TryGetValue(path, out int n) ? n : 0;

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
        }

        private void OnRequest(IAsyncResult ar)
        {
            HttpListener listener = _listener;
            if (listener == null || !listener.IsListening) return;
            try
            {
                HttpListenerContext ctx = listener.EndGetContext(ar);
                listener.BeginGetContext(OnRequest, null);

                string path = ctx.Request.Url.AbsolutePath;
                _hits[path] = HitCount(path) + 1;

                if (_routes.TryGetValue(path, out var route))
                {
                    bool stillFailing = _failTimes.TryGetValue(path, out int failTimes) && _hits[path] <= failTimes;
                    ctx.Response.StatusCode = stillFailing ? 500 : route.status;
                    if (!stillFailing && route.body != null)
                        ctx.Response.OutputStream.Write(route.body, 0, route.body.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
            catch (Exception) { /* 停止时的竞态 */ }
        }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
