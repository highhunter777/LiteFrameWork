#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Text;
using LiteFramework;
using LiteNet;
using LiteNet.Diagnostics;

using LiteGame.Adapters.Dev.Debug.WeakNet;
using LiteGame.Modules.Battle.Abstractions.Run;
using LiteGame.Modules.Battle.Shared;
using LiteSync.Client;
using LiteGame.Adapters.Dev.Debug.Diagnostics;
using LiteGame.Adapters.Dev.Debug.WeakNet.Profile;
using LiteGame.Adapters.Dev.Debug.WeakNet.Stats;
namespace LiteGame.Adapters.Dev.Debug.NetDebug
{
    /// <summary>
    /// 网络调试组件（调试内核 DebugKernel.Network 持有）：把**读数**（会话相位 / RTT / 丢包 / 弱网注入）与**动作**（强制断线 / 无状态重连 / 抑制自动重连）收在一处。
    /// </summary>
    public sealed class NetworkDebug
    {
        private readonly WeakNetRuntime _weakNet;
        private readonly Func<BattleDebugPort> _debugPort;
        private readonly Func<IBattleClient> _battleClient;

        /// <param name="weakNet">弱网运行实例（可为 null——未装配时读模型显示"未启用"）。</param>
        /// <param name="debugPort">当前对局调试开关面取件（通常 <c>() =&gt; BattleDiagnostics.Current?.DebugPort</c>；null = 恒无对局）。</param>
        /// <param name="battleClient">当前对局会话客户端取件（断线/重连动作口；null = 恒无对局）。</param>
        public NetworkDebug(WeakNetRuntime weakNet, Func<BattleDebugPort> debugPort,
            Func<IBattleClient> battleClient)
        {
            _weakNet = weakNet;
            _debugPort = debugPort;
            _battleClient = battleClient;
        }

        /// <summary>当前对局是否存在（无对局时动作一律拒绝、读数整组隐藏）。</summary>
        public bool HasBattle => BattleClient != null;

        /// <summary>当前会话相位（无对局 = Idle）。</summary>
        public ClientSessionPhase Phase
            => TryPeek(out BattleDiagnostics.Snapshot snapshot) ? snapshot.Phase : ClientSessionPhase.Idle;

        /// <summary>网络质量（探针推断的 RTT/丢包；无对局 = 无样本）。</summary>
        public NetworkQuality Quality
            => TryPeek(out BattleDiagnostics.Snapshot snapshot) ? snapshot.Quality : NetworkQuality.None;

        /// <summary>弱网注入只读快照（未装配 = 未启用）。</summary>
        public WeakNetSnapshot WeakNet => _weakNet != null ? _weakNet.TakeSnapshot() : default;

        /// <summary>本局标识（空 = 无对局/未开局）——现场包与日志关联用。</summary>
        public string MatchId
            => TryPeek(out BattleDiagnostics.Snapshot snapshot) ? snapshot.MatchId : string.Empty;

        /// <summary>自动重连抑制（调试开关；无对局恒 false）：true 时断线停在 SuspectedLost，
        /// 由 <see cref="TryBeginReconnect"/> 手动恢复。默认 false＝产品行为（断线自动重连）。</summary>
        public bool SuppressAutoReconnect
            => TryPeek(out BattleDiagnostics.Snapshot snapshot) && snapshot.SuppressAutoReconnect;

        /// <summary>设置自动重连抑制（无对局如实拒绝）。</summary>
        public bool TrySetSuppressAutoReconnect(bool suppressed, out string error)
        {
            BattleDebugPort port = DebugPort;
            if (port == null)
            {
                error = "无对局（未进房）";
                return false;
            }

            port.SuppressAutoReconnect = suppressed;
            error = null;
            return true;
        }

        /// <summary>
        /// 追加"会话 + 弱网注入"诊断行（DevHud 联机诊断段 / F2 面板 / F3 控制台 / GM 页同一文案单源）。
        /// </summary>
        public bool AppendLines(StringBuilder into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            bool hasBattle = BattleDiagnostics.AppendExtendedCurrent(into);
            if (!hasBattle) into.Append("无对局（未进房）");
            if (hasBattle && SuppressAutoReconnect)
                into.Append("\n自动重连: 抑制（断线后停在 SuspectedLost，用 net reconnect 手动恢复）");
            WeakNetSnapshot weak = WeakNet;
            if (weak.Enabled)
            {
                into.Append('\n');
                weak.AppendLines(into);
            }
            return hasBattle;
        }

        /// <summary>
        /// 强制断线（模拟丢链）：仅 Connected/Restoring 可执行。
        /// </summary>
        public bool TryForceDisconnect(out string error)
        {
            IBattleClient client = BattleClient;
            if (client == null || !BattleDiagnostics.TryTake(out BattleDiagnostics.Snapshot snapshot))
            {
                error = "无对局（未进房）";
                return false;
            }

            ClientSessionPhase phase = snapshot.Phase;
            if (!CanForceDisconnect(phase))
            {
                error = $"相位 {phase} 不可断线（仅 Connected/Restoring）";
                return false;
            }

            client.Disconnect();
            error = null;
            return true;
        }

        /// <summary>
        /// 无状态强制重连：仅 SuspectedLost（自动重连尚未接管）有效；成功后相位由会话机推进，
        /// 本组件不保存任何"抑制自动重连"状态（避免调试开关改变产品行为）。
        /// </summary>
        public bool TryBeginReconnect(out string error)
        {
            IBattleClient client = BattleClient;
            if (client == null || !BattleDiagnostics.TryTake(out BattleDiagnostics.Snapshot snapshot))
            {
                error = "无对局（未进房）";
                return false;
            }

            ClientSessionPhase phase = snapshot.Phase;
            if (!CanBeginReconnect(phase))
            {
                error = $"相位 {phase} 不可重连（仅 SuspectedLost；Connected 下断线会自动重连）";
                return false;
            }

            if (!client.BeginReconnect())
            {
                error = "重连未发起（票据已耗尽或相位已变化）";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>可断线相位（会话已建立；重连窗口内不再重复断）。</summary>
        public static bool CanForceDisconnect(ClientSessionPhase phase)
            => phase == ClientSessionPhase.Connected || phase == ClientSessionPhase.Restoring;

        /// <summary>可手动重连相位（自动重连的唯一入口相位——其余相位拒绝，避免与状态机争写）。</summary>
        public static bool CanBeginReconnect(ClientSessionPhase phase)
            => phase == ClientSessionPhase.SuspectedLost;

        private BattleDebugPort DebugPort => _debugPort != null ? _debugPort() : null;

        private IBattleClient BattleClient => _battleClient != null ? _battleClient() : null;

        /// <summary>当前对局快照（读数一律经快照，不逐个抓上下文属性；无对局 = false）。</summary>
        private bool TryPeek(out BattleDiagnostics.Snapshot snapshot)
            => BattleDiagnostics.TryTake(out snapshot);
    }
}
#endif
