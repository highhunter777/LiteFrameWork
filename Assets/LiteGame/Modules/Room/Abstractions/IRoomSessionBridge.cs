namespace LiteGame
{
    /// <summary>
    /// 房间会话门面装订端口（模块侧契约）：Battle 阶段进/出时装订/摘除当前对局会话，
    /// 供 <c>Bridge.room</c> 读取房主事实/发提前开局请求。实现归 Lua 桥适配器，
    /// 流程只经本端口驱动，不持有会话态门面本体。
    /// </summary>
    public interface IRoomSessionBridge
    {
        /// <summary>装订当前会话（幂等：重复装订先摘旧）。</summary>
        void BindSession(IRoomSessionFace session);

        /// <summary>摘除会话（Battle 阶段收尾必调——页面订阅不得命中死会话）。</summary>
        void UnbindSession();
    }
}