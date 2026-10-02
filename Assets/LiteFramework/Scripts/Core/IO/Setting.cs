using System;
using System.Globalization;

namespace LiteFramework
{
    /// <summary>
    /// 单个设置项的强类型值对象（**机制件**）：key/默认值/范围校验**单源**（调用处不散落硬编码默认值与裸字符串键）。
    /// Get：缺失 → 默认值（首启正常）；解析失败 → 默认值 + Log.Warning（事故可见）。
    /// Set：先经 clamp（可选范围校验）再写入——越界值在写入口死掉。
    /// 只支持 float/int/bool/string（SettingService 的四种口径）；其他 T 在构造时抛。
    /// 具体设置清单（键/默认值/范围）由业务定义——见 LiteClient.GameSettings。
    /// </summary>
    public sealed class Setting<T>
    {
        private readonly SettingService _svc;
        private readonly string _key;
        private readonly T _default;
        private readonly Func<T, T> _clamp;
        private readonly Func<string, T> _deser;
        private readonly Func<T, string> _ser;

        public Setting(SettingService svc, string key, T defaultValue, Func<T, T> clamp = null)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _default = defaultValue;
            _clamp = clamp;
            _deser = GetDeser<T>();
            _ser = GetSer<T>();
        }

        public T Get()
        {
            var raw = _svc.GetString(_key);
            if (raw == null) return _default;                     // 首启:正常状态,静默
            try
            {
                var v = _deser(raw);
                return _clamp != null ? _clamp(v) : v;
            }
            catch (Exception ex)
            {
                Log.Warning($"设置 \"{_key}\" 值 \"{raw}\" 解析失败,按默认值 {_default}({ex.Message})", "Setting");
                return _default;
            }
        }

        public void Set(T value)
        {
            if (_clamp != null) value = _clamp(value);
            _svc.SetString(_key, _ser(value));
        }

        // 静态转换器:按运行时类型分发(外层 T 由构造时 typeof(T) 决定一次,此后委托缓存,无反射)。
        // 静态方法的类型参数是 TVal 不是外层 T——静态方法不共享外层类型参数(CS0693)。
        private static Func<string, T> GetDeser<TVal>()
        {
            if (typeof(TVal) == typeof(float))
                return s => (T)(object)float.Parse(s, CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(int))
                return s => (T)(object)int.Parse(s, CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(bool))
                return s => (T)(object)bool.Parse(s);
            if (typeof(TVal) == typeof(string))
                return s => (T)(object)s;
            throw new NotSupportedException($"Setting<T> 只支持 float/int/bool/string,收到 {typeof(TVal).Name}");
        }

        private static Func<T, string> GetSer<TVal>()
        {
            if (typeof(TVal) == typeof(float))
                return v => ((float)(object)v).ToString("R", CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(int))
                return v => ((int)(object)v).ToString(CultureInfo.InvariantCulture);
            if (typeof(TVal) == typeof(bool))
                return v => ((bool)(object)v).ToString();
            return v => (string)(object)v;
        }
    }
}
