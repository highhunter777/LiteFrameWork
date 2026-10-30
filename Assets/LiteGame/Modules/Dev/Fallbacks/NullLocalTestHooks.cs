using LiteFramework;
using LiteSim;
using LiteFramework;
using LiteGame;

namespace LiteGame.Modules.Dev
{
    /// <summary>发布形态的本地测试钩子：全关空实现（写入丢弃、读取恒默认），不产生任何测试副作用。</summary>
    internal sealed class NullLocalTestHooks : ILocalTestHooks
    {
        public static readonly NullLocalTestHooks Instance = new NullLocalTestHooks();

        private NullLocalTestHooks() { }

        public bool Active { get; set; }
        public bool BotFrozen { get; set; }
        public bool BotInfiniteHp { get; set; }
        public bool BotResetRequested { get; set; }
        public bool TeleportDispatch { get; set; }
        public SimVector3 TeleportTarget { get; set; }
    }
}
