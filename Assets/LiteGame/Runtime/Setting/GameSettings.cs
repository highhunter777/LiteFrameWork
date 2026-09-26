using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 设置项的**唯一消费面**（DI 单例）。**业务层件**——2026-09-10 从框架 Core 迁出：
    /// 机制（SettingService 存储引擎 / Setting&lt;T&gt; 强类型封装）留框架，**具体清单与键值属业务**。
    /// 具名 Setting 字段即全部设置项的清单——IDE 展开即见，键与默认值在此单源。
    /// 业务**禁止**直接消费 SettingService 的裸字符串 API（与 Fsm 数据字典同病的收口）。
    /// 新增设置项 = 这里加一个字段，零机制改动。Source Generator 的入场线：设置键 &gt; 50 或多团队消费时再评估。
    /// </summary>
    public sealed class GameSettings
    {
        public Setting<float> MasterVolume { get; }
        public Setting<int> Quality { get; }
        public Setting<string> Language { get; }
        public Setting<bool> Fullscreen { get; }

        public GameSettings(SettingService svc)
        {
            if (svc == null) throw new ArgumentNullException(nameof(svc));
            MasterVolume = new Setting<float>(svc, "volume", 0.8f, v => Math.Clamp(v, 0f, 1f));
            Quality      = new Setting<int>(svc, "quality", 2, v => Math.Clamp(v, 0, 3));
            Language     = new Setting<string>(svc, "lang", "zh-CN");
            Fullscreen   = new Setting<bool>(svc, "fullscreen", true);
        }
    }
}
