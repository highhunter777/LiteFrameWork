using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// 开放继承：业务侧以空子类 + 标记接口对三注册表作类型化区分（容器按 Type 键控）；
    /// 子类不得重写行为（仅构造传 kind）。
    public class LuaRegistry<T> : ILuaRegistry<T>
    {
        private readonly string _kind;    // "UI" / "Content" / "Strategies"——报错信息的路径约定提示用
        private readonly Dictionary<string, T> _items = new(StringComparer.Ordinal);  // key 大小写敏感, culturally 稳定
        public int Generation { get; private set; }

        public LuaRegistry(string kind) => _kind = kind ?? throw new ArgumentNullException(nameof(kind));

        public void Fill(string name, T impl)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException($"注册名不得为空({_kind})", nameof(name));
            if (impl == null) throw new ArgumentNullException(nameof(impl));
            if (_items.ContainsKey(name))
                throw new InvalidOperationException(
                    $"Lua 注册表[{_kind}] 重复注册 \"{name}\"——表主键或 Lua 路径冲突(双向校验该抓住的另一侧)");
            _items.Add(name, impl);
            Generation++;                 // 增量热更逐项 Fill 时持续变化;消费方在安全检查点读一次即可
        }

        public T Get(string name)
        {
            if (name != null && _items.TryGetValue(name, out var v)) return v;
            throw new KeyNotFoundException(
                $"Lua 注册表[{_kind}] 未注册 \"{name}\"——按路径约定应为 {_kind}.<名称>;" +
                "检查表行与 Lua 模块两侧拼写(单向校验只抓漏配,双向才抓拼错)");
        }

        public bool Has(string name) => name != null && _items.ContainsKey(name);

        /// <summary>DevReload 重填前置：旧逻辑表全弃。Generation 前进一位作失效纪元——
        /// 重填即使逐项同名，消费方也能凭 Generation 变化感知到"实例已换"。幂等。</summary>
        public void Clear()
        {
            if (_items.Count > 0) Generation++;
            _items.Clear();
        }
    }
}
