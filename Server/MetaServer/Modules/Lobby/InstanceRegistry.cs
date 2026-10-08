using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using MetaServer.Contracts.Lobby;

namespace MetaServer.Modules.Lobby
{
    /// <summary>
    /// Lobby 实例注册表（《Meta 服务专项设计》§7"实例注册/容量驱动分配/drain 协同"）。
    ///
    /// - **注册即心跳**：同一 <c>InstanceId</c> 重复注册 = 更新（get-or-create 幂等键）。
    ///   每次注册刷新 <c>LastSeenMs</c>（时钟注入——L1 用虚拟时钟造超时，不 sleep）。
    /// - **心跳超时即移除**：超过时限未再注册的实例从可分配集合消失（惰性清扫：每次
    ///   注册/分配/投影前清一次，无需后台计时器）。
    /// - **容量驱动分配**：只选未排空且 <c>PlayerCount &lt; MaxPlayers</c> 的实例；**不按估算值分配**
    ///   （数据=上报事实）。排序确定：玩家占用少者优先，再比房间占用，再比实例标识（Ordinal）——
    ///   输入相同则结果相同，测试可钉死。
    /// - **有界**（§13"任何队列/缓存…都必须有显式容量与清理策略"）：表满**拒绝新实例**
    ///   （显式拒绝码），不淘汰旧条目——淘汰会静默丢掉存活实例的分配资格。
    /// - **单 Meta 实例形态**：注册表驻进程内存（在线状态、可重建——§9.2 同口径数据）。
    ///   多 Meta 实例部署需要共享存储（Redis 注册表），属后续部署形态，不在本批。
    ///
    /// 线程安全：Kestrel 并发请求共用——字典访问与清扫在锁内；<see cref="Entry"/> 更新用**换对象**
    /// 而非原地改字段，快照读者不看到半更新状态。
    /// </summary>
    public sealed class InstanceRegistry
    {
        /// <summary>字段边界（对齐 §P0-3"解析前限制长度"；与房间端 JoinAdmissionGate 的字段上限同数量级）。</summary>
        public const int MaxInstanceIdBytes = 64;
        public const int MaxBuildHashBytes = 64;
        public const int MaxAddressBytes = 128;

        /// <summary>结构上限（越界 = 上报事实不可信，拒绝而不是裁剪）。</summary>
        public const int MaxRoomsCeiling = 65535;
        public const int MaxPlayersCeiling = 100_000_000;

        /// <summary>注册结果（端点映射：Accepted→200；InvalidFields→400；RegistryFull→503）。</summary>
        public enum RegisterResult : byte
        {
            Accepted = 0,
            InvalidFields = 1,
            RegistryFull = 2,
        }

        /// <summary>一个实例的上报事实（不可变语义：更新以换对象表达）。</summary>
        public sealed class Entry
        {
            public string InstanceId;
            public string BuildHash;
            public string Address;
            public int MaxRooms;
            public int RoomCount;
            public int MaxPlayers;
            public int PlayerCount;
            public bool Draining;
            public long LastSeenMs;

            /// <summary>是否仍有玩家容量（分配硬闸之一）。</summary>
            public bool HasCapacity
            {
                get { return PlayerCount < MaxPlayers; }
            }
        }

        private readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int _capacity;
        private readonly long _heartbeatTtlMs;
        private readonly Func<long> _nowMs;
        private readonly object _gate = new object();
        private long _sweptExpired;

        /// <param name="capacity">注册表容量上限（含存活实例数；满拒新——§13 有界要求）。</param>
        /// <param name="heartbeatTtlMs">心跳存活时限（毫秒；超过即从集合移除）。</param>
        /// <param name="nowMs">单调毫秒时钟（注入——禁用系统墙钟，L1 可虚拟推进）。</param>
        public InstanceRegistry(int capacity, long heartbeatTtlMs, Func<long> nowMs)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "注册表容量必须 > 0");
            if (heartbeatTtlMs <= 0) throw new ArgumentOutOfRangeException(nameof(heartbeatTtlMs), heartbeatTtlMs, "心跳时限必须 > 0");
            _capacity = capacity;
            _heartbeatTtlMs = heartbeatTtlMs;
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        }

        /// <summary>心跳存活时限（注册响应据此回告实例，保持两端口径一致）。</summary>
        public long HeartbeatTtlMs
        {
            get { return _heartbeatTtlMs; }
        }

        /// <summary>当前存活实例数（清扫后）。</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    SweepLocked(_nowMs());
                    return _byId.Count;
                }
            }
        }

        /// <summary>因心跳超时被移除的累计实例数（诊断——持续增长说明实例侧心跳异常）。</summary>
        public long SweptExpired
        {
            get { return Interlocked.Read(ref _sweptExpired); }
        }

        /// <summary>
        /// 注册/心跳（幂等：同 InstanceId 更新字段并刷新时刻）。字段越界 → InvalidFields（reason 指出首项）；
        /// 表满且是新实例 → RegistryFull（既有实例的心跳不受满拒影响）。
        /// </summary>
        public RegisterResult Register(InstanceRegisterCommand command, out string reason)
        {
            reason = null;
            if (command == null) { reason = "payload-missing"; return RegisterResult.InvalidFields; }
            if (!ValidField(command.InstanceId, MaxInstanceIdBytes)) { reason = "instanceId"; return RegisterResult.InvalidFields; }
            if (!ValidField(command.BuildHash, MaxBuildHashBytes)) { reason = "buildHash"; return RegisterResult.InvalidFields; }
            if (!ValidField(command.Address, MaxAddressBytes) || !TryParseHostPort(command.Address)) { reason = "address"; return RegisterResult.InvalidFields; }
            if (command.MaxRooms < 1 || command.MaxRooms > MaxRoomsCeiling) { reason = "maxRooms"; return RegisterResult.InvalidFields; }
            if (command.RoomCount < 0 || command.RoomCount > command.MaxRooms) { reason = "roomCount"; return RegisterResult.InvalidFields; }
            if (command.MaxPlayers < 1 || command.MaxPlayers > MaxPlayersCeiling) { reason = "maxPlayers"; return RegisterResult.InvalidFields; }
            if (command.PlayerCount < 0 || command.PlayerCount > command.MaxPlayers) { reason = "playerCount"; return RegisterResult.InvalidFields; }

            lock (_gate)
            {
                long now = _nowMs();
                SweepLocked(now);

                bool existing = _byId.ContainsKey(command.InstanceId);
                if (!existing && _byId.Count >= _capacity)
                {
                    reason = "registry-full";
                    return RegisterResult.RegistryFull;
                }

                // 换对象而非原地改字段：并发读者持有的快照不会被半更新污染
                _byId[command.InstanceId] = new Entry
                {
                    InstanceId = command.InstanceId,
                    BuildHash = command.BuildHash,
                    Address = command.Address,
                    MaxRooms = command.MaxRooms,
                    RoomCount = command.RoomCount,
                    MaxPlayers = command.MaxPlayers,
                    PlayerCount = command.PlayerCount,
                    Draining = command.Draining,
                    LastSeenMs = now,
                };
                return RegisterResult.Accepted;
            }
        }

        /// <summary>
        /// 容量驱动分配：在**存活、未排空、有玩家容量**的实例中选一个。
        /// <paramref name="requiredBuildHash"/> 非空时只选构建哈希一致的实例（§7 版本准入）；
        /// 无匹配但存在其它版本的可用实例 → <paramref name="versionConflict"/> = true
        /// （调用方区分"版本不符"与"无容量"——两者对客户端的处置不同）。
        /// </summary>
        public bool TryAllocate(string requiredBuildHash, out Entry chosen, out bool versionConflict)
        {
            chosen = null;
            versionConflict = false;
            lock (_gate)
            {
                SweepLocked(_nowMs());

                bool requireBuild = !string.IsNullOrEmpty(requiredBuildHash);
                bool sawOtherBuild = false;
                foreach (KeyValuePair<string, Entry> kv in _byId)
                {
                    Entry e = kv.Value;
                    if (e.Draining || !e.HasCapacity) continue;         // drain 协同：不再分配新对局
                    if (requireBuild && !string.Equals(e.BuildHash, requiredBuildHash, StringComparison.Ordinal))
                    {
                        sawOtherBuild = true;
                        continue;
                    }
                    if (chosen == null || Better(e, chosen)) chosen = e;
                }
                versionConflict = chosen == null && sawOtherBuild;
                return chosen != null;
            }
        }

        /// <summary>
        /// 投影快照（§7"Match 状态投影"：上报事实的投影，不是权威）。清扫后按 Ordinal 实例标识序输出，
        /// <c>LastSeenAgeMs</c> 用注册表时钟计算（时钟单源——端点不另读墙钟）。
        /// </summary>
        public List<LobbyRoomView> ProjectRooms()
        {
            lock (_gate)
            {
                long now = _nowMs();
                SweepLocked(now);
                var list = new List<LobbyRoomView>(_byId.Count);
                foreach (KeyValuePair<string, Entry> kv in _byId)
                {
                    Entry e = kv.Value;
                    list.Add(new LobbyRoomView
                    {
                        InstanceId = e.InstanceId,
                        BuildHash = e.BuildHash,
                        Address = e.Address,
                        MaxRooms = e.MaxRooms,
                        RoomCount = e.RoomCount,
                        MaxPlayers = e.MaxPlayers,
                        PlayerCount = e.PlayerCount,
                        Draining = e.Draining,
                        LastSeenAgeMs = now - e.LastSeenMs,
                    });
                }
                list.Sort((a, b) => string.CompareOrdinal(a.InstanceId, b.InstanceId));
                return list;
            }
        }

        /// <summary>玩家占用少者优先；同占用比房间数；再同则 Ordinal 实例标识序（确定性）。</summary>
        private static bool Better(Entry candidate, Entry current)
        {
            if (candidate.PlayerCount != current.PlayerCount) return candidate.PlayerCount < current.PlayerCount;
            if (candidate.RoomCount != current.RoomCount) return candidate.RoomCount < current.RoomCount;
            return string.CompareOrdinal(candidate.InstanceId, current.InstanceId) < 0;
        }

        private void SweepLocked(long now)
        {
            List<string> dead = null;
            foreach (KeyValuePair<string, Entry> kv in _byId)
            {
                if (now - kv.Value.LastSeenMs <= _heartbeatTtlMs) continue;
                (dead ??= new List<string>()).Add(kv.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _byId.Remove(dead[i]);
            Interlocked.Add(ref _sweptExpired, dead.Count);
        }

        private static bool ValidField(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return Encoding.UTF8.GetByteCount(value) <= maxBytes;
        }

        /// <summary>地址形状：<c>host:port</c>（host 非空、port 十进制 1..65535）。</summary>
        private static bool TryParseHostPort(string address)
        {
            int sep = address.LastIndexOf(':');
            if (sep <= 0 || sep == address.Length - 1) return false;
            if (!int.TryParse(address.Substring(sep + 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int port)) return false;
            return port >= 1 && port <= 65535;
        }
    }
}
