using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;
using XLua; // lint-allow R12：房间会话门面本体住 App（须引用 BattleClient——LuaBridge 不引用 App，依赖方向不可反），本体只做 env 装订/回调转发——与 MetaBridge 同判

namespace LiteGame
{
    /// <summary>
    /// 房间会话门面（<c>Bridge.room</c>，房主开局批）：《开房与房主开局》UI 消费面的 Lua 侧入口——
    /// 房主事实（开钮归属）与提前开局请求。**会话态门面**：后端不是装配期常驻服务，而是**当前对局会话**
    /// （Battle 阶段进/出时装订/摘除——"活会话登记"范式，但生产域、
    /// 随阶段收尾必摘）。
    ///
    /// **语义契约**：
    /// - <see cref="OnOwner"/>：**替换语义**（重复登记换回调，nil＝退订——与控件族 On*Changed 同契约）；
    ///   订阅时已绑会话＝**立即回放当前事实**（页面先于 JoinAck 打开的竞态由此消解）；会话装订时若页面
    ///   已订阅＝装订即回放（JoinAck 在 Match 阶段到达，装订在 Battle 阶段——两序皆回放、无丢事实）。
    ///   事实变化源＝JoinAck（初值）与 OwnerChanged（房主迁移——续批"UI 换按钮归属"的消费面）。
    /// - <see cref="OnMembers"/>：**替换语义**同 <see cref="OnOwner"/>；载荷 = 席位全量快照行表
    ///   （`{key, playerId, name, online}`——行字段按条目子节点名投影，供 `SetList` 整表重绑）；
    ///   订阅/装订即回放当前快照（未收到过 = 空表；首份广播到达后按事件推送）。数据源＝协议全量快照
    ///   （进房/断线/恢复完成重发，自带收敛）；显示名服务端派生占位（G3 Profile 换真昵称时两处来源一并更替）。
    /// - <see cref="StartMatch"/>：**被拒恰一次回调**（false + 服务器 reason）；成功**不回调**——开局以
    ///   OnStartGame 广播为准，流程侧关房间页（<c>PageFlow.CloseRoomIfOpen</c>），页面无需反馈；
    ///   未绑会话＝立即回 false（"not-in-room"——迟到点击不悬挂）。
    /// - <see cref="ExpectedPlayers"/>：**拉取式**（房规模是 JoinAck 定值、整局不变，无需订阅）；房间页
    ///   "等待玩家 N/M" 的分母取它，分子取 <see cref="OnMembers"/> 席位面条数——**两处同源于同一份席位面／
    ///   JoinAck**，故计数与名单恒同帧一致（房间页不另查大厅投影：那是给 Page_Lobby 看未进的房用的）。
    /// - 无会话时一切入口显性降级（IsOwner=false / ExpectedPlayers=0 / 不推送），不抛。
    /// </summary>
    public static class RoomBridge
    {
        private static IRoomSessionFace s_session;
        private static LuaEnv s_env;                    // 订阅期捕获（行表构造用；BindGlobals 与测试同源注入）
        private static LuaFunction s_ownerFn;           // 页面订阅（替换语义——OnShow 重登记/OnHide 退订）
        private static LuaFunction s_membersFn;         // 席位名单订阅（替换语义；载荷=行表）
        private static LuaFunction s_pendingStart;      // 在途开局请求回调（被拒恰一次）

        /// <summary>会话装订（Battle 阶段进——ProcedureBattle 调）。幂等：重复装订先摘旧。</summary>
        public static void BindSession(IRoomSessionFace session)
        {
            UnbindSession();
            s_session = session ?? throw new ArgumentNullException(nameof(session));
            s_session.StartMatchDenied += OnStartMatchDenied;
            s_session.OwnerChanged += OnOwnerChangedInternal;
            s_session.RoomSeatsChanged += OnRoomSeatsInternal;
            if (s_ownerFn != null)
                NotifyOwner(s_session.IsOwner);         // 页面已订阅：装订即回放（JoinAck 先到的序）
            if (s_membersFn != null)
                NotifyMembers(s_session.Seats);         // 席位名单同款双序回放
        }

        /// <summary>会话摘除（Battle 阶段收尾必调——离场/失败后页面订阅不得命中死会话）。</summary>
        public static void UnbindSession()
        {
            if (s_session == null) return;
            s_session.StartMatchDenied -= OnStartMatchDenied;
            s_session.OwnerChanged -= OnOwnerChangedInternal;
            s_session.RoomSeatsChanged -= OnRoomSeatsInternal;
            s_session = null;
            s_pendingStart = null;                      // 在途回调随会话作废（页面随收尾关闭；OnShow 复位标志）
        }

        /// <summary>当前房主事实（未绑会话 = false——页面默认隐藏开钮）。</summary>
        public static bool IsOwner => s_session != null && s_session.IsOwner;

        /// <summary>本房期望人数（未绑会话 = 0——消费方按"未标注"处理：不显假数）。
        /// 房间页"等待玩家 N/M"的分母取此值，分子取 <see cref="OnMembers"/> 席位面的条数——
        /// 两者同源于 JoinAck/席位广播，故计数与名单恒一致。</summary>
        public static int ExpectedPlayers => s_session != null ? s_session.ExpectedPlayers : 0;

        /// <summary>房主事实订阅（替换语义；nil＝退订）。已绑会话＝订阅即回放当前事实。</summary>
        public static void OnOwner(LuaFunction fn)
        {
            s_ownerFn = fn;
            if (fn != null && s_session != null) NotifyOwner(s_session.IsOwner);
        }

        /// <summary>席位名单订阅（替换语义；nil＝退订）。订阅即回放当前快照（未收到过 = 空表）。
        /// envoy：行表构造用（生产 BindGlobals 闭包注入；测试同源注入）。</summary>
        public static void OnMembers(LuaEnv env, LuaFunction fn)
        {
            s_membersFn = fn;
            if (fn == null) return;
            s_env = env;                                // 订阅期捕获——后续事件推送同源构造
            if (s_session != null) NotifyMembers(s_session.Seats);
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
            room.Set("ExpectedPlayers", new Func<int>(() => ExpectedPlayers));
            room.Set("OnOwner", new Action<LuaFunction>(OnOwner));
            room.Set("OnMembers", new Action<LuaFunction>(fn => OnMembers(env, fn)));
            room.Set("StartMatch", new Action<LuaFunction>(StartMatch));
            env.Global.Get<LuaTable>("Bridge").Set("room", room);
        }

        private static void OnOwnerChangedInternal(LiteNet.Proto.OwnerChanged change)
        {
            if (s_session != null) NotifyOwner(s_session.IsOwner);   // 事实已重算（RoomClient）——推送当前值
        }

        private static void OnRoomSeatsInternal(LiteNet.Proto.RoomSeats seats)
            => NotifyMembers(seats);

        private static void OnStartMatchDenied(LiteNet.Proto.StartMatchResponse response)
        {
            LuaFunction cb = s_pendingStart;
            s_pendingStart = null;                      // 恰一次
            DoneStart(cb, false, response?.Reason ?? "denied");
        }

        private static void NotifyOwner(bool isOwner)
            => SafeCall.Invoke(() => s_ownerFn?.Call(isOwner), "Bridge.room.OnOwner");

        private static void NotifyMembers(LiteNet.Proto.RoomSeats seats)
        {
            LuaFunction fn = s_membersFn;
            LuaEnv env = s_env;
            if (fn == null || env == null) return;      // 无订阅/无 env（未装订）——显性空转
            SafeCall.Invoke(() => fn.Call(SeatsTable(env, seats)), "Bridge.room.OnMembers");
        }

        /// <summary>席位快照 → LuaTable 数组（行字段只取页面消费面：playerId/name/online；
        /// key = playerId 字符串——行协议稳定 key）。seats 为 null（未收到过）= 空表。</summary>
        private static LuaTable SeatsTable(LuaEnv env, LiteNet.Proto.RoomSeats seats)
        {
            var rows = env.NewTable();
            if (seats == null) return rows;
            for (int i = 0; i < seats.Seats.Count; i++)
            {
                LiteNet.Proto.SeatInfo seat = seats.Seats[i];
                var row = env.NewTable();
                row.Set("key", seat.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                row.Set("playerId", seat.PlayerId);
                row.Set("name", seat.DisplayName ?? string.Empty);
                row.Set("online", seat.Connected);
                rows.Set(i + 1, row);
            }
            return rows;
        }

        private static void DoneStart(LuaFunction cb, bool ok, string reason)
            => SafeCall.Invoke(() => cb?.Call(ok, reason), "Bridge.room.StartMatch");
    }

    /// <summary>房间会话端口（App 域——LiteFramework 不反引 LiteGame，故端口在此、由 App 侧适配
    /// <see cref="BattleClient"/>；IMetaClient 先例同形）。测试注 fake 即可直测门面语义。</summary>
    public interface IRoomSessionFace
    {
        /// <summary>本机是否房主（协议派生态——RoomClient 单源）。</summary>
        bool IsOwner { get; }

        /// <summary>本房期望人数（协议派生态——JoinAck 定值的房事实；0 = 服务器未标注，
        /// 消费方按默认 2 座回落，与 <c>BattleContext</c> 的定容口径同源）。</summary>
        int ExpectedPlayers { get; }

        /// <summary>席位实时面最新快照（协议缓存单源——RoomClient；未收到前为 null）。</summary>
        LiteNet.Proto.RoomSeats Seats { get; }

        /// <summary>提前开局被拒反馈（成功路径以 OnStartGame 广播为准——本事件只在被拒时到达，RoomClient 契约）。</summary>
        event Action<LiteNet.Proto.StartMatchResponse> StartMatchDenied;

        /// <summary>房主迁移通知（载荷＝新房主席位；事实已重算——订阅方拉取 IsOwner 取新值）。</summary>
        event Action<LiteNet.Proto.OwnerChanged> OwnerChanged;

        /// <summary>席位实时面广播（全量快照——进房/断线/恢复完成时到达）。</summary>
        event Action<LiteNet.Proto.RoomSeats> RoomSeatsChanged;

        /// <summary>发送提前开局请求（相位守卫在 RoomClient——未入房 no-op）。</summary>
        void SendStartMatchRequest();
    }

    /// <summary>BattleClient → 会话端口适配（四透传 + 事件透传；装配由 ProcedureBattle 完成）。</summary>
    public sealed class RoomSessionFace : IRoomSessionFace
    {
        private readonly BattleClient _battle;

        public RoomSessionFace(BattleClient battle)
            => _battle = battle ?? throw new ArgumentNullException(nameof(battle));

        public bool IsOwner => _battle.IsOwner;

        public int ExpectedPlayers => _battle.Client.ExpectedPlayers;

        public LiteNet.Proto.RoomSeats Seats => _battle.Client.Seats;

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

        public event Action<LiteNet.Proto.RoomSeats> RoomSeatsChanged
        {
            add => _battle.Client.OnRoomSeats += value;
            remove => _battle.Client.OnRoomSeats -= value;
        }

        public void SendStartMatchRequest() => _battle.SendStartMatchRequest();
    }
}
