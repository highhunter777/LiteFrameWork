#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Text;
using LiteClient;
using LiteNet;
using LiteNet.Diagnostics;

namespace LiteGame
{
    /// <summary>
    /// 网络调试组件（调试内核 <see cref="DebugKernel.Network"/> 持有）：把**读数**（会话相位 / RTT / 丢包 /
    /// 弱网注入）与**动作**（强制断线 / 无状态重连 / 抑制自动重连）收在一处，投影（DevHUD / F2 面板 / F3 控制台 / GM 页）
    /// 只经内核消费——不各自拼读数、不直调会话客户端。
    ///
    /// **相位语义**：<see cref="BattleContext"/> 在进入
    /// <see cref="ClientSessionPhase.SuspectedLost"/> 时默认**自动**发起重连（凭 JoinAck 票据）；
    /// 抑制开关开启则不自动重拨、停在断线态等手动恢复。自动重连已接管时"重连"按钮仍只接受
    /// SuspectedLost 相位，其余相位如实拒绝。
    ///
    /// 边界：只对真 KCP 会话有意义；本地测试房（进程内 <c>LocalServerTransport</c>）无 Socket——
    /// 断线/重连只走协议层，不模拟真实丢包（弱网注入同理不覆盖本地测试房）。
    /// </summary>
    public sealed class NetworkDebug
    {
        private readonly WeakNetRuntime _weakNet;
        private readonly Func<BattleContext> _battle;

        /// <param name="weakNet">弱网运行实例（可为 null——未装配时读模型显示"未启用"）。</param>
        /// <param name="battle">当前对局上下文取件（通常 <c>() =&gt; BattleDiagnostics.Current</c>；null = 恒无对局）。</param>
        public NetworkDebug(WeakNetRuntime weakNet, Func<BattleContext> battle)
        {
            _weakNet = weakNet;
            _battle = battle;
        }

        /// <summary>当前对局是否存在（无对局时动作一律拒绝、读数整组隐藏）。</summary>
        public bool HasBattle => Battle != null;

        /// <summary>当前会话相位（无对局 = Idle）。</summary>
        public ClientSessionPhase Phase
        {
            get
            {
                BattleContext context = Battle;
                return context != null ? context.SessionPhase : ClientSessionPhase.Idle;
            }
        }

        /// <summary>网络质量（探针推断的 RTT/丢包；无对局 = 无样本）。</summary>
        public NetworkQuality Quality
        {
            get
            {
                BattleContext context = Battle;
                return context != null ? context.NetworkQuality : NetworkQuality.None;
            }
        }

        /// <summary>弱网注入只读快照（未装配 = 未启用）。</summary>
        public WeakNetSnapshot WeakNet => _weakNet != null ? _weakNet.TakeSnapshot() : default;

        /// <summary>本局标识（空 = 无对局/未开局）——现场包与日志关联用。</summary>
        public string MatchId
        {
            get
            {
                BattleContext context = Battle;
                return context != null ? context.MatchId : string.Empty;
            }
        }

        /// <summary>自动重连抑制（调试开关；无对局恒 false）：true 时断线停在 SuspectedLost，
        /// 由 <see cref="TryBeginReconnect"/> 手动恢复。默认 false＝产品行为（断线自动重连）。</summary>
        public bool SuppressAutoReconnect
        {
            get
            {
                BattleContext context = Battle;
                return context != null && context.DebugSuppressAutoReconnect;
            }
        }

        /// <summary>设置自动重连抑制（无对局如实拒绝）。</summary>
        public bool TrySetSuppressAutoReconnect(bool suppressed, out string error)
        {
            BattleContext context = Battle;
            if (context == null)
            {
                error = "无对局（未进房）";
                return false;
            }

            context.DebugSuppressAutoReconnect = suppressed;
            error = null;
            return true;
        }

        /// <summary>
        /// 追加"会话 + 弱网注入"诊断行（DevHUD 联机诊断段 / F2 面板 / F3 控制台 / GM 页同一文案单源）：
        /// 会话两行 + 扩展行由 <see cref="BattleDiagnostics.AppendExtendedCurrent"/> 产出，
        /// 弱网启用时再追加注入行。返回 false = 无对局（已写入空态占位）。
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
        /// 强制断线（模拟丢链）：仅 Connected/Restoring 可执行；断后由 <see cref="BattleContext"/>
        /// 的相位策略自动发起重连（SuspectedLost → Reconnecting），用于验证恢复全链。
        /// 返回 false 时 <paramref name="error"/> 为稳定拒绝原因（投影原样显示）。
        /// </summary>
        public bool TryForceDisconnect(out string error)
        {
            BattleContext context = Battle;
            if (context == null)
            {
                error = "无对局（未进房）";
                return false;
            }

            ClientSessionPhase phase = context.SessionPhase;
            if (!CanForceDisconnect(phase))
            {
                error = $"相位 {phase} 不可断线（仅 Connected/Restoring）";
                return false;
            }

            context.DebugBattle.Disconnect();
            error = null;
            return true;
        }

        /// <summary>
        /// 无状态强制重连：仅 SuspectedLost（自动重连尚未接管）有效；成功后相位由会话机推进，
        /// 本组件不保存任何"抑制自动重连"状态（避免调试开关改变产品行为）。
        /// </summary>
        public bool TryBeginReconnect(out string error)
        {
            BattleContext context = Battle;
            if (context == null)
            {
                error = "无对局（未进房）";
                return false;
            }

            ClientSessionPhase phase = context.SessionPhase;
            if (!CanBeginReconnect(phase))
            {
                error = $"相位 {phase} 不可重连（仅 SuspectedLost；Connected 下断线会自动重连）";
                return false;
            }

            if (!context.DebugBattle.BeginReconnect())
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

        private BattleContext Battle => _battle != null ? _battle() : null;
    }
}
#endif
