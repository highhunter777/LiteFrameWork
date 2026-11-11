using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;
using LiteGame.Modules.Meta.Abstractions.Room;

namespace LiteGame.Modules.Meta.Abstractions
{
    /// <summary>
    /// 纯票据边界（流程域消费）：只提供登录态事实、登出与取票/建房取票，不解析地址、不推进流程、不暴露访问令牌。
    /// </summary>
    public interface IRoomTicketSource
    {
        /// <summary>是否配置了联机入口。</summary>
        bool IsConfigured { get; }

        /// <summary>当前登录账号（null = 未登录）。</summary>
        string AccountId { get; }

        /// <summary>是否已登录（令牌在手）。</summary>
        bool IsLoggedIn { get; }

        /// <summary>清登录态（不落令牌原文）。</summary>
        void Logout(string reason);

        /// <summary>按房间号换票（roomId 空 = 服务端缺省房）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId, CancellationToken ct = default);

        /// <summary>按注入的默认模板/口令建房取票。</summary>
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default);

        /// <summary>按显式模板/口令建房取票（GM 云端测试房）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default);
    }
}
