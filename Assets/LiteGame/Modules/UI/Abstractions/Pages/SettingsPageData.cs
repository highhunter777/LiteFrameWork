using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>设置页导航数据：对局中打开（battleExit）= 显示类置灰并给退场钮；exitKey 覆盖退场文案。</summary>
    public sealed class SettingsPageData : IUIData
    {
        public readonly bool BattleExit;
        public readonly string ExitKey;

        public SettingsPageData(bool battleExit, string exitKey = null)
        {
            BattleExit = battleExit;
            ExitKey = exitKey;
        }
    }
}
