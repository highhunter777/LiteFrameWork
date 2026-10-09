using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet.Proto;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 进房流程：创建 **Account Scope** → BattleClient 连接 + Join → JoinAck 即移交 Battle。
    ///
    /// 身份口径：本阶段**仅开发/编辑器/开发包**使用隔离测试发行者的受控测试身份
    /// （<see cref="TestToken"/>，与 ProcedureMain F9 同门禁）；**正式构建缺真实身份依赖时
    /// 直接拒绝进房**（确定错误态，不悄悄退回 fake——见 <see cref="RunAsync"/> 门禁）。
    /// 服务端维持"不得公网"红线（Join 侧票据校验仍为原型级）。
    /// buildHash 用 <see cref="LiteNet.BuildHash.Value"/>（两端同源——不一致服务端拒绝进房）。
    ///
    /// 所有权：Account Scope 在本阶段创建；正常路径随 <see cref="ProcedureArgs"/> 移交 Battle
    /// （离场收尾在 Battle，关闭序见 <see cref="ProcedureBattle"/>）；本阶段失败/取消则就地 Dispose。
    /// </summary>
    public sealed class ProcedureMatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>本地联调端点（RoomServer 默认装配；测试入口）。</summary>
        public const string TestHost = "127.0.0.1";
        public const int TestPort = 17777;
        /// <summary>测试房间（RoomConfig 默认房间号）。</summary>
        public const string DefaultRoomId = "Room-A";
        /// <summary>测试房房间号（F10 入口：本地服专用于测试；全房免死规则见 <see cref="LiteSim.SimTestRules"/>）。</summary>
        public const string TestRoomId = "Room-Test";
        /// <summary>隔离测试发行者的受控测试身份（服务端仍按红线拒绝公网）。</summary>
        public const string TestToken = "c2-dev-token";

        /// <summary>JoinAck 等待预算（毫秒）——超时进 Error（确定失败态，不静默重试）。</summary>
        public const int JoinTimeoutMs = 10_000;

        private readonly IAccountSessionFactory _accounts;

        /// <summary>本阶段在途的会话（<see cref="OnUpdate"/> 据它驱动传输泵；未连接时为 null）。</summary>
        private BattleClient _pending;

        public ProcedureMatch(IAccountSessionFactory accounts, CancellationToken rootToken = default) : base(rootToken)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        }

        /// <summary>
        /// 驱动传输泵（**本阶段必需的**，不是可选优化）：KCP 的 cookie 握手与后续收发全靠
        /// <c>TickIncoming/TickOutgoing</c> 轮询推进——不泵就永远连不上，Join 也永远发不出去。
        /// 泵挂在本阶段的 <see cref="OnUpdate"/> 上：阶段生命周期 = 泵的生命周期，
        /// 迁移进 Battle 后由 <c>BattleContext.Tick</c> 接续（不会双泵）。
        /// </summary>
        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            BattleClient pending = _pending;
            if (pending == null) return;
            pending.TickIncoming();
            pending.TickOutgoing();
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            RunAsyncCore(m, ct, req.TestRoom).Forget();          // 一行转发，仅此而已——禁止 async void
#else
            // 生产配置缺真实依赖时拒绝启动或拒绝相应功能，不能悄悄退回 fake：
            // 正式包未接入真实登录——**拒绝进房**并进确定错误态。
            // 当前正式包本无进房入口（F9 已被同门禁编译排除），此处是第二道防线：
            // 任何入口到达本阶段都必须显式失败，不得用测试身份连服务器。
            var ex = new InvalidOperationException(
                "正式构建未接入真实登录——拒绝以测试身份进房（框架先行 §6）");
            Fail(m, ex, nameof(RunAsync));
            m.Request(ProcedureId.Error, new ProcedureArgs(ex));
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct, bool testRoom)
        {
            AccountSession account = null;
            BattleClient battle = null;
            try
            {
                account = _accounts.Create(TestHost, TestPort,
                    testRoom ? TestRoomId : DefaultRoomId, TestToken, LiteNet.BuildHash.Value, testRoom);
                battle = account.Battle;
                _pending = battle;                        // 交给 OnUpdate 驱动泵（握手/收发全靠它推进）

                await WaitJoined(battle, ct);
                UnityEngine.Debug.Log("[Battle] joined (JoinAck)");

                _pending = null;                          // 泵交棒给 BattleContext.Tick（不双泵）
                // 移交所有权：AccountSession 随迁移进 Battle（离场收尾在 Battle）。
                m.Request(ProcedureId.Battle, new ProcedureArgs(accountSession: account));
            }
            catch (OperationCanceledException)
            {
                _pending = null;
                account?.Dispose();                       // 离场/宿主关闭：就地收尾，不流转
            }
            catch (Exception ex)
            {
                _pending = null;
                account?.Dispose();                       // 失败收尾（含 BattleClient/传输释放）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }

        /// <summary>
        /// 选择传输（<paramref name="testRoom"/> = F10 测试房：强制本地服 + 房号 Room-Test + 全房免死）：
        /// **真 KCP（默认）** 还是 **进程内本地服务器**（离线隔离开发，开关 <see cref="DebugTuner.UseLocalServerEnabled"/>）。
        ///
        /// **只在开发/编辑器/开发包可用**——`DebugTuner` 整体在 `#if` 内，release 下该静态不存在，
        /// 故此处也包在同一门禁里，正式包恒走真 KCP（不给"悄悄退化成假服务器"留窗口，
        /// 框架先行 §6"生产配置缺真实依赖时拒绝启动或拒绝相应功能，不能悄悄退回 fake"）。
        /// **测试房不提供 KCP 形态**：免死规则靠"客户端预测与进程内服务器同进程同源"（<see cref="LiteSim.SimTestRules"/>），
        /// 真服务器的房间级规则下发未做。
        /// </summary>
        internal static LiteNet.Transport.IClientTransport CreateTransportForFactory(bool testRoom)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            LiteSim.SimTestRules.NoDeath   = testRoom && TestModeRuntime.NoDeath;   // 规则随入口设置：常规入口（F9）一律复位
            LiteSim.SimTestRules.InfiniteAmmo = testRoom && TestModeRuntime.InfiniteAmmo;
            LiteSim.SimTestRules.Active    = testRoom;                             // 本地服宿主钩子（冻结/传送）开关
            LiteSim.SimTestRules.BotFrozen = testRoom && TestModeRuntime.BotFrozen;
            LiteSim.SimTestRules.TeleportDispatch = false;                         // 进房清残留请求
            if (testRoom || DebugTuner.UseLocalServerEnabled)
            {
                UnityEngine.Debug.Log(testRoom
                    ? "[Match] **测试房**：本地服 Room-Test + 全房免死（Hp 保底 1、目标不消失——测试专用）"
                    : "[Match] **本地服务器**：对局在进程内跑真 RoomRuntime 内核"
                        + "（无 Socket / 无票据 / 单房间 / 剩余席位自动补位站桩——弱网、重连真实性与真实多人交互仍须真服务器验证）");
                int players = testRoom ? 1 + UnityEngine.Mathf.Max(0, TestModeRuntime.BotCount) : 0;   // 0 = 房间默认
                return LocalServerTransport.ForRoom(testRoom ? TestRoomId : DefaultRoomId, players);
            }
#endif
            return new LiteNet.Transport.SecureEnvelopeClientTransport(
                new LiteNet.Transport.KcpTransportClient(),
                LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment());
        }

        /// <summary>等 JoinAck（RoomClient 相位 Idle → Connected；超时 = 确定失败态）。</summary>
        private static async UniTask WaitJoined(BattleClient battle, CancellationToken ct)
        {
            var joined = new UniTaskCompletionSource();
            void Handler(JoinAck _) => joined.TrySetResult();
            battle.Client.OnJoinAck += Handler;
            try
            {
                await joined.Task.AttachExternalCancellation(ct)
                    .Timeout(TimeSpan.FromMilliseconds(JoinTimeoutMs));
            }
            finally
            {
                battle.Client.OnJoinAck -= Handler;       // 临时订阅必须退——持久订阅归 BattleContext
            }
        }
    }
}
