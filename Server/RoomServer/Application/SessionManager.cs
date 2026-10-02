using System.Collections.Generic;

namespace RoomServer.Application
{
    /// <summary>
    /// 会话表（§4.5 SessionManager 行：连接表 + 断线标记；超时踢除由 kcp2k 内建 Timeout 承担）。
    /// GetOrAddOnFirstPacket：首包未登记（连接成功但 Host 尚未见到 OnConnected 竞态窗口）时兜底登记。
    ///
    /// **容量与清理**（《商业级通用服务端框架总设计》§9.2"SessionManager…必须有容量上限和周期清理"）：
    /// - **容量上限**：构造时定容（默认 4×房间容量），超限拒绝新会话（地址栏连接仍由 kcp2k 管，
    ///   应用层不再为它分配记账对象）——防未认证连接把会话表撑成无界内存；
    /// - **周期清理** <see cref="Cleanup"/>：清掉"断线且超出重连窗口"与"未进房且长期静默"的会话，
    ///   由宿主按帧节拍调用（10s 一次即可），返回清理条数进 Ops。
    /// </summary>
    public sealed class SessionManager
    {
        /// <summary>断线会话保留时长（毫秒）——与重连票据 TTL 同量级（重连窗口内不清，窗口外必清）。</summary>
        public const long DisconnectedTtlMs = ReconnectService.TicketTtlMs;

        /// <summary>未进房连接的静默上限（毫秒）：连上但从不 Join/发包的会话（探测/半开）到期清理。</summary>
        public const long UnseatedIdleTtlMs = 60 * 1000;

        private readonly Dictionary<int, Session> _byConnection = new Dictionary<int, Session>();
        private readonly int _capacity;
        private long _nextEpoch;

        public SessionManager(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 1;
        }

        public int Count => _byConnection.Count;

        /// <summary>容量上限（超限的新连接不予登记——宿主应同时断开它）。</summary>
        public int Capacity => _capacity;

        /// <summary>登记会话；超容量返回 false（调用方负责断开该连接并计数）。</summary>
        public bool TryAdd(Session session)
        {
            if (_byConnection.ContainsKey(session.ConnectionId)) { _byConnection[session.ConnectionId] = session; return true; }
            if (_byConnection.Count >= _capacity) return false;
            _byConnection[session.ConnectionId] = session;
            return true;
        }

        /// <summary>为传输连接创建新的代次并登记；连接 Id 复用时也会递增。</summary>
        public bool TryAddNew(int connectionId, long nowMs, out Session session)
        {
            session = null;
            if (_byConnection.TryGetValue(connectionId, out Session existing) && !existing.Disconnected)
            {
                session = existing;
                return true;
            }
            if (existing != null) _byConnection.Remove(connectionId);
            if (_byConnection.Count >= _capacity) return false;
            session = new Session(connectionId, nowMs, ++_nextEpoch);
            _byConnection[connectionId] = session;
            return true;
        }

        public bool TryGet(int connectionId, out Session session) => _byConnection.TryGetValue(connectionId, out session);

        /// <summary>
        /// 首包兜底：连接事件与首包间存在竞态窗口时补登记（超容量返回 null——宿主丢包并计数）。
        /// 已断线的旧代次原样返回，由宿主丢弃迟到包；连接 Id 复用必须先经过
        /// <see cref="TryAddNew"/>，避免迟到数据在没有新连接事件时复活会话。
        /// </summary>
        public Session GetOrAddOnFirstPacket(int connectionId, long nowMs)
        {
            if (_byConnection.TryGetValue(connectionId, out Session session))
            {
                // OnConnected 会通过 TryAddNew 显式替换同 Id 的旧代次。
                // 这里不能把断线会话提前移除并新建：传输层迟到包不应在没有
                // 新 OnConnected 的情况下复活连接。调用方会看到 Disconnected
                // 并丢弃该包；真正的 Id 复用由下一次 TryAddNew 完成。
                return session;
            }
            if (_byConnection.Count >= _capacity) return null;
            session = new Session(connectionId, nowMs, ++_nextEpoch);
            _byConnection[connectionId] = session;
            return session;
        }

        /// <summary>
        /// 周期清理（宿主按帧节拍调用）：移除（a）断线且超出重连窗口的会话；（b）未进房且长期静默的会话。
        /// 返回清理条数（Ops 观测）。
        /// </summary>
        public int Cleanup(long nowMs)
        {
            List<int> dead = null;
            foreach (var kv in _byConnection)
            {
                Session s = kv.Value;
                long idle = nowMs - s.LastSeenMs;
                bool expired = s.Disconnected
                    ? idle > DisconnectedTtlMs
                    : (s.PlayerId < 0 && idle > UnseatedIdleTtlMs);
                if (!expired) continue;
                (dead ??= new List<int>()).Add(kv.Key);
            }

            if (dead == null) return 0;
            for (int i = 0; i < dead.Count; i++) _byConnection.Remove(dead[i]);
            return dead.Count;
        }

        /// <summary>遍历会话（Ops/心跳巡检用）。</summary>
        public IEnumerable<Session> All()
        {
            foreach (var kv in _byConnection) yield return kv.Value;
        }
    }
}
