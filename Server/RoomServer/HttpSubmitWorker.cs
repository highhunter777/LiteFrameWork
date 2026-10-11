using System;
using System.Net.Http;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 房间侧"后台 HTTP 客户端"骨架（P2 批二：**模板方法**收口）：
    /// 构造校验 → <c>ManualResetEventSlim</c> 停止位 → 专用后台线程 → 有界停止 → 释放。
    ///
    /// **为什么要有它**：<see cref="LobbyRegistrationClient"/>（实例心跳）与
    /// <see cref="SettlementSubmitService"/>（结算提交）此前是同一套骨架的两份手抄——启动/停止/
    /// 释放/构造校验逐行同构，只有**循环策略**不同（前者定间隔心跳，后者排空式消费 + 指数退避）。
    /// 骨架手抄的代价不是冗长，而是**单边修改**：给一个客户端补上"停止后再 Start 不许复活"
    /// 或"释放顺序"，另一个静默保持旧行为。
    ///
    /// **收口边界**：本类只管**线程生命周期与公共校验**；循环策略留在子类的 <see cref="Loop"/>，
    /// 载荷形状/退避算法/快照回调也留在子类——那是两条链真正的差异，收进来只会变成一堆 if。
    /// 值常量与无状态机制（超时兜底/节流节奏/Bearer/有界等待）见 <see cref="HttpWorkerSupport"/>。
    ///
    /// **线程纪律**：单在途请求、无队列无堆积；<c>IsBackground</c>（不阻塞进程退出）；
    /// 停止走 <see cref="Stop"/>（置位 + 有界 Join），重复调用幂等。
    /// </summary>
    public abstract class HttpSubmitWorker : IDisposable
    {
        /// <summary>HTTP 客户端（子类发请求用；超时由构造时的 <c>requestTimeoutMs</c> 决定）。</summary>
        protected readonly HttpClient Http;

        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private Thread _thread;
        private bool _disposed;

        /// <param name="url">端点完整地址（必须绝对 http/https）。</param>
        /// <param name="urlErrorLabel">URL 校验失败时的文案前缀（如"Lobby 注册 URL"——保留各链自有措辞）。</param>
        /// <param name="instanceKey">实例密钥（非空；Bearer 原文以其 Base64 串编码）。</param>
        /// <param name="threadName">后台线程名（诊断/调试器可见）。</param>
        /// <param name="requestTimeoutMs">单次请求超时（&lt;=0 走 <see cref="HttpWorkerSupport.DefaultRequestTimeoutMs"/>）。</param>
        /// <param name="handler">HTTP handler（L1 注入伪造 handler 断言载荷与失败路径）。</param>
        protected HttpSubmitWorker(string url, string urlErrorLabel, byte[] instanceKey,
            string threadName, int requestTimeoutMs, HttpMessageHandler handler)
        {
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException(urlErrorLabel + " 必须是绝对 http/https 地址", "settings");
            if (instanceKey == null || instanceKey.Length == 0)
                throw new ArgumentException("实例密钥不能为空", "settings");

            Http = HttpWorkerSupport.NewClient(handler, requestTimeoutMs);
            ThreadName = threadName;
        }

        /// <summary>后台线程名（子类可覆盖以带实例标识）。</summary>
        protected string ThreadName { get; }

        /// <summary>停止已请求（子类循环判据——替代各自的 <c>_stop.IsSet</c>）。</summary>
        protected bool StopRequested
        {
            get { return _stop.IsSet; }
        }

        /// <summary>可中断等待（子类节拍用）；返回 true = 已请求停止。</summary>
        protected bool WaitStop(int millisecondsTimeout)
        {
            return _stop.Wait(millisecondsTimeout);
        }

        /// <summary>后台循环体（子类实现：循环策略是两条链真正的差异）。</summary>
        protected abstract void Loop();

        /// <summary>启动后台循环（重复调用无操作；已释放则不启动）。</summary>
        public void Start()
        {
            if (_disposed) return;
            if (_thread != null) return;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = ThreadName,
            };
            _thread.Start();
        }

        /// <summary>停止后台循环（置停止位 + **有界等待**——不因网络卡死拖住进程退出；幂等）。</summary>
        public void Stop()
        {
            _stop.Set();
            Thread thread = _thread;
            _thread = null;
            HttpWorkerSupport.JoinBounded(thread);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            Http.Dispose();
            _stop.Dispose();
        }
    }
}
