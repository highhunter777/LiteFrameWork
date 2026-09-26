using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>红点规则注册口（M4 §2.5）：key → 求值委托的注册表（覆盖式注册）。
    /// 求值经 SafeCall（规则抛 = 按无红点处理，不炸消费方）；缓存 / 树传播 = M4c 完整红点控件。</summary>
    public sealed class RedDotRegistry
    {
        private readonly Dictionary<string, Func<bool>> _rules = new Dictionary<string, Func<bool>>(16);

        public void Register(string key, Func<bool> evaluate)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("红点 key 不得为空", nameof(key));
            _rules[key] = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        }

        /// <summary>求值：未注册 key = 无红点（false）；规则抛 = 无红点 + 日志（SafeCall 隔离）。</summary>
        public bool Evaluate(string key)
            => _rules.TryGetValue(key, out var rule) && SafeCall.Invoke(rule, $"RedDot[{key}]", false);

        public void Clear() => _rules.Clear();

        public int Count => _rules.Count;
    }
}
