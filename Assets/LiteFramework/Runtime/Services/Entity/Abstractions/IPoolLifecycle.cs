namespace LiteFramework
{
    /// <summary>GameObject 池化生命周期契约：取出后 / 归还前（实体、UI 件等场景物件实现）。
    /// 与 Core ObjectPool&lt;T&gt; 的 IPoolable 分层：那是纯 C# 实例的池回调，本接口是场景件回调。</summary>
    public interface IPoolLifecycle
    {
        void OnSpawn();      // 池取出/首次创建后
        void OnRecycle();    // 回收入池前（停粒子、退事件、断引用——谁挂谁清）
    }
}
