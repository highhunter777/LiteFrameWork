namespace LiteFramework.UI
{
    /// <summary>
    /// HUD 驱动统一拍点：只描述“每渲染帧推进”，不含任何 HUD 数据；
    /// 各 HUD 族的驱动实现本接口，装配器按列表顺序派发。
    /// </summary>
    public interface IHudDriver
    {
        /// <summary>每渲染帧推进（同帧顺序由装配顺序决定）。</summary>
        void Tick(float deltaSeconds);
    }
}
