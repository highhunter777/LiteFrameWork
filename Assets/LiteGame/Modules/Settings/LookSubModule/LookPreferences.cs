using System;
using LiteFramework;

namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// <see cref="ILookPreferences"/> 的实现：把 <see cref="GameSettings"/> 的三项读成**缓存值**。
    ///
    /// 为什么要缓存：<c>Setting&lt;T&gt;.Get()</c> 每次都走字典查找 + 字符串解析，放在逐帧的
    /// 视角计算里是纯浪费；设置变更时由 <see cref="ProductSettings"/> 调 <see cref="Refresh"/> 刷新，
    /// 读取端（<see cref="BattleLookController"/>）只读字段。
    /// </summary>
    public sealed class LookPreferences : ILookPreferences
    {
        private readonly GameSettings _settings;

        public LookPreferences(GameSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Refresh();
        }

        public float SensitivityMultiplier { get; private set; }
        public float AdsScaleMultiplier { get; private set; }
        public bool InvertY { get; private set; }

        /// <summary>重新读取设置（改值/恢复默认后调用；对局中即时生效，无需重开）。</summary>
        public void Refresh()
        {
            SensitivityMultiplier = _settings.LookSensitivity.Get();
            AdsScaleMultiplier = _settings.AdsSensitivity.Get();
            InvertY = _settings.InvertLookY.Get();
        }
    }
}
