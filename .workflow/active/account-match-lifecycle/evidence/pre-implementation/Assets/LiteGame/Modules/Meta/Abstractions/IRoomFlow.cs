using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// 页面/GM 的实质入场与离房动作口（实现住流程域）：负责占用、取票、取消与阶段移交，返回票据事实。
    /// </summary>
    public interface IRoomFlow
    {
        /// <summary>换票并请求入场（roomId 空 = 快速进入）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default);

        /// <summary>按默认建房参数请求入场。</summary>
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default);

        /// <summary>按显式模板/口令建房请求入场（GM 云端测试房）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default);

        /// <summary>离房/取消在途入场；无在途入场且无房间时返回 false（幂等）。</summary>
        bool LeaveMatch();
    }
}
