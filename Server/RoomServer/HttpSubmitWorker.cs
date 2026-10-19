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
    /// 停止走 <see cref="Stop"/>（置停止位 + **取消在途请求** + 有界 Join，返回"线程是否已退出"）；
    /// **释放前置条件 = 线程确已退出**——未退出时不释放停止位/HttpClient（见 <see cref="Dispose"/>）。
    /// </summary>
    public abstract class HttpSubmitWorker : IDisposable
    {
        /// <summary>HTTP 客户端（子类发请求用；超时由构造时的 <c>requestTimeoutMs</c> 决定）。</summary>
        protected readonly HttpClient Http;

        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        /// <summary>停止令牌：在途 HTTP 请求带上它，<see cref="Stop"/> 时取消——否则"有界等待"到期后
        /// 线程仍在 5s（可配更长）的请求里，<see cref="Dispose"/> 便会在存活线程上释放停止位。</summary>
        private readonly CancellationTokenSource _stopCancellation = new CancellationTokenSource();

        private Thread _thread;

        /// <summary>停止位（0/1）。**与 <see cref="_stop"/> 并存的第二份真相**：MRES 的 <c>IsSet</c>
        /// 在 Dispose 之后语义随运行时版本变化（实测 .NET 10 仍返回 true、部分版本返回 false），
        /// 而本值不随 Dispose 失效——后台循环的停止判据读它，不读 MRES。</summary>
        private int _stopRequested;

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

        /// <summary>停止已请求（子类循环判据——替代各自的 <c>_stop.IsSet</c>）。
        /// 读**不随 Dispose 失效**的停止位，见 <see cref="_stopRequested"/>。</summary>
        protected bool StopRequested
        {
            get { return Volatile.Read(ref _stopRequested) != 0; }
        }

        /// <summary>停止令牌（在途请求带它：<see cref="Stop"/> 时取消，让"有界等待"真的能等到线程退出）。</summary>
        protected CancellationToken StopToken
        {
            get { return _stopCancellation.Token; }
        }

        /// <summary>可中断等待（子类节拍用）；返回 true = 已请求停止。
        /// Dispose 竞态（停止位已释放）按"已停止"返回——**绝不把 ObjectDisposedException 抛进后台线程**
        /// （后台线程未处理异常 = 进程终止，见 <see cref="Dispose"/>）。</summary>
        protected bool WaitStop(int millisecondsTimeout)
        {
            if (StopRequested) return true;
            try
            {
                return _stop.Wait(millisecondsTimeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
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

        /// <summary>
        /// 停止后台循环：置停止位 + **取消在途请求** + 有界等待（不因网络卡死拖住进程退出；幂等、可重复调用）。
        /// 返回 true = 线程已退出（<see cref="Dispose"/> 据此决定能否释放资源）。
        /// </summary>
        public bool Stop()
        {
            Volatile.Write(ref _stopRequested, 1);
            try { _stopCancellation.Cancel(); } catch (ObjectDisposedException) { }   // 二次 Stop：仅在"线程已退出并已释放"后可能发生
            try { _stop.Set(); } catch (ObjectDisposedException) { }

            Thread thread = _thread;
            if (thread == null) return true;
            HttpWorkerSupport.JoinBounded(thread);
            if (thread.IsAlive) return false;    // **保留线程引用**：超时后再次 Stop 仍能继续等（不得谎报已停）
            _thread = null;
            return true;
        }

        /// <summary>
        /// 释放（幂等）。**线程未退出则不释放停止位与 <see cref="Http"/>**：旧形态无条件
        /// <c>_stop.Dispose()</c>，而请求超时（缺省 5s、可配更长）大于停止等待（2s）时线程仍在跑，
        /// 回到循环后的 <see cref="WaitStop"/> 会在已释放的 MRES 上抛 <see cref="ObjectDisposedException"/>
        /// ——后台线程未处理异常 = **关闭阶段整进程终止**（打断 DI 容器逆序释放、退出码非 0）。
        /// 代价是超时分支**不置 <c>_disposed</c>**：之后再调 Dispose（线程已退出时）仍可完成释放；
        /// 若到进程结束都没再调，则是关闭路径上一次有意保留（进程即将退出，无后续影响）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            if (!Stop())
            {
                OnStopTimeout();
                return;
            }

            _disposed = true;
            Http.Dispose();
            _stop.Dispose();
            _stopCancellation.Dispose();
        }

        /// <summary>停止超时（线程未在 <see cref="HttpWorkerSupport.StopJoinTimeoutMs"/> 内退出）的告警口——
        /// 本类不持日志面，子类接自己的日志。</summary>
        protected virtual void OnStopTimeout()
        {
        }
    }
}
