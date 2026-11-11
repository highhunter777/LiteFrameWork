using System;
using UnityEngine;

namespace LiteGameNext.Features.Settings
{
    /// <summary>单个设置项：键/默认值/钳制/变更回调；值只在 Set 内规整一次。</summary>
    public sealed class Setting<T>
    {
        private readonly Func<T, T> _clamp;
        private readonly Action<T> _apply;

        public Setting(string key, T defaultValue, Func<T, T> clamp, Action<T> apply)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Default = defaultValue;
            Value = defaultValue;
            _clamp = clamp;
            _apply = apply;
        }

        public string Key { get; }

        public T Default { get; }

        public T Value { get; private set; }

        public event Action<T> Changed;

        public void Set(T value)
        {
            T normalized = _clamp == null ? value : _clamp(value);
            if (Equals(Value, normalized)) return;
            Value = normalized;
            _apply?.Invoke(normalized);
            Changed?.Invoke(normalized);
        }

        public void Reset() => Set(Default);
    }

    /// <summary>设置定义单源：音频/画质；页面只绑定，不复制默认值/值域。</summary>
    public sealed class GameSettings
    {
        public GameSettings()
        {
            MasterVolume = new Setting<float>("volume", 0.8f,
                value => Mathf.Clamp01(value), value => AudioListener.volume = value);
            Quality = new Setting<int>("quality", 2,
                value => Mathf.Clamp(value, 0, 2), value => QualitySettings.SetQualityLevel(value, true));
        }

        public Setting<float> MasterVolume { get; }

        public Setting<int> Quality { get; }

        /// <summary>恢复默认并即时生效。</summary>
        public void ResetDefaults()
        {
            MasterVolume.Reset();
            Quality.Reset();
        }
    }
}
