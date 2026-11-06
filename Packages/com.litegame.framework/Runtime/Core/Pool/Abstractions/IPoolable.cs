namespace LiteFramework
{
    /// <summary>
    /// 可池化对象的**可选**自生命周期接口（与池的构造回调共存，职责分层）。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>出池后调用（池策略 onGet 之后）。禁止抛异常（抛则对象被销毁、账目回滚、异常传播）。</summary>
        void OnSpawn();

        /// <summary>归还时调用（池策略 onRelease **之前**——对象先清自己的挂载，池再挪动它）。
        /// 默认职责：逆序执行自身生命周期容器并清空（容器非空 = 有挂载方没注销）。</summary>
        void OnDespawn();
    }
}
