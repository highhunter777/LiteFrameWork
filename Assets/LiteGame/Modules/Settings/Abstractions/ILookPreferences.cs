namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 视角手感的**玩家偏好端口**（只读实例）。
    /// </summary>
    public interface ILookPreferences
    {
        /// <summary>视角灵敏度倍率（默认 1.0，值域见 <see cref="LiteFramework.ControlOptions"/>）。</summary>
        float SensitivityMultiplier { get; }

        /// <summary>开镜灵敏度倍率（默认 1.0；机制 ADS 收敛比之上再乘本值）。</summary>
        float AdsScaleMultiplier { get; }

        /// <summary>垂直视角反转（鼠标上推时镜头上仰 vs 下俯）。</summary>
        bool InvertY { get; }
    }
}
