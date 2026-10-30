namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// 相机混合默认值（相机域常量单源）：瞄准接管的混合宽度与 Cinemachine Brain 的 DefaultBlend
    /// （EaseInOut 0.3s）对齐——短按也走完一次完整混合，点按不重启混合。
    ///
    /// 为什么住相机域而不是战斗/输入：这是**相机混合语义**的宽度，不是玩家瞄准事实；
    /// 战斗侧的滞回门（<see cref="CameraAimBlendGate"/>）只消费它。
    /// </summary>
    public static class CameraBlendDefaults
    {
        /// <summary>瞄准接管的最小混合宽度（秒）。</summary>
        public const float AimBlendSeconds = 0.3f;
    }
}
