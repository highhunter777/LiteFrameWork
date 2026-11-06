namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// 相机混合默认值（相机域常量单源）：瞄准接管的混合宽度与 Cinemachine Brain 的 DefaultBlend （EaseInOut 0.3s）对齐——短按也走完一次完整混合，点按不重启混合。
    /// </summary>
    public static class CameraBlendDefaults
    {
        /// <summary>瞄准接管的最小混合宽度（秒）。</summary>
        public const float AimBlendSeconds = 0.3f;
    }
}
