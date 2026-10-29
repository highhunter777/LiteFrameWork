using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 设置项的**唯一消费面**（DI 单例）。**业务层件**：机制（SettingService 存储引擎 /
    /// Setting&lt;T&gt; 强类型封装）留框架，**具体清单与键值属业务**。
    /// 具名 Setting 字段即全部设置项的清单——IDE 展开即见，键与默认值在此单源。
    /// 业务**禁止**直接消费 SettingService 的裸字符串 API（与 Fsm 数据字典同病的收口）。
    /// 新增设置项 = 这里加一个字段，零机制改动。Source Generator 的入场线：设置键 &gt; 50 或多团队消费时再评估。
    ///
    /// **分组即生效面**（《设置系统专项设计》§3）：音频（音量/音效）、图形（画质预设 + 三项覆盖）、
    /// 显示（模式/分辨率/帧率/垂直同步，桌面专属）、控制（视角倍率/反转；键位覆盖为 P2）、常规（语言/页签记忆）。
    /// 值域收敛点：本类 clamp（第一道）→ 生效器（第二道）→ 门面（第三道），见设计 §5.3。
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>设置页面签数（与 `Page_Settings.prefab` 的 TabGroup 一致；`LastTab` 的钳制上界）。</summary>
        public const int SettingsTabCount = 5;

        // ---- 音频 ----
        public Setting<float> MasterVolume { get; }
        public Setting<bool> SoundEnabled { get; }

        // ---- 图形 ----
        public Setting<int> Quality { get; }
        public Setting<int> RenderScale { get; }
        public Setting<int> AntiAliasing { get; }
        public Setting<int> ShadowQuality { get; }

        // ---- 显示 ----
        public Setting<int> DisplayMode { get; }
        public Setting<string> Resolution { get; }
        public Setting<int> FrameRate { get; }
        public Setting<int> VSync { get; }

        // ---- 控制 ----
        public Setting<float> LookSensitivity { get; }
        public Setting<float> AdsSensitivity { get; }
        public Setting<bool> InvertLookY { get; }
        /// <summary>输入绑定覆盖（`InputActionAsset.SaveBindingOverridesAsJson` 原样串；空＝全默认）。P2 消费者。</summary>
        public Setting<string> Bindings { get; }

        // ---- 常规 ----
        public Setting<string> Language { get; }
        public Setting<int> LastTab { get; }

        /// <summary>全部设置项（"恢复默认"与清单遍历的单源；顺序即声明序）。</summary>
        public IReadOnlyList<ISetting> Settings { get; }

        /// <param name="isMobilePlatform">当前平台是否移动端——只影响"帧率上限"的默认值与可选档
        /// （由装配点用运行时平台查询传入，本类不做平台分支，R15）。</param>
        public GameSettings(SettingService svc, bool isMobilePlatform = false)
        {
            if (svc == null) throw new ArgumentNullException(nameof(svc));

            MasterVolume = new Setting<float>(svc, "volume", 0.8f, v => Math.Clamp(v, 0f, 1f));
            // 音效开关（批C-⑥ 增项）：关 = Effect 组静音（Ui/Voice/Bgm 不受影响——"音效"语义面）
            SoundEnabled = new Setting<bool>(svc, "sfx", true);

            Quality = new Setting<int>(svc, "quality", 2, v => Math.Clamp(v, 0, 3));
            // 覆盖项默认"跟随预设"（-1）：全跟随时不克隆管线资产，与无覆盖形态等价
            RenderScale = new Setting<int>(svc, "renderScale", GraphicsOptions.FollowPreset, GraphicsOptions.SnapRenderScale);
            AntiAliasing = new Setting<int>(svc, "aa", GraphicsOptions.FollowPreset, GraphicsOptions.SnapAntiAliasing);
            ShadowQuality = new Setting<int>(svc, "shadow", GraphicsOptions.FollowPreset, GraphicsOptions.SnapShadow);

            DisplayMode = new Setting<int>(svc, "displayMode", DisplayOptions.DefaultMode, DisplayOptions.SnapMode);
            Resolution = new Setting<string>(svc, "resolution", string.Empty, NormalizeResolution);
            FrameRate = new Setting<int>(svc, "frameRate", FrameRateOptions.DefaultFor(isMobilePlatform),
                v => FrameRateOptions.Snap(v, isMobilePlatform));
            VSync = new Setting<int>(svc, "vsync", DisplayOptions.VSyncFollowPreset, DisplayOptions.SnapVSync);

            // 视角手感是"机制基线 × 倍率"——倍率默认 1.0，机制常量仍是唯一数值源（不在此复制 0.6）
            LookSensitivity = new Setting<float>(svc, "lookSens", ControlOptions.DefaultLookSensitivity,
                ControlOptions.ClampLookSensitivity);
            AdsSensitivity = new Setting<float>(svc, "adsSens", ControlOptions.DefaultAdsSensitivity,
                ControlOptions.ClampAdsSensitivity);
            InvertLookY = new Setting<bool>(svc, "invertY", false);
            Bindings = new Setting<string>(svc, "bindings", string.Empty);

            Language = new Setting<string>(svc, "lang", "zh-CN");
            LastTab = new Setting<int>(svc, "settingsTab", 0, v => Math.Clamp(v, 0, SettingsTabCount - 1));

            Settings = new ISetting[]
            {
                MasterVolume, SoundEnabled,
                Quality, RenderScale, AntiAliasing, ShadowQuality,
                DisplayMode, Resolution, FrameRate, VSync,
                LookSensitivity, AdsSensitivity, InvertLookY, Bindings,
                Language, LastTab,
            };
        }

        /// <summary>全部设置项回默认（值写入 + 标脏；应用与落盘由调用方走常规路径）。</summary>
        public void ResetAll()
        {
            for (int i = 0; i < Settings.Count; i++) Settings[i].ResetToDefault();
        }

        /// <summary>分辨率存档串规范化（<c>"1920X1080"</c> → <c>"1920x1080"</c>；解析不了 = 未指定）。</summary>
        private static string NormalizeResolution(string value)
            => DisplayOptions.TryParseResolution(value, out int width, out int height)
                ? DisplayOptions.FormatResolution(width, height)
                : string.Empty;
    }
}
