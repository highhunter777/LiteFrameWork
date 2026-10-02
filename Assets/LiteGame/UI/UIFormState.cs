namespace LiteGame
{
    /// <summary>
    /// 界面七态（GF UIManager 同款）：Loading→OnInit→Active⇄Covered/Paused→Closing→Recycled。
    /// 迁移守卫在 <see cref="UIForm"/>（非法迁移当场抛——fail-fast 精神，§3.4）。
    /// </summary>
    public enum UIFormState
    {
        Loading,    // prefab 异步实例化中（OnInit 在此态内触发，一次性）
        Active,     // 显示且参与驱动（OnUpdate 仅此态）
        Covered,    // 被更高层级组的全屏界面遮盖（批量语义；OnCover/OnReveal 配对）
        Paused,     // 手动暂停（OnPause；恢复走 OnShow）
        Closing,    // 关闭中（OnHide 已调；§2.2 转场策略可在此等待动画）
        Recycled,   // 已回收进池（inactive，可复用；复用不重跑 OnInit）
        Disposed,   // 已销毁（缓存淘汰/显式 Destroy/Shutdown——终态，不可复用）
    }
}
