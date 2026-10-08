namespace LiteSim.View
{
    /// <summary>
    /// 特效句柄（《VFX服务实施指导》§1 决策 2）：**单调递增、永不复用**——旧句柄绝不会命中新实例（防 ABA）。
    /// `default(VfxHandle)`（Id=0）= 无效句柄：<see cref="IVFXService.Play"/> 失败/被拒时返回它，<c>Stop</c> 静默 no-op。
    /// </summary>
    public readonly struct VfxHandle
    {
        public readonly int Id;

        public VfxHandle(int id)
        {
            Id = id;
        }

        /// <summary>有效 = Id != 0。</summary>
        public bool IsValid => Id != 0;
    }
}
