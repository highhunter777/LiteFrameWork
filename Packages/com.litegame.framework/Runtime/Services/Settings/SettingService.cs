using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;


namespace LiteFramework
{
    /// <summary>
    /// 玩家偏好服务(音量/画质/语言:**存档不经过它**,存档结构业务自持走 FileSystem + TryReadJson 迁移)。
    /// </summary>
    public sealed class SettingService
    {
        private const string FilePath = "settings.json";
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(16);
        private readonly IFileStore _files;
        private bool _dirty;

        /// <summary>注入文件端口（必需；未注入即装配错误）。</summary>
        public SettingService(IFileStore files = null)
        {
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        /// <summary>文件 → 字典。可重调(重载语义:丢弃内存中未存修改)。</summary>
        public void Load()
        {
            _values.Clear();
            var data = _files.ReadJson<Dictionary<string, string>>(FilePath);
            if (data == null) return;                      // 首启:空白,静默
            foreach (var kv in data) _values[kv.Key] = kv.Value;
            _dirty = false;
        }

        /// <summary>写回(原子写)。失败(磁盘满等)→ Log.Error 不抛——宁丢偏好不炸调用方;脏标记保持,下次时机再试。</summary>
        public void Save()
        {
            try { _files.WriteJson(FilePath, _values); _dirty = false; }
            catch (Exception ex) { Log.Error(ex, null, LogTag.Setting); }
        }

        /// <summary>有未存修改才写。三处标准调用点见类注释③。</summary>
        public void SaveIfDirty()
        {
            if (_dirty) Save();
        }

        public bool Has(string key)
        {
            ValidateKey(key);
            return _values.ContainsKey(key);
        }

        public void Remove(string key)
        {
            ValidateKey(key);
            if (_values.Remove(key)) _dirty = true;
        }

        // ---- 字符串 ----。

        public string GetString(string key, string defaultValue = null)
        {
            ValidateKey(key);
            return _values.TryGetValue(key, out var v) ? v : defaultValue;
        }

        public void SetString(string key, string value)
        {
            ValidateKey(key);
            if (value == null) { Remove(key); return; }    // null = 移除(⑤)
            _values[key] = value;
            _dirty = true;
        }

        // ---- 值类型:Get = TryGetValue → 解析(失败 Warning + 默认);Set = Invariant 格式化 + 标脏 ----。

        /// <summary>类型化读取的统一入口（<see cref="Setting{T}"/> 与 GetInt/GetBool/GetFloat 共用）：
        /// 缺键静默默认；解析异常 → 默认 + Log.Warning（不再各写一份）。</summary>
        public T GetTyped<T>(string key, T defaultValue, Func<string, T> parse)
        {
            if (!TryGet(key, out var s)) return defaultValue;
            try { return parse(s); }
            catch (Exception ex) { return Bad(key, s, defaultValue, ex.Message); }
        }

        public int GetInt(string key, int defaultValue = 0)
            => GetTyped(key, defaultValue, s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

        public void SetInt(string key, int value)
            => SetRaw(key, value.ToString(CultureInfo.InvariantCulture));

        public bool GetBool(string key, bool defaultValue = false)
            => GetTyped(key, defaultValue, bool.Parse);

        public void SetBool(string key, bool value)
            => SetRaw(key, value ? "true" : "false");

        public float GetFloat(string key, float defaultValue = 0f)
            => GetTyped(key, defaultValue, s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));

        public void SetFloat(string key, float value)
            => SetRaw(key, value.ToString("R", CultureInfo.InvariantCulture));   // R = 往返无损,0.1f 不缩水

        // ---- 内部 ----。

        private bool TryGet(string key, out string s)
        {
            ValidateKey(key);
            return _values.TryGetValue(key, out s);
        }

        private void SetRaw(string key, string raw)
        {
            ValidateKey(key);
            _values[key] = raw;
            _dirty = true;
        }

        private T Bad<T>(string key, string raw, T defaultValue, string detail = null)
        {
            Log.Warning(detail == null
                ? $"键 \"{key}\" 值 \"{raw}\" 解析失败,按默认值 {defaultValue}"
                : $"键 \"{key}\" 值 \"{raw}\" 解析失败,按默认值 {defaultValue}({detail})", LogTag.Setting);
            return defaultValue;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
        }
    }
}
