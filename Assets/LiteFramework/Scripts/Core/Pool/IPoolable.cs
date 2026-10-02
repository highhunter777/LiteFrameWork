namespace LiteFramework
{
    /// <summary>
    /// 可池化对象的**可选**自生命周期接口（与池的构造回调共存，职责分层）：
    /// - `OnSpawn/OnDespawn` = **对象自身的生命周期**：自清理、解除外部引用、逆序执行自身生命周期容器
    ///   （"谁挂谁清"的执行点）；reset 逻辑只有类型自己知道，放类型里才不散；
    /// - 池的构造回调（onGet/onRelease/onDestroy）= **池策略**：如 GameObject 的 SetActive/挪池根/挂 parent；
    /// - 带参初始化不走接口（接口无参）——Acquire 之后由调用方赋值。
    /// 不实现本接口完全合法（纯小对象只用回调即可）；纯数据对象的**强制**接口是 <see cref="IReference"/>
    /// （ReferencePool 层，与本接口层次不同、不冲突）。
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
