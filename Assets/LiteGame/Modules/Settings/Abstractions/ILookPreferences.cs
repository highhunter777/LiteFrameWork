namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 视角手感的**玩家偏好端口**（只读实例；设置层写入、表现层消费）。
    ///
    /// 为什么是倍率而不是绝对值：机制基线（0.0025 rad/px、ADS 收敛 0.6）是
    /// <see cref="BattleLookController"/> 的常量单源；产品设置只在其上乘倍率，
    /// 避免把同一个数值在"机制常量"和"设置默认值"里各写一份（改一处即分叉）。
    ///
    /// 为什么是端口而不是静态读口：R13/红线 6——设置值经实例随装配链下传，
    /// 表现件不读任何 public static 可变状态。
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
