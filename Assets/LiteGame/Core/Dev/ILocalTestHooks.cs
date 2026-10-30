using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 本地测试房宿主钩子（开发/沙箱面端口）：产品只经本接口读写，具体实例住 `LiteGame.Dev`。
    ///
    /// 信任边界：这些开关只服务进程内本地服（冻结/回满/重置/传送），不得进入真服务器判定链；
    /// 发布形态由 `NullDevSession` 提供全关空实现。
    /// </summary>
    public interface ILocalTestHooks
    {
        /// <summary>本地测试模式进行中（宿主循环据此执行钩子）。</summary>
        bool Active { get; set; }

        /// <summary>bot 冻结：权威侧每帧回写补位席位位置。</summary>
        bool BotFrozen { get; set; }

        /// <summary>bot 无限生命：权威侧每帧回满补位席位。</summary>
        bool BotInfiniteHp { get; set; }

        /// <summary>重置假人：权威侧执行一次（消费即清）。</summary>
        bool BotResetRequested { get; set; }

        /// <summary>传送分发：待权威侧执行一次。</summary>
        bool TeleportDispatch { get; set; }

        /// <summary>传送目标点（世界坐标）。</summary>
        SimVector3 TeleportTarget { get; set; }
    }
}
