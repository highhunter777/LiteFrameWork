using System.Collections.Generic;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// 响应必填校验（反射反序列化之后的一步）。
    ///
    /// **为什么需要**：反序列化只保证"能构造"，不保证"字段有值"——服务端少发一个字段，
    /// 属性就是 null/0，客户端会带着空值继续跑并在更远处炸。这里把"缺必填"变成
    /// <see cref="MetaFailureKind.InvalidResponse"/>（确定错误态，重试无效），与既有失败分类一致。
    ///
    /// **新增 DTO 字段时必须同步归类**——`MetaWireContractTests` 会反射遍历客户端成功响应 DTO 的
    /// 每个属性，要求显式归入“必填/可选”；必填缺失即判非法，新增属性未分类即红。
    /// </summary>
    internal static class MetaWireValidator
    {
        internal static bool TryRequire(GuestLoginInfo value, out string reason)
        {
            if (value == null) { reason = "响应为空"; return false; }
            if (string.IsNullOrEmpty(value.AccountId)) { reason = "缺 accountId"; return false; }
            if (string.IsNullOrEmpty(value.AccessToken)) { reason = "缺 accessToken"; return false; }
            if (string.IsNullOrEmpty(value.TokenType)) { reason = "缺 tokenType"; return false; }
            if (value.ExpiresAtMs <= 0) { reason = "缺 expiresAtMs"; return false; }
            reason = null;
            return true;
        }

        internal static bool TryRequire(JoinTicketInfo value, out string reason)
        {
            if (value == null) { reason = "响应为空"; return false; }
            if (string.IsNullOrEmpty(value.InstanceId)) { reason = "缺 instanceId"; return false; }
            if (string.IsNullOrEmpty(value.Address)) { reason = "缺 address"; return false; }
            if (string.IsNullOrEmpty(value.RoomId)) { reason = "缺 roomId"; return false; }
            if (string.IsNullOrEmpty(value.Ticket)) { reason = "缺 ticket"; return false; }
            if (value.ExpiresAtMs <= 0) { reason = "缺 expiresAtMs"; return false; }
            reason = null;
            return true;
        }

        internal static bool TryRequire(RoomsResponse value, out string reason)
        {
            if (value == null) { reason = "响应为空"; return false; }
            if (value.Rooms == null) { reason = "缺 rooms 数组"; return false; }
            for (int i = 0; i < value.Rooms.Count; i++)
            {
                LobbyInstanceView instance = value.Rooms[i];
                if (instance == null) { reason = "rooms 元素为空"; return false; }
                if (string.IsNullOrEmpty(instance.InstanceId)) { reason = "实例行缺 instanceId"; return false; }
                if (string.IsNullOrEmpty(instance.Address)) { reason = "实例行缺 address"; return false; }
                if (instance.Rooms == null) continue;      // 未上报明细 = 无房，合法
                for (int r = 0; r < instance.Rooms.Count; r++)
                {
                    LobbyRoomEntry room = instance.Rooms[r];
                    if (room == null) { reason = "房间行为空"; return false; }
                    if (string.IsNullOrEmpty(room.RoomId)) { reason = "房间行缺 roomId"; return false; }
                }
            }
            reason = null;
            return true;
        }

        internal static bool TryRequire(MatchesResponse value, out string reason)
        {
            if (value == null) { reason = "响应为空"; return false; }
            if (value.Matches == null) { reason = "缺 matches 数组"; return false; }
            for (int i = 0; i < value.Matches.Count; i++)
            {
                MatchRecordInfo record = value.Matches[i];
                if (record == null) { reason = "matches 元素为空"; return false; }
                if (string.IsNullOrEmpty(record.MatchId)) { reason = "战绩行缺 matchId"; return false; }
                if (record.FinishedAtMs <= 0) { reason = "战绩行缺 finishedAtMs"; return false; }
            }
            reason = null;
            return true;
        }
    }

    /// <summary>必填校验签名（供 <see cref="MetaJson"/> 的通用解析路径复用）。</summary>
    internal delegate bool MetaWireRequire<in T>(T value, out string reason);

    /// <summary>集合包装：null 归一为空集合（对外仍是 `IReadOnlyList`，既有接口面不变）。</summary>
    internal static class MetaWireCollections
    {
        internal static IReadOnlyList<T> OrEmpty<T>(List<T> items)
            => items == null ? (IReadOnlyList<T>)System.Array.Empty<T>() : items;
    }
}
