using System;
using LiteFramework;
using LiteNet.Proto;
using XLua; // lint-allow R12：房间会话门面（Lua 桥适配器），本体只做 env 装订/回调转发

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 房间会话门面（Bridge.room，房主开局批）：《开房与房主开局》UI 消费面的 Lua 侧入口—— 房主事实（开钮归属）与提前开局请求。
    /// </summary>
    public sealed class RoomBridge : IRoomSessionBridge
    {
        private IRoomSessionFace _session;
        private LuaEnv _env;                    // 订阅期捕获（行表构造用；BindGlobals 与测试同源注入）
        private LuaFunction _ownerFn;           // 页面订阅（替换语义——OnShow 重登记/OnHide 退订）
        private LuaFunction _membersFn;         // 席位名单订阅（替换语义；载荷=行表）
        private LuaFunction _pendingStart;      // 在途开局请求回调（被拒恰一次）

        /// <summary>会话装订（Battle 阶段进——ProcedureBattle 经端口调）。幂等：重复装订先摘旧。</summary>
        public void BindSession(IRoomSessionFace session)
        {
            UnbindSession();
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _session.StartMatchDenied += OnStartMatchDenied;
            _session.OwnerChanged += OnOwnerChangedInternal;
            _session.RoomSeatsChanged += OnRoomSeatsInternal;
            if (_ownerFn != null)
                NotifyOwner(_session.IsOwner);          // 页面已订阅：装订即回放（JoinAck 先到的序）
            if (_membersFn != null)
                NotifyMembers(_session.Seats);          // 席位名单同款双序回放
        }

        /// <summary>会话摘除（Battle 阶段收尾必调——离场/失败后页面订阅不得命中死会话）。</summary>
        public void UnbindSession()
        {
            if (_session == null) return;
            _session.StartMatchDenied -= OnStartMatchDenied;
            _session.OwnerChanged -= OnOwnerChangedInternal;
            _session.RoomSeatsChanged -= OnRoomSeatsInternal;
            _session = null;
            _pendingStart = null;                       // 在途回调随会话作废（页面随收尾关闭；OnShow 复位标志）
        }

        /// <summary>当前房主事实（未绑会话 = false——页面默认隐藏开钮）。</summary>
        public bool IsOwner => _session != null && _session.IsOwner;

        /// <summary>本房期望人数（未绑会话 = 0——消费方按"未标注"处理：不显假数）。
        /// 房间页"等待玩家 N/M"的分母取此值，分子取 <see cref="OnMembers"/> 席位面的条数——
        /// 两者同源于 JoinAck/席位广播，故计数与名单恒一致。</summary>
        public int ExpectedPlayers => _session != null ? _session.ExpectedPlayers : 0;

        /// <summary>房主事实订阅（替换语义；nil＝退订）。已绑会话＝订阅即回放当前事实。</summary>
        public void OnOwner(LuaFunction fn)
        {
            _ownerFn = fn;
            if (fn != null && _session != null) NotifyOwner(_session.IsOwner);
        }

        /// <summary>席位名单订阅（替换语义；nil＝退订）。订阅即回放当前快照（未收到过 = 空表）。
        /// envoy：行表构造用（生产 BindGlobals 闭包注入；测试同源注入）。</summary>
        public void OnMembers(LuaEnv env, LuaFunction fn)
        {
            _membersFn = fn;
            if (fn == null) return;
            _env = env;                                // 订阅期捕获——后续事件推送同源构造
            if (_session != null) NotifyMembers(_session.Seats);
        }

        /// <summary>提前开局请求（房主按钮点击）。被拒恰一次回调（false + reason）；成功不回调（OnStartGame 流程接管）。</summary>
        public void StartMatch(LuaFunction cb)
        {
            if (_session == null)
            {
                DoneStart(cb, false, "not-in-room");   // 迟到点击（离场后页面残余回调）——显性降级不悬挂
                return;
            }
            _pendingStart = cb;
            _session.SendStartMatchRequest();
        }

        /// <summary>装订 <c>Bridge.room</c> 进全局 Bridge 表（ProductBridges 调用）。
        /// 委托形全部在 GenConfig 白名单在册（Func&lt;bool&gt;/Action&lt;LuaFunction&gt;）——零新增生成。</summary>
        public void BindGlobals(LuaEnv env)
        {
            var room = env.NewTable();
            room.Set("IsOwner", new Func<bool>(() => IsOwner));
            room.Set("ExpectedPlayers", new Func<int>(() => ExpectedPlayers));
            room.Set("OnOwner", new Action<LuaFunction>(OnOwner));
            room.Set("OnMembers", new Action<LuaFunction>(fn => OnMembers(env, fn)));
            room.Set("StartMatch", new Action<LuaFunction>(StartMatch));
            env.Global.Get<LuaTable>("Bridge").Set("room", room);
        }

        private void OnOwnerChangedInternal(OwnerChanged change)
        {
            if (_session != null) NotifyOwner(_session.IsOwner);   // 事实已重算（RoomClient）——推送当前值
        }

        private void OnRoomSeatsInternal(RoomSeats seats)
            => NotifyMembers(seats);

        private void OnStartMatchDenied(StartMatchResponse response)
        {
            LuaFunction cb = _pendingStart;
            _pendingStart = null;                      // 恰一次
            DoneStart(cb, false, response?.Reason ?? "denied");
        }

        private void NotifyOwner(bool isOwner)
            => SafeCall.Invoke(() => _ownerFn?.Call(isOwner), "Bridge.room.OnOwner");

        private void NotifyMembers(RoomSeats seats)
        {
            LuaFunction fn = _membersFn;
            LuaEnv env = _env;
            if (fn == null || env == null) return;      // 无订阅/无 env（未装订）——显性空转
            SafeCall.Invoke(() => fn.Call(SeatsTable(env, seats)), "Bridge.room.OnMembers");
        }

        /// <summary>席位快照 → LuaTable 数组（行字段只取页面消费面：playerId/name/online；
        /// key = playerId 字符串——行协议稳定 key）。seats 为 null（未收到过）= 空表。</summary>
        private static LuaTable SeatsTable(LuaEnv env, RoomSeats seats)
        {
            var rows = env.NewTable();
            if (seats == null) return rows;
            for (int i = 0; i < seats.Seats.Count; i++)
            {
                SeatInfo seat = seats.Seats[i];
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
}