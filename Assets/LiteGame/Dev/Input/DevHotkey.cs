namespace LiteClient
{
    /// <summary>DevHUD 用到的功能键（不暴露 KeyCode，便于输入后端切换）。</summary>
    public enum DevKey
    {
        F1, F2, F3, F4, F5, F6, F7, F8, F9,
        Backspace,
        Enter,
        Escape,
    }

    /// <summary>
    /// 开发工具热键/文本输入门面（开发装配层）：键位名与 UI 语义在这层，
    /// 实际键盘读取走平台能力 <see cref="PlatformKeyInput"/>——R12 禁 App/Dev 直接 import InputSystem。
    ///
    /// 本类只在 `LiteGame.Dev` 编译（开发装配层约束），发布包无此面；
    /// 平台层 <c>PlatformKeyInput</c> 无开发宏，但发布构建没有调用点（消费者全在本程序集）。
    /// </summary>
    public static class DevHotkey
    {
        public static bool Pressed(DevKey key) => PlatformKeyInput.Pressed(ToPlatform(key));

        /// <summary>文本输入所有权：同一时刻只允许一个调试面收字符（HUD 过滤 / 控制台互斥）。
        /// 返回 false = 另一个 owner 正占用（调用方自行提示/重试），不抢占、不静默吞字。</summary>
        public static bool SetTextInputActive(object owner, bool active)
            => PlatformKeyInput.SetTextInputActive(owner, active);

        /// <summary>取走本帧输入的可见字符（仅当前 owner；不含退格/回车等控制键）。</summary>
        public static string TakeTypedText(object owner) => PlatformKeyInput.TakeTypedText(owner);

        private static PlatformKey ToPlatform(DevKey key)
        {
            switch (key)
            {
                case DevKey.F1: return PlatformKey.F1;
                case DevKey.F2: return PlatformKey.F2;
                case DevKey.F3: return PlatformKey.F3;
                case DevKey.F4: return PlatformKey.F4;
                case DevKey.F5: return PlatformKey.F5;
                case DevKey.F6: return PlatformKey.F6;
                case DevKey.F7: return PlatformKey.F7;
                case DevKey.F8: return PlatformKey.F8;
                case DevKey.F9: return PlatformKey.F9;
                case DevKey.Backspace: return PlatformKey.Backspace;
                case DevKey.Enter: return PlatformKey.Enter;
                case DevKey.Escape: return PlatformKey.Escape;
                default: return PlatformKey.Escape;
            }
        }
    }
}
