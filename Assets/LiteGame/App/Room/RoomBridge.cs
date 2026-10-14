using System;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using XLua; // lint-allow R12：房间会话门面本体住 App（须引用 BattleClient——LuaBridge 不引用 App，依赖方向不可反），本体只做 env 装订/回调转发——与 MetaBridge 同判

namespace LiteGame
{
    /// <summary>
    /// 房间会话门面（<c>Bridge.room</c>，房主开局批）：《开房与房主开局》UI 消费面的 Lua 侧入口——
    /// 房主事实（开钮归属）与提前开局请求。**会话态门面**：后端不是装配期常驻服务，而是**当前对局会话**
    /// （Battle 阶段进/出时装订/摘除——`SimTestRules.ActiveMatch` 同款"活会话登记"范式，但生产域、
    /// 随阶段收尾必摘）。
    ///
    /// **语义契约**：
    /// - <see cref="OnOwner"/>：**替换语义**（重复登记换回调，nil＝退订——与控件族 On*Changed 同契约）；
    ///   订阅时已绑会话＝**立即回放当前事实**（页面先于 JoinAck 打开的竞态由此消解）；会话装订时若页面
    ///   已订阅＝装订即回放（JoinAck 在 Match 阶段到达，装订在 Battle 阶段——两序皆回放、无丢事实）。
    ///   事实变化源＝JoinAck（初值）与 OwnerChanged（房主迁移——续批"UI 换按钮归属"的消费面）。
    /// - <see cref="StartMatch"/>：**被拒恰一次回调**（false + 服务器 reason）；成功**不回调**——开局以
    ///   OnStartGame 广播为准，流程侧关房间页（<c>PageFlow.CloseRoomIfOpen</c>），页面无需反馈；
    ///   未绑会话＝立即回 false（"not-in-room"——迟到点击不悬挂）。
    /// - 无会话时一切入口显性降级（IsOwner=false / 不推送），不抛。
    /// </summary>
    public static class RoomBridge
    {
        private static IRoomSessionFace s_session;
        private static LuaFunction s_ownerFn;           // 页面订阅（替换语义——OnShow 重登记/OnHide 退订）
        private static LuaFunction s_pendingStart;      // 在途开局请求回调（被拒恰一次）

        /// <summary>会话装订（Battle 阶段进——ProcedureBattle 调）。幂等：重复装订先摘旧。</summary>
        public static void BindSession(IRoomSessionFace session)
        {
            UnbindSession();
            s_session = session ?? throw new ArgumentNullException(nameof(session));
            s_session.StartMatchDenied += OnStartMatchDenied;
            s_session.OwnerChanged += OnOwnerChangedInternal;            if (s_ownerFn != null)
                NotifyOwner(s_session.IsOwner);         // 页面已订阅：装订即回放（JoinAck 先到的序）
        }

        /// <summary>会话摘除（Battle 阶段收尾必调——离场/失败后页面订阅不得命中死会话）。</summary>
        public static void UnbindSession()
        {
            if (s_session == null) return;
            s_session.StartMatchDenied -= OnStartMatchDenied;
            s_session.OwnerChanged -= OnOwnerChangedInternal;
            s_session = null;
            s_pendingStart = null;                      // 在途回调随会话作废（页面随收尾关闭；OnShow 复位标志）
        }

        /// <summary>当前房主事实（未绑会话 = false——页面默认隐藏开钮）。</summary>
        public static bool IsOwner => s_session != null && s_session.IsOwner;

        /// <summary>房主事实订阅（替换语义；nil＝退订）。已绑会话＝订阅即回放当前事实。</summary>
        public static void OnOwner(LuaFunction fn)
        {
            s_ownerFn = fn;
            if (fn != null && s_session != null) NotifyOwner(s_session.IsOwner);
        }

        /// <summary>提前开局请求（房主按钮点击）。被拒恰一次回调（false + reason）；成功不回调（OnStartGame 流程接管）。</summary>
        public static void StartMatch(LuaFunction cb)
        {
            if (s_session == null)
            {
                DoneStart(cb, false, "not-in-room");   // 迟到点击（离场后页面残余回调）——显性降级不悬挂
                return;
            }
            s_pendingStart = cb;
            s_session.SendStartMatchRequest();
        }

        /// <summary>装订 <c>Bridge.room</c> 进全局 Bridge 表（ProductBridges 调用）。
        /// 委托形全部在 GenConfig 白名单在册（Func&lt;bool&gt;/Action&lt;LuaFunction&gt;）——零新增生成。</summary>
        public static void BindGlobals(LuaEnv env)
        {
            var room = env.NewTable();
            room.Set("IsOwner", new Func<bool>(() => IsOwner));
            room.Set("OnOwner", new Action<LuaFunction>(OnOwner));
            room.Set("StartMatch", new Action<LuaFunction>(StartMatch));
            env.Global.Get<LuaTable>("Bridge").Set("room", room);
        }

        private static void OnOwnerChangedInternal(LiteNet.Proto.OwnerChanged change)
        {
            if (s_session != null) NotifyOwner(s_session.IsOwner);   // 事实已重算（RoomClient）——推送当前值
        }

        private static void OnStartMatchDenied(LiteNet.Proto.StartMatchResponse response)
        {
            LuaFunction cb = s_pendingStart;
            s_pendingStart = null;                      // 恰一次
            DoneStart(cb, false, response?.Reason ?? "denied");
        }

        private static void NotifyOwner(bool isOwner)
            => SafeCall.Invoke(() => s_ownerFn?.Call(isOwner), "Bridge.room.OnOwner");

        private static void DoneStart(LuaFunction cb, bool ok, string reason)
            => SafeCall.Invoke(() => cb?.Call(ok, reason), "Bridge.room.StartMatch");
    }

    /// <summary>房间会话端口（App 域——LiteClient 不反引 LiteGame，故端口在此、由 App 侧适配
    /// <see cref="BattleClient"/>；IMetaClient 先例同形）。测试注 fake 即可直测门面语义。</summary>
    public interface IRoomSessionFace
    {
        /// <summary>本机是否房主（协议派生态——RoomClient 单源）。</summary>
        bool IsOwner { get; }

        /// <summary>提前开局被拒反馈（成功路径以 OnStartGame 广播为准——本事件只在被拒时到达，RoomClient 契约）。</summary>
        event Action<LiteNet.Proto.StartMatchResponse> StartMatchDenied;

        /// <summary>房主迁移通知（载荷＝新房主席位；事实已重算——订阅方拉取 IsOwner 取新值）。</summary>
        event Action<LiteNet.Proto.OwnerChanged> OwnerChanged;

        /// <summary>发送提前开局请求（相位守卫在 RoomClient——未入房 no-op）。</summary>
        void SendStartMatchRequest();
    }

    /// <summary>BattleClient → 会话端口适配（三透传 + 事件透传；装配由 ProcedureBattle 完成）。</summary>
    public sealed class RoomSessionFace : IRoomSessionFace
    {
        private readonly BattleClient _battle;

        public RoomSessionFace(BattleClient battle)
            => _battle = battle ?? throw new ArgumentNullException(nameof(battle));

        public bool IsOwner => _battle.IsOwner;

        public event Action<LiteNet.Proto.StartMatchResponse> StartMatchDenied
        {
            add => _battle.Client.OnStartMatchResponse += value;
            remove => _battle.Client.OnStartMatchResponse -= value;
        }

        public event Action<LiteNet.Proto.OwnerChanged> OwnerChanged
        {
            add => _battle.Client.OnOwnerChanged += value;
            remove => _battle.Client.OnOwnerChanged -= value;
        }

        public void SendStartMatchRequest() => _battle.SendStartMatchRequest();
    }
}
