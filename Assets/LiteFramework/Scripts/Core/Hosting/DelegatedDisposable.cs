using System;

namespace LiteFramework
{
    /// <summary>
    /// 委托式可释放对象（C2 引入）：把一段清理动作包装成 <see cref="IDisposable"/>，
    /// 供 <see cref="ClientScope.Register{T}"/> 登记"退订/归还/计数回退"类无类型清理动作——
    /// Scope 的 LIFO 释放序因此覆盖事件退订这类最易漏的生命周期项。
    /// 重复 Dispose 幂等；null 委托在构造期拒绝。
    /// </summary>
    public sealed class DelegatedDisposable : IDisposable
    {
        private Action _onDispose;

        public DelegatedDisposable(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public void Dispose()
        {
            Action action = _onDispose;
            if (action == null) return;
            _onDispose = null;              // 幂等：只执行一次
            action();
        }
    }
}
