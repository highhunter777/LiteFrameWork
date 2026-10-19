using System;
using System.Collections.Generic;
using System.Threading;
using MetaServer.Contracts.Lobby;
using MetaServer.Contracts.Validation;
using RoomServer.Runtime;

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
        /// <summary>字段边界（对齐 §P0-3"解析前限制长度"；**别名**——上限表单源 <see cref="FieldLimits"/>）。</summary>
        public const int MaxInstanceIdBytes = FieldLimits.BusinessKeyBytes;
        public const int MaxBuildHashBytes = FieldLimits.BusinessKeyBytes;
        public const int MaxAddressBytes = FieldLimits.AddressBytes;

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

            /// <summary>房间明细（房间列表批；null = 未上报。换对象语义——每次注册换新表）。</summary>
            public List<RoomView> Rooms;

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
            if (!FieldValidation.IsValid(command.InstanceId, MaxInstanceIdBytes)) { reason = "instanceId"; return RegisterResult.InvalidFields; }
            if (!FieldValidation.IsValid(command.BuildHash, MaxBuildHashBytes)) { reason = "buildHash"; return RegisterResult.InvalidFields; }
            if (!FieldValidation.IsValid(command.Address, MaxAddressBytes) || !TryParseHostPort(command.Address)) { reason = "address"; return RegisterResult.InvalidFields; }
            if (command.MaxRooms < 1 || command.MaxRooms > MaxRoomsCeiling) { reason = "maxRooms"; return RegisterResult.InvalidFields; }
            if (command.RoomCount < 0 || command.RoomCount > command.MaxRooms) { reason = "roomCount"; return RegisterResult.InvalidFields; }
            if (command.MaxPlayers < 1 || command.MaxPlayers > MaxPlayersCeiling) { reason = "maxPlayers"; return RegisterResult.InvalidFields; }
            if (command.PlayerCount < 0 || command.PlayerCount > command.MaxPlayers) { reason = "playerCount"; return RegisterResult.InvalidFields; }

            // 房间明细（房间列表批；可空 = 未上报——旧实例兼容）。越界 = 上报事实不可信，拒绝不裁剪。
            List<RoomView> rooms = null;
            if (command.Rooms != null)
            {
                if (command.Rooms.Count > command.MaxRooms) { reason = "rooms"; return RegisterResult.InvalidFields; }
                rooms = new List<RoomView>(command.Rooms.Count);
                for (int i = 0; i < command.Rooms.Count; i++)
                {
                    RoomReport r = command.Rooms[i];
                    if (r == null || !FieldValidation.IsValid(r.RoomId, MaxInstanceIdBytes)) { reason = "rooms[" + i + "].roomId"; return RegisterResult.InvalidFields; }
                    // 相位闸引内核单源（MatchPhase/MatchStateMachine 经 MetaServer.csproj 源链接共编）：值域与
                    // 终态判据均由枚举派生——内核新增相位时本闸自动跟随（此前的字面量 0..5 绑注释，属静默漂移面）。
                    // 终态（Closed/Aborted）按契约"终态房间不报"（LobbyContracts.RoomReport）恒不该出现——
                    // 房间侧读面已过滤，此处为防御面，拒绝粒度维持整次注册（与实例级字段同款）。
                    if (r.Phase < (int)MatchPhase.Created || r.Phase > (int)MatchPhase.Aborted
                        || MatchStateMachine.IsTerminal((MatchPhase)r.Phase))
                    { reason = "rooms[" + i + "].phase"; return RegisterResult.InvalidFields; }
                    if (r.Players < 0 || r.Players > MaxPlayersCeiling) { reason = "rooms[" + i + "].players"; return RegisterResult.InvalidFields; }
                    if (r.ExpectedPlayers < 0 || r.ExpectedPlayers > MaxPlayersCeiling) { reason = "rooms[" + i + "].expectedPlayers"; return RegisterResult.InvalidFields; }
                    // 地图行键（多图批）：0 = 未标注合法；负值 = 上报错误（值域上界归房间端表校验，Meta 不查表）
                    if (r.MapId < 0) { reason = "rooms[" + i + "].mapId"; return RegisterResult.InvalidFields; }
                    rooms.Add(new RoomView { RoomId = r.RoomId, Phase = r.Phase, Players = r.Players, ExpectedPlayers = r.ExpectedPlayers, MapId = r.MapId });
                }
            }

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
                    Rooms = rooms,                                   // 房间列表批：未上报（null）保持未上报语义
                };
                return RegisterResult.Accepted;
            }
        }

        /// <summary>
        /// 按房号归属分配的结果（多实例正确性：**房号只能落在"报了它的那台实例"上**——
        /// 换实例发票据会让房间服对未知房号懒创建 ⇒ 同名房出现在两台实例上，原房队友永远等不到人）。
        /// </summary>
        public enum RoomAllocation : byte
        {
            /// <summary>该房号不在任何实例的房间明细里（未上报/已销毁）——调用方按负载分配兜底。</summary>
            Unknown = 0,

            /// <summary>命中归属实例（<c>chosen</c> 非空）。</summary>
            Found = 1,

            /// <summary>房号在某实例上，但该实例在排空——房间服对排空实例拒绝新进房，不得改投别处另建同名房。</summary>
            Draining = 2,

            /// <summary>房号在某实例上，但实例构建哈希与请求不一致（版本准入，不换实例兜底）。</summary>
            VersionMismatch = 3,
        }

        /// <summary>
        /// 容量驱动分配：在**存活、未排空、有玩家容量**的实例中选一个。
        /// <paramref name="requiredBuildHash"/> 非空时只选构建哈希一致的实例（§7 版本准入）；
        /// 无匹配但存在其它版本的可用实例 → <paramref name="versionConflict"/> = true
        /// （调用方区分"版本不符"与"无容量"——两者对客户端的处置不同）。
        /// <paramref name="requireRoomSlot"/> = true 时额外要求"房间表未满"（RoomCount &lt; MaxRooms）：
        /// **新建房**才需要它——房间普遍坐不满的常态下 RoomCount 先到上限而 PlayerCount 还没到
        /// （MaxPlayers = MaxRooms × 最宽模板人数），只看玩家容量会给"建不出房"的实例发票据。
        /// </summary>
        public bool TryAllocate(string requiredBuildHash, out Entry chosen, out bool versionConflict,
            bool requireRoomSlot = false)
        {
            chosen = null;
            versionConflict = false;
            lock (_gate)
            {
                SweepLocked(_nowMs());

                bool requireBuild = !string.IsNullOrEmpty(requiredBuildHash);
                bool sawOtherBuild = false;
                bool sawMatchingBuild = false;
                foreach (KeyValuePair<string, Entry> kv in _byId)
                {
                    Entry e = kv.Value;
                    if (e.Draining || !e.HasCapacity) continue;         // drain 协同：不再分配新对局
                    if (requireBuild && !string.Equals(e.BuildHash, requiredBuildHash, StringComparison.Ordinal))
                    {
                        sawOtherBuild = true;
                        continue;
                    }
                    if (requireBuild) sawMatchingBuild = true;           // 构建匹配（可能仅因房间表满而不可用）
                    if (requireRoomSlot && e.RoomCount >= e.MaxRooms) continue;   // 房间表满：新房建不出来
                    if (chosen == null || Better(e, chosen)) chosen = e;
                }
                // 版本冲突只在"确实没有本构建的可用实例"时报（房间表满不算版本问题——那是容量问题）
                versionConflict = chosen == null && sawOtherBuild && !sawMatchingBuild;
                return chosen != null;
            }
        }

        /// <summary>
        /// 按**房号归属**分配（多实例正确性，见 <see cref="RoomAllocation"/>）：房号出现在实例上报的
        /// 房间明细里 ⇒ 那台就是它的归属。多台都报了同号（旧缺陷遗留的同名房）时优先有空位的、再按
        /// <see cref="Better"/> 定序（确定性）。房间明细未上报（null）的实例不参与——判据只能是上报事实。
        /// </summary>
        public RoomAllocation TryAllocateForRoom(string roomId, string requiredBuildHash, out Entry chosen)
        {
            chosen = null;
            if (string.IsNullOrEmpty(roomId)) return RoomAllocation.Unknown;

            lock (_gate)
            {
                SweepLocked(_nowMs());

                bool requireBuild = !string.IsNullOrEmpty(requiredBuildHash);
                bool roomSeen = false;
                bool buildMismatch = false;
                bool chosenFreeSeat = false;

                foreach (KeyValuePair<string, Entry> kv in _byId)
                {
                    Entry e = kv.Value;
                    if (!ContainsRoom(e, roomId, out bool hasFreeSeat)) continue;
                    roomSeen = true;
                    if (e.Draining) continue;                            // 排空实例不接新进房（房间服同口径）
                    if (requireBuild && !string.Equals(e.BuildHash, requiredBuildHash, StringComparison.Ordinal))
                    {
                        buildMismatch = true;
                        continue;
                    }
                    if (chosen == null || BetterRoomOwner(e, hasFreeSeat, chosen, chosenFreeSeat))
                    {
                        chosen = e;
                        chosenFreeSeat = hasFreeSeat;
                    }
                }

                if (chosen != null) return RoomAllocation.Found;
                // 归属存在但不可用：**不回落负载分配**（换实例 = 另建同名房）。版本不符优先于排空上报：
                // 前者是客户端构建问题（换包），后者是该实例生命周期问题（等或换房）。
                if (buildMismatch) return RoomAllocation.VersionMismatch;
                return roomSeen ? RoomAllocation.Draining : RoomAllocation.Unknown;
            }
        }

        /// <summary>实例的房间明细里是否有该房号；<paramref name="hasFreeSeat"/> = 至少一条明细还有空位
        /// （ExpectedPlayers 为 0 的"未标注"行不算空位）。</summary>
        private static bool ContainsRoom(Entry e, string roomId, out bool hasFreeSeat)
        {
            hasFreeSeat = false;
            if (e.Rooms == null) return false;
            bool found = false;
            for (int i = 0; i < e.Rooms.Count; i++)
            {
                RoomView r = e.Rooms[i];
                if (r == null || !string.Equals(r.RoomId, roomId, StringComparison.Ordinal)) continue;
                found = true;
                if (r.ExpectedPlayers > 0 && r.Players < r.ExpectedPlayers) hasFreeSeat = true;
            }
            return found;
        }

        /// <summary>归属实例比较：先有空位者，再按 <see cref="Better"/>（确定性）。</summary>
        private static bool BetterRoomOwner(Entry candidate, bool candidateFree, Entry current, bool currentFree)
        {
            if (candidateFree != currentFree) return candidateFree;
            return Better(candidate, current);
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
                        Rooms = e.Rooms,                               // 房间列表批：明细直映（null = 未上报）
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
