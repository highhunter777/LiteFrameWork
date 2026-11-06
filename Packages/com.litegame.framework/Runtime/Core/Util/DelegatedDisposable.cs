using System;

namespace LiteFramework
{
    /// <summary>
    /// 委托式可释放对象：把一段清理动作包装成 <see cref="IDisposable"/>。
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
