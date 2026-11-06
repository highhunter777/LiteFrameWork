namespace LiteFramework.UI
{
    /// <summary>层级策略：组内 sortingOrder 分配规则。默认 = 组基序 + 递增槽位。</summary>
    public interface ILayerStrategy
    {
        int ResolveSortingOrder(UILayerGroup group, int slot);
    }
}
