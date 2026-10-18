using System;
using System.Collections.Generic;
using MetaServer.Contracts.Lobby;
using MetaServer.Contracts.Validation;
using MetaServer.Modules.Lobby;

namespace MetaServer.Modules.Lobby
{
    /// <summary>
    /// 创建房间用例（房主开局批）：**出号 + 按号签发**——服务端生成保证唯一的新房号，
    /// 随后**内联复用 <see cref="JoinTicketIssueUseCase"/>** 完成校验/实例分配/盖章签发
    /// （三面单源，创建批零复制）；响应形状 = 进房票据（客户端拿新号走既有进房管道）。
    ///
    /// **创建语义的边界（如实）**：房间实体仍随**首入座者**物化（房间服对未知房号自动
    /// OpenRoom，开房者=房主）——本用例保证的是"出号唯一 + 票据即达"，不是预占房实体；
    /// 创建后不入房 = 号作废（保留窗过期自动让位，无幽灵房）。
    ///
    /// **防撞号两层**：
    /// - 对照 `InstanceRegistry.ProjectRooms()` 的既有号（跨实例明细投影——出号必须避让所有在册房间）；
    /// - **保留集**（出号 → 到期时刻，TTL 内不重出）：覆盖"签发→首入座→心跳上报注册表"窗口——
    ///   该窗口内新号不在投影里，若无保留集，并发创建可能重号。单 Meta 实例形态下的进程内保证
    ///   （与注册表同口径——多 Meta 形态需要共享存储，属后续部署形态）。
    /// 保留窗过期自动让位（惰性清扫，与注册表同款；创建后不入房即无幽灵房）。
    ///
    /// - **结果型返回**：复用 <see cref="JoinTicketIssueUseCase.Result"/>（Accepted/Rejected/NoCapacity
    ///   同形状——端点映射与客户端解析零新面）。
    /// - **确定性**：出号经 <paramref name="idSegment"/> 注入缝（默认随机段）——测试注序列即可钉死
    ///   避让/重试行为，不依赖真随机。
    /// </summary>
    public sealed class CreateRoomUseCase
    {
        /// <summary>出号前缀（与既有 "Room-A"/"Room-Test" 手工形态可辨——生成号一眼可辨）。</summary>
        public const string IdPrefix = "R-";

        /// <summary>随机段长度（32 字母表去易混字符，6 位 ≈ 10 亿组合——对照注册表防撞后碰撞率可忽略）。</summary>
        public const int IdRandomLength = 6;

        /// <summary>出号保留窗（毫秒）：覆盖"签发 → 首入座 → 房间服心跳上报注册表"的往返（心跳秒级——2 分钟为富余）。</summary>
        public const long ReservedIdTtlMs = 120_000;

        /// <summary>出号重试上限（对照投影与保留集后仍撞号——概率上不可达；耗尽按"无新位"语义）。</summary>
        private const int IdRetryLimit = 16;

        /// <summary>随机段字母表（去 I/1/O/0——房号会被玩家抄写/口播，易混字符剔除）。</summary>
        private const string IdAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly JoinTicketIssueUseCase _ticket;
        private readonly InstanceRegistry _registry;
        private readonly Func<long> _nowMs;
        private readonly Func<string> _idSegment;
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _reservedIds = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <param name="ticket">签发用例（内联复用——校验/分配/盖章单源）。</param>
        /// <param name="registry">实例注册表（房间明细投影——出号避让面）。</param>
        /// <param name="nowMs">单调毫秒时钟（注入——保留窗判定与签发时刻同源）。</param>
        /// <param name="idSegment">随机段缝（默认 6 位随机段；测试注序列钉死避让/重试）。</param>
        public CreateRoomUseCase(JoinTicketIssueUseCase ticket, InstanceRegistry registry,
            Func<long> nowMs, Func<string> idSegment = null)
        {
            _ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _idSegment = idSegment ?? DefaultIdSegment;
        }

        /// <summary>创建房间：出唯一新号 → 按号签发进房票据。结果形状 = 进房票据用例同款（端点零新映射面）。</summary>
        public JoinTicketIssueUseCase.Result Create(string accountId, CreateRoomCommand command)
        {
            // 早闸（避免为非法请求出号）：身份与追踪标识格式先验；其余校验归签发用例单源
            if (!FieldValidation.IsValid(accountId, JoinTicketIssueUseCase.MaxFieldBytes)
                || !FieldValidation.IsValid(command?.RequestId, JoinTicketIssueUseCase.MaxFieldBytes))
                return new JoinTicketIssueUseCase.Result.Rejected
                {
                    ErrorCode = LobbyErrorCodes.InvalidRequest,
                    MessageKey = LobbyErrorCodes.MessageKeyInvalidRequest,
                };

            string roomId = TakeFreshRoomId();
            if (roomId == null)
                return new JoinTicketIssueUseCase.Result.NoCapacity();   // 出号重试耗尽——按"无新位"语义（503）

            return _ticket.Issue(accountId, new JoinTicketCommand
            {
                RequestId = command.RequestId,
                RoomId = roomId,
                BuildHash = command.BuildHash,          // 版本准入前置（空 = 不断言，与进房同语义）
                MapId = command.MapId,                  // 地图行键随票据盖章（多图批：房间端建房时按表校验）
                ExpectedPlayers = command.ExpectedPlayers,   // 期望人数随票据盖章（开房参数批：房间端按内核容量校验）
                Template = command.Template,            // 模板名随票据盖章（联机测试房批；口令校验在 Issue 单点）
                GmKey = command.GmKey,                  // 口令透传给 Issue（唯一校验点）
            });
        }

        /// <summary>出唯一新号：投影避让 + 保留集避让 + 重试上限。null = 重试耗尽。</summary>
        private string TakeFreshRoomId()
        {
            // 先取投影快照（注册表自带锁与清扫——不在本类锁内嵌套它的锁）
            IReadOnlyList<LobbyRoomView> snapshot = _registry.ProjectRooms();

            lock (_gate)
            {
                SweepReservedLocked(_nowMs());
                for (int attempt = 0; attempt < IdRetryLimit; attempt++)
                {
                    string candidate = IdPrefix + _idSegment();
                    if (InProjection(snapshot, candidate) || _reservedIds.ContainsKey(candidate)) continue;
                    _reservedIds[candidate] = _nowMs() + ReservedIdTtlMs;
                    return candidate;
                }
                return null;
            }
        }

        private static bool InProjection(IReadOnlyList<LobbyRoomView> snapshot, string roomId)
        {
            if (snapshot == null) return false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                IReadOnlyList<RoomView> rooms = snapshot[i].Rooms;
                if (rooms == null) continue;
                for (int r = 0; r < rooms.Count; r++)
                    if (string.Equals(rooms[r].RoomId, roomId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void SweepReservedLocked(long now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, long> kv in _reservedIds)
            {
                if (kv.Value > now) continue;
                (expired ??= new List<string>()).Add(kv.Key);
            }
            if (expired == null) return;
            for (int i = 0; i < expired.Count; i++) _reservedIds.Remove(expired[i]);
        }

        private static string DefaultIdSegment()
        {
            Span<char> chars = stackalloc char[IdRandomLength];
            for (int i = 0; i < IdRandomLength; i++)
                chars[i] = IdAlphabet[System.Random.Shared.Next(IdAlphabet.Length)];
            return new string(chars);
        }
    }
}
