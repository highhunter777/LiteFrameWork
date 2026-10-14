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
            // 联机路径（批C）：Meta 签发的真实票据——开发与正式构建同链（区别于下面的测试身份门禁）。
            // 票据经验服务端验签才能进房；参数由 MetaEntryService 换票后经 ProcedureArgs 移交。
            if (req.MetaJoin.Ticket != null)
            {
                var join = req.MetaJoin;
                RunAsyncCore(m, ct, join.Host, join.Port, join.RoomId, join.Ticket, testRoom: false).Forget();
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            RunAsyncCore(m, ct, TestHost, TestPort,
                req.TestRoom ? TestRoomId : DefaultRoomId, TestToken, req.TestRoom).Forget();   // 一行转发，仅此而已——禁止 async void
#else
            // 生产配置缺真实依赖时拒绝启动或拒绝相应功能，不能悄悄退回 fake：
            // 正式包未接入真实登录——**拒绝以测试身份进房**（联机路径已在上方放行——真实票据不受此门禁）。
            var ex = new InvalidOperationException(
                "正式构建拒绝以测试身份进房——真实进房走 Meta 票据（框架先行 §6；上云批C）");
            Fail(m, ex, nameof(RunAsync));
            m.Request(ProcedureId.Error, new ProcedureArgs(ex));
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct,
            string host, int port, string roomId, string token, bool testRoom)
        {
            AccountSession account = null;
            BattleClient battle = null;
            try
            {
                account = _accounts.Create(host, port, roomId, token, LiteNet.BuildHash.Value, testRoom);
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
        /// **测试房不提供 KCP 形态**：免死规则靠"客户端预测与进程内服务器同持一个规则实例"
        /// （<see cref="LiteSim.MatchTestRules"/> 经 <see cref="LiteSim.SimTestRules.ActiveMatch"/> 登记），
        /// 真服务器的房间级规则下发未做。
        ///
        /// 信封 PSK 注入口（批C）：<paramref name="deploy"/> 带 <c>-envelope.key</c> 时优先（构建期/启动参数注入）；
        /// 空 = 回落 <c>LITENET_SECURE_ENVELOPE_KEY</c> 环境变量既有口（两道注入口不互斥——与部署侧同 key）。
        ///
        /// <paramref name="gameplayDigestBase"/>（批 E）= 本地装载产物的玩法数据身份基摘要提供者：本地服形态
        /// 的**房间身份必须与客户端同源**（同进程同一份装载 —— 客户端 `BattleContext` 校验点按它复算 `StartGame.ConfigHash`），
        /// 故由装配根传同一提供者；null = 测试/未配置形态（摘要按 0 基值，双方一致即可——见 `LocalServerTransport`）。
        /// <paramref name="maps"/>（批4）= 地图提供者（D9）：本地服房间的判定图必须与客户端逐位同图
        /// （出生点/碰撞/遮挡），故两处取同一提供者；本地服形态缺它即抛（无图不得起权威局——D22）。
        /// </summary>
        internal static LiteNet.Transport.IClientTransport CreateTransportForFactory(bool testRoom,
            MetaEntryConfig deploy = null, Func<uint> gameplayDigestBase = null, IMapProvider maps = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 每局规则持有件：初值随入口定（常规入口一律 false——与既有"随入口复位"同语义），
            // 登记后客户端预测（BattleContext.StartGame 绑定）与本地服（ForRoom 注入）同实例。
            // 爆头带覆写随 TestModeRuntime 待用值（本地服形态的调参延续；真服务器无此链——「在线房间级下发未做」）。
            var rules = new LiteSim.MatchTestRules
            {
                NoDeath = testRoom && TestModeRuntime.NoDeath,
                InfiniteAmmo = testRoom && TestModeRuntime.InfiniteAmmo,
                HeadHitLineOverride = TestModeRuntime.HeadHitLineOverride,
                HeadshotRadiusOverride = TestModeRuntime.HeadshotRadiusOverride,
            };
            LiteSim.SimTestRules.ActiveMatch = rules;             // 活动局登记口（GM 中局开合的写通道；判定路径不读）
            LiteSim.SimTestRules.Active = testRoom;                // 宿主钩子（冻结/传送/假人操作）开关
            LiteSim.SimTestRules.BotFrozen = testRoom && TestModeRuntime.BotFrozen;
            LiteSim.SimTestRules.BotInfiniteHp = testRoom && TestModeRuntime.BotInfiniteHp;   // 进房初值镜像（Live 开关另有 Set 时镜像）
            LiteSim.SimTestRules.TeleportDispatch = false;         // 进房清残留请求
            LiteSim.SimTestRules.BotResetRequested = false;        // 同上（进房前的面板误点不带到对局）
            if (testRoom || DebugTuner.UseLocalServerEnabled)
            {
                UnityEngine.Debug.Log(testRoom
                    ? "[Match] **测试房**：本地服 Room-Test + 全房免死（Hp 保底 1、目标不消失——测试专用）"
                    : "[Match] **本地服务器**：对局在进程内跑真 RoomRuntime 内核"
                        + "（无 Socket / 无票据 / 单房间 / 剩余席位自动补位站桩——弱网、重连真实性与真实多人交互仍须真服务器验证）");
                int players = testRoom ? 1 + UnityEngine.Mathf.Max(0, TestModeRuntime.BotCount) : 0;   // 0 = 房间默认
                if (maps == null)
                    throw new InvalidOperationException(
                        "本地服务器形态缺地图提供者（装配根注入 IMapProvider——D22：无回退地图）");
                return LocalServerTransport.ForRoom(testRoom ? TestRoomId : DefaultRoomId, players, rules,
                    gameplayDigestBase?.Invoke() ?? 0u,   // 房间身份与客户端同源（同进程同一份装载——批 E）
                    maps.GetMap());                       // 判定图与客户端逐位同图（出生点/碰撞/遮挡——批4）
            }
#endif
            return new LiteNet.Transport.SecureEnvelopeClientTransport(
                new LiteNet.Transport.KcpTransportClient(),
                string.IsNullOrEmpty(deploy?.EnvelopeKeyBase64)
                    ? LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment()   // 既有口（部署侧 env 注入）
                    : LiteNet.Transport.SecureEnvelopeOptions.FromBase64(deploy.EnvelopeKeyBase64));  // -envelope.key 优先
        }

        /// <summary>等 JoinAck（RoomClient 相位 Idle → Connected；超时 = 确定失败态）。</summary>
        private static UniTask WaitJoined(BattleClient battle, CancellationToken ct)
            => StageSignal.Wait<JoinAck>(
                h => battle.Client.OnJoinAck += h,
                h => battle.Client.OnJoinAck -= h,       // 临时订阅必须退——持久订阅归 BattleContext
                alreadyDone: false,
                ct,
                timeoutMs: JoinTimeoutMs);
    }
}
