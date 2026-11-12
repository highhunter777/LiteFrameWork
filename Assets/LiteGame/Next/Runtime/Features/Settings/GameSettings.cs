using System;
using System.Collections.Generic;
using System.Globalization;
using LiteFrameworkNext;
using UnityEngine;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置项统一接口：遍历与"恢复默认"只需键与默认值，具体值经 <see cref="Setting{T}"/> 读写。</summary>
    public interface ISetting
    {
        string Key { get; }

        object DefaultValue { get; }

        /// <summary>从存档重读本项（解析失败/越界按默认与钳制归一）。</summary>
        void ReloadFromStore();

        void ResetToDefault();
    }

    /// <summary>单个设置项：键/默认值/钳制/变更回调；构造时读回存档，变更即写存档。</summary>
    public sealed class Setting<T> : ISetting
    {
        private readonly SettingsStore _store;
        private readonly Func<T, T> _clamp;
        private readonly Action<T> _apply;
        private readonly Func<string, T> _parse;
        private readonly Func<T, string> _serialize;

        public Setting(SettingsStore store, string key, T defaultValue, Func<T, T> clamp = null, Action<T> apply = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Default = defaultValue;
            _store = store;
            _clamp = clamp;
            _apply = apply;
            _parse = ParserOf<T>();
            _serialize = SerializerOf<T>();
            Value = Normalize(store == null ? defaultValue : ReadStored(store, key, defaultValue));
        }

        public string Key { get; }

        public T Default { get; }

        public T Value { get; private set; }

        public object DefaultValue => Default;

        public event Action<T> Changed;

        public void Set(T value)
        {
            T normalized = Normalize(value);
            if (Equals(Value, normalized)) return;
            Value = normalized;
            _store?.Set(Key, _serialize(normalized));
            _apply?.Invoke(normalized);
            Changed?.Invoke(normalized);
        }

        public void ResetToDefault() => Set(Default);

        public void ReloadFromStore()
        {
            if (_store == null) return;
            Set(ReadStored(_store, Key, Default));
        }

        private T Normalize(T value) => _clamp == null ? value : _clamp(value);

        private T ReadStored(SettingsStore store, string key, T fallback)
        {
            string raw = store.Get(key);
            if (raw == null) return fallback;
            try
            {
                return _parse(raw);
            }
            catch (Exception ex)
            {
                Log.Warning("设置 " + key + " 的值 \"" + raw + "\" 解析失败，按默认值 " + fallback + "：" + ex.Message);
                return fallback;
            }
        }

        // 静态转换器按运行时类型分发一次（此后委托缓存，无反射）；静态方法的类型参数另命名避免 CS0693。
        private static Func<string, TVal> ParserOf<TVal>()
        {
            if (typeof(TVal) == typeof(float))
                return s => (TVal)(object)float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(int))
                return s => (TVal)(object)int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(bool))
                return s => (TVal)(object)bool.Parse(s);
            if (typeof(TVal) == typeof(string))
                return s => (TVal)(object)s;
            throw new NotSupportedException("Setting<T> 只支持 float/int/bool/string，收到 " + typeof(TVal).Name);
        }

        // 序列化同 ParserOf：InvariantCulture + R 往返无损（0.1f 不缩水）。
        private static Func<TVal, string> SerializerOf<TVal>()
        {
            if (typeof(TVal) == typeof(float))
                return v => ((float)(object)v).ToString("R", CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(int))
                return v => ((int)(object)v).ToString(CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(bool))
                return v => ((bool)(object)v).ToString();
            if (typeof(TVal) == typeof(string))
                return v => (string)(object)v;
            throw new NotSupportedException("Setting<T> 只支持 float/int/bool/string，收到 " + typeof(TVal).Name);
        }
    }

    /// <summary>设置定义单源：16 项（音频 2/图形 4/显示 4/控制 4/常规 2），键/默认值/钳制对齐旧客户端
    /// `Assets/LiteGame/Modules/Settings/Model/GameSettings.cs`；持久化经 <see cref="SettingsStore"/>。</summary>
    public sealed class GameSettings
    {
        /// <summary>设置页面签数（与设置页 TabGroup 一致；`settingsTab` 的钳制上界）。</summary>
        public const int SettingsTabCount = 5;

        private const float DefaultVolume = 0.8f;
        private const int DefaultQuality = 2;
        private const string DefaultLanguage = "zh-CN";

        private readonly SettingsStore _store;

        /// <param name="store">存档（可空＝不持久化，供单测/纯值域使用）；读回依赖调用方先 <c>store.Load()</c>。</param>
        /// <param name="isMobilePlatform">移动端平台传入 true——只影响帧率档默认值；类内不做平台分支（R15）。</param>
        /// <param name="appliers">生效器（可空＝按 <paramref name="isMobilePlatform"/> 自建）。</param>
        public GameSettings(SettingsStore store = null, bool isMobilePlatform = false, SettingsAppliers appliers = null)
        {
            _store = store;
            Appliers = appliers ?? new SettingsAppliers(isMobilePlatform);

            MasterVolume = new Setting<float>(store, "volume", DefaultVolume, Mathf.Clamp01,
                _ => Appliers.ApplyAudio(this));
            SoundEnabled = new Setting<bool>(store, "sfx", true, null,
                _ => Appliers.ApplyAudio(this));

            Quality = new Setting<int>(store, "quality", DefaultQuality, value => Mathf.Clamp(value, 0, 3),
                _ => Appliers.ApplyGraphics(this));
            RenderScale = new Setting<int>(store, "renderScale", GraphicsOptions.FollowPreset,
                GraphicsOptions.SnapRenderScale, _ => Appliers.ApplyGraphics(this));
            AntiAliasing = new Setting<int>(store, "aa", GraphicsOptions.FollowPreset,
                GraphicsOptions.SnapAntiAliasing, _ => Appliers.ApplyGraphics(this));
            ShadowQuality = new Setting<int>(store, "shadow", GraphicsOptions.FollowPreset,
                GraphicsOptions.SnapShadow, _ => Appliers.ApplyGraphics(this));

            DisplayMode = new Setting<int>(store, "displayMode", DisplayOptions.DefaultMode,
                DisplayOptions.SnapMode, _ => Appliers.ApplyDisplay(this));
            Resolution = new Setting<string>(store, "resolution", string.Empty, NormalizeResolution,
                _ => Appliers.ApplyDisplay(this));
            FrameRate = new Setting<int>(store, "frameRate", FrameRateOptions.DefaultFor(isMobilePlatform),
                value => FrameRateOptions.Snap(value, isMobilePlatform), _ => Appliers.ApplyDisplay(this));
            VSync = new Setting<int>(store, "vsync", DisplayOptions.VSyncFollowPreset,
                DisplayOptions.SnapVSync, _ => Appliers.ApplyDisplay(this));

            LookSensitivity = new Setting<float>(store, "lookSens", ControlOptions.DefaultLookSensitivity,
                ControlOptions.ClampLookSensitivity);
            AdsSensitivity = new Setting<float>(store, "adsSens", ControlOptions.DefaultAdsSensitivity,
                ControlOptions.ClampAdsSensitivity);
            InvertLookY = new Setting<bool>(store, "invertY", false);
            Bindings = new Setting<string>(store, "bindings", string.Empty);

            Language = new Setting<string>(store, "lang", DefaultLanguage);
            LastTab = new Setting<int>(store, "settingsTab", 0,
                value => Mathf.Clamp(value, 0, SettingsTabCount - 1));

            All = new ISetting[]
            {
                MasterVolume, SoundEnabled,
                Quality, RenderScale, AntiAliasing, ShadowQuality,
                DisplayMode, Resolution, FrameRate, VSync,
                LookSensitivity, AdsSensitivity, InvertLookY, Bindings,
                Language, LastTab,
            };
        }

        // ---- 音频 ----。主音量走 AudioListener；音效开关记录在生效器（音频服务接入后消费）。
        public Setting<float> MasterVolume { get; }
        public Setting<bool> SoundEnabled { get; }

        // ---- 图形 ----。三个覆盖项默认 FollowPreset(-1)＝跟随画质预设，不做覆盖。
        public Setting<int> Quality { get; }
        public Setting<int> RenderScale { get; }
        public Setting<int> AntiAliasing { get; }
        public Setting<int> ShadowQuality { get; }

        // ---- 显示 ----。
        public Setting<int> DisplayMode { get; }
        public Setting<string> Resolution { get; }
        public Setting<int> FrameRate { get; }
        public Setting<int> VSync { get; }

        // ---- 控制 ----。手感/绑定只存值（由输入与相机将来消费，不在设置层改相机）。
        public Setting<float> LookSensitivity { get; }
        public Setting<float> AdsSensitivity { get; }
        public Setting<bool> InvertLookY { get; }
        /// <summary>输入绑定覆盖 JSON 原样串（空＝全默认），由输入层将来消费。</summary>
        public Setting<string> Bindings { get; }

        // ---- 常规 ----。
        public Setting<string> Language { get; }
        public Setting<int> LastTab { get; }

        /// <summary>全部设置项（"恢复默认"与清单遍历的单源；顺序即声明序）。</summary>
        public IReadOnlyList<ISetting> All { get; }

        /// <summary>设置生效器（音频/图形/显示三项落到引擎 API）。</summary>
        public SettingsAppliers Appliers { get; }

        /// <summary>音效开关当前值（音频服务接入前的只读查询点，无引擎侧落点）。</summary>
        public bool SfxEnabled => SoundEnabled.Value;

        /// <summary>从存档重读全部项（先 store.Load()，越界值按钳制归一）；生效由调用方 ApplyAll。</summary>
        public void Load()
        {
            _store?.Load();
            for (int i = 0; i < All.Count; i++) All[i].ReloadFromStore();
        }

        /// <summary>把当前值全部生效到引擎（启动/重载存档后调用）。</summary>
        public void ApplyAll() => Appliers.ApplyAll(this);

        /// <summary>写回存档（有未存改动才写），关闭链调用。</summary>
        public void Save() => SaveIfDirty();

        public void SaveIfDirty() => _store?.SaveIfDirty();

        /// <summary>全部项回默认（写值 + 标脏 + 即时生效）。</summary>
        public void ResetAll()
        {
            for (int i = 0; i < All.Count; i++) All[i].ResetToDefault();
        }

        /// <summary>旧名，等同 <see cref="ResetAll"/>（设置页现有调用点保留）。</summary>
        public void ResetDefaults() => ResetAll();

        /// <summary>分辨率存档串规范化（<c>"1920X1080"</c> → <c>"1920x1080"</c>；解析不了＝未指定）。</summary>
        private static string NormalizeResolution(string value)
            => DisplayOptions.TryParseResolution(value, out int width, out int height)
                ? DisplayOptions.FormatResolution(width, height)
                : string.Empty;
    }
}
