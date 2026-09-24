using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 进房流程（C2 批①）：创建 **Account Scope** → BattleClient 连接 + Join → JoinAck 即移交 Battle。
    ///
    /// 身份口径（框架先行 §6）：正式登录业务归 G3——本阶段使用**隔离测试发行者**的受控测试身份
    /// （<see cref="TestToken"/>；生产缺真实依赖时由服务端拒绝，不悄悄退回 fake）。
    /// buildHash 用 <see cref="LiteNet.BuildHash.Value"/>（两端同源——不一致服务端拒绝进房）。
    ///
    /// 所有权：Account Scope 在本阶段创建；正常路径随 <see cref="ProcedureArgs"/> 移交 Battle
    /// （离场收尾在 Battle，关闭序见 <see cref="ProcedureBattle"/>）；本阶段失败/取消则就地 Dispose。
    /// </summary>
    public sealed class ProcedureMatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>本地联调端点（RoomServer 默认装配；R2 配置化前的测试入口）。</summary>
        public const string TestHost = "127.0.0.1";
        public const int TestPort = 17777;
        /// <summary>测试房间（RoomConfig 默认房间号）。</summary>
        public const string TestRoomId = "Room-A";
        /// <summary>隔离测试发行者的受控测试身份（G3 接入真实 Join Ticket 前的占位——服务端仍按红线拒绝公网）。</summary>
        public const string TestToken = "c2-dev-token";

        /// <summary>JoinAck 等待预算（毫秒）——超时进 Error（确定失败态，不静默重试）。</summary>
        public const int JoinTimeoutMs = 10_000;

        private readonly ClientScope _rootScope;

        public ProcedureMatch(ClientScope rootScope, CancellationToken rootToken = default) : base(rootToken)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            ClientScope account = null;
            BattleClient battle = null;
            try
            {
                account = _rootScope.CreateChild("Account");
                battle = account.Register(new BattleClient(TestHost, TestPort, TestRoomId, TestToken,
                    LiteNet.BuildHash.Value));

                await WaitJoined(battle, ct);
                UnityEngine.Debug.Log("[Battle] joined (JoinAck)");

                // 移交所有权：Account Scope + BattleClient 随迁移进 Battle（离场收尾在 Battle）
                m.Request(ProcedureId.Battle, new ProcedureArgs(battleClient: battle, accountScope: account));
            }
            catch (OperationCanceledException)
            {
                account?.Dispose();                       // 离场/宿主关闭：就地收尾，不流转
            }
            catch (Exception ex)
            {
                account?.Dispose();                       // 失败收尾（含 BattleClient/传输释放）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
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
