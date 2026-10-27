using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteClient.UI
{
    /// <summary>
    /// 绑定索引的**本地化域**：语言服务注入/退订、控件名 → key 的绑定表、语言变更通知与复数解析。
    /// 只持有本地化状态，不反向依赖 <see cref="UIBindIndex"/>——索引在 <see cref="Changed"/> 里
    /// 用自己的受控写口重刷文本（所有权断言仍归索引）。
    /// </summary>
    internal sealed class UIBindLocale
    {
        private readonly Dictionary<string, LTextBinding> _bindings = new Dictionary<string, LTextBinding>(8);
        private ILocalizationService _locale;

        /// <summary>语言切换（服务回调后触发；索引据此重刷已绑定项与列表行）。</summary>
        public event Action Changed;

        public bool HasLocale => _locale != null;

        /// <summary>
        /// 绑定语言服务（装配点注入）。切换语言时经 <see cref="Changed"/> 刷新全部已绑定项。
        /// 重复注入会先退订旧的——避免同实例多份订阅导致重复刷新。
        /// </summary>
        public void Bind(ILocalizationService localization)
        {
            if (ReferenceEquals(_locale, localization)) return;
            Detach();
            _locale = localization;
            if (_locale != null) _locale.OnLocaleChanged += OnLocaleChanged;
        }

        public void Detach()
        {
            if (_locale != null) _locale.OnLocaleChanged -= OnLocaleChanged;
            _locale = null;
        }

        /// <summary>未注入语言服务时抛——静默显示原始 key 会让“忘了注入”变成线上全是 key 的隐蔽故障。</summary>
        public void RequireBound(string name, string method)
        {
            if (_locale == null)
                throw new InvalidOperationException($"绑定索引[{name}] {method} 需先 BindLocale（未注入语言服务）");
        }

        public string Set(string name, string key, params object[] args)
        {
            var binding = new LTextBinding { Key = key, Args = args, IsPlural = false };
            _bindings[name] = binding;
            return Resolve(binding);
        }

        public string SetPlural(string name, string key, long count, params object[] args)
        {
            var binding = new LTextBinding { Key = key, Args = args, Plural = count, IsPlural = true };
            _bindings[name] = binding;
            return Resolve(binding);
        }

        public bool Unbind(string name) => _bindings.Remove(name);

        public void Clear() => _bindings.Clear();

        /// <summary>按已登记 key 重写文本（索引传入自己的受控写口；不触碰未绑定 key 的控件）。</summary>
        public void RewriteAll(Action<string, string> write)
        {
            foreach (KeyValuePair<string, LTextBinding> kv in _bindings)
                write(kv.Key, Resolve(kv.Value));
        }

        /// <summary>列表行文案：已注入则按 key 解出，否则给可诊断占位（SetList 已拦未注入态）。</summary>
        public string FormatOrFallback(string key, object[] args)
            => _locale != null ? _locale.Format(key, args) : "[" + key + "]";

        private string Resolve(LTextBinding b)
            => b.IsPlural ? _locale.FormatPlural(b.Key, b.Plural, b.Args) : _locale.Format(b.Key, b.Args);

        private void OnLocaleChanged(string _) => Changed?.Invoke();

        private sealed class LTextBinding
        {
            public string Key;
            public object[] Args;
            public long Plural;
            public bool IsPlural;
        }
    }
}
