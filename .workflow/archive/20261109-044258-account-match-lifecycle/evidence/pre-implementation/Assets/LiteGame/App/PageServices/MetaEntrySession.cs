using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.Modules.UI;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// <see cref="IMetaSession"/> 的生产实现：把 <see cref="MetaEntryService"/> 的 MetaCall 收口成
    /// 页面可 await 的结果（失败原因映射/地图名解析/时间格式化沿用既有稳定口径）。
    /// </summary>
    public sealed class MetaEntrySession : IMetaSession
    {
        /// <summary>战绩查询条数（服务端上限 100 裁剪）。</summary>
        public const int RecordsLimit = 20;

        private readonly MetaEntryService _meta;
        private readonly IRoomFlow _rooms;
        private readonly Func<cfg.Tables> _tables;
        private readonly IAccountReentry _reentry;

        public MetaEntrySession(MetaEntryService meta, IRoomFlow rooms, Func<cfg.Tables> tables, IAccountReentry reentry = null)
        {
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _tables = tables;
            _reentry = reentry;
        }

        public bool IsConfigured => _meta.IsConfigured;
        public string AccountId => _meta.AccountId ?? string.Empty;
        public string ServerUrl => _meta.ServerUrl ?? string.Empty;
        public string Version => _meta.BuildHash ?? string.Empty;

        public async UniTask<MetaResult<bool>> GuestLoginAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<bool>.Fail("NotConfigured");
            MetaCall<GuestLoginInfo> call = await _meta.GuestLoginAsync();
            ct.ThrowIfCancellationRequested();
            if (call.Ok) return MetaResult<bool>.Success(true);
            await ReenterIfAuthLostAsync(call.Failure);
            return MetaResult<bool>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<IReadOnlyList<MetaRoomRow>>> ListRoomsAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<IReadOnlyList<MetaRoomRow>>.Fail("NotConfigured");
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = await _meta.ListRoomsAsync();
            ct.ThrowIfCancellationRequested();
            if (!call.Ok) return MetaResult<IReadOnlyList<MetaRoomRow>>.Fail(Reason(call.Failure));

            var rows = new List<MetaRoomRow>();
            if (call.Value != null)
            {
                for (int i = 0; i < call.Value.Count; i++)
                {
                    LobbyInstanceView instance = call.Value[i];
                    if (instance.Rooms == null) continue;
                    for (int j = 0; j < instance.Rooms.Count; j++)
                    {
                        LobbyRoomEntry room = instance.Rooms[j];
                        rows.Add(new MetaRoomRow(room.RoomId, room.Players, room.ExpectedPlayers,
                            room.MapId, MapDisplayName(room.MapId)));
                    }
                }
            }
            return MetaResult<IReadOnlyList<MetaRoomRow>>.Success(rows);
        }

        public async UniTask<MetaResult<string>> QuickEnterAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("NotConfigured");
            MetaCall<JoinTicketInfo> call = await _rooms.EnterRoomAsync("", ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<string>> CreateRoomAsync(int mapId, int expectedPlayers, CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("NotConfigured");
            MetaCall<JoinTicketInfo> call = await _rooms.CreateRoomAsync(mapId, expectedPlayers, ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<string>> EnterRoomAsync(string roomId, CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("NotConfigured");
            MetaCall<JoinTicketInfo> call = await _rooms.EnterRoomAsync(roomId ?? string.Empty, ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<IReadOnlyList<MetaRecordRow>>> ListRecordsAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<IReadOnlyList<MetaRecordRow>>.Fail("NotConfigured");
            MetaCall<IReadOnlyList<MatchRecordInfo>> call = await _meta.ListRecordsAsync(RecordsLimit);
            ct.ThrowIfCancellationRequested();
            if (!call.Ok) return MetaResult<IReadOnlyList<MetaRecordRow>>.Fail(Reason(call.Failure));

            var rows = new List<MetaRecordRow>();
            if (call.Value != null)
            {
                for (int i = 0; i < call.Value.Count; i++)
                {
                    MatchRecordInfo record = call.Value[i];
                    rows.Add(new MetaRecordRow(record.MatchId, record.Kills, record.Deaths,
                        ((LiteSim.MatchEndReason)record.GameplayEndReason).ToString(), FormatTime(record.FinishedAtMs)));
                }
            }
            return MetaResult<IReadOnlyList<MetaRecordRow>>.Success(rows);
        }

        public void LeaveMatch() => _rooms.LeaveMatch();

        /// <summary>地图行键 → 显示名（0/缺行/表未装载 = 空串——纯显示面不抛）。</summary>
        private string MapDisplayName(int mapId)
        {
            if (mapId <= 0 || _tables == null) return string.Empty;
            cfg.map row = _tables().Tbmap?.GetOrDefault(mapId);
            return row?.Name ?? string.Empty;
        }

        /// <summary>本地时间显示（恒 InvariantCulture；0 = 无时间面）。</summary>
        private static string FormatTime(long finishedAtMs)
            => finishedAtMs <= 0
                ? string.Empty
                : DateTimeOffset.FromUnixTimeMilliseconds(finishedAtMs).ToLocalTime()
                    .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>凭证失效 = 恢复链的重新登录（先回报页面失败，再走单点重入）。</summary>
        private async UniTask ReenterIfAuthLostAsync(MetaFailure failure)
        {
            if (_reentry == null || failure == null || !failure.IsAuthLost) return;
            try { await _reentry.ReturnToLoginAsync("meta." + Reason(failure)); }
            catch (Exception ex) { LiteFramework.Log.Error("账号重入失败:" + ex.Message, LiteFramework.LogTag.Meta); }
        }

        /// <summary>失败原因稳定文本（Network/InvalidResponse/HTTP&lt;码&gt;/稳定错误码）。</summary>
        private static string Reason(MetaFailure failure)
        {
            if (failure == null) return "Internal";
            switch (failure.Kind)
            {
                case MetaFailureKind.Network: return "Network";
                case MetaFailureKind.InvalidResponse: return "InvalidResponse";
                default:
                    return string.IsNullOrEmpty(failure.Code) ? "HTTP" + failure.HttpStatus : failure.Code;
            }
        }
    }
}
