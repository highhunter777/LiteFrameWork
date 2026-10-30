using LiteFramework;
namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 控制设置的值域（视角手感），单源。
    ///
    /// **为什么是倍率而不是绝对值**：机制基线（0.0025 rad/px、ADS 收敛 0.6）是
    /// `BattleLookController` 的常量单源；产品设置只在其上乘一个玩家可调倍率——
    /// 若把 0.6 复制成设置默认值，改机制常量时两处必然分叉（"必须与 X 一致"只靠注释即违规）。
    /// </summary>
    public static class ControlOptions
    {
        public const float MinLookSensitivity = 0.2f;
        public const float MaxLookSensitivity = 3f;
        public const float DefaultLookSensitivity = 1f;

        public const float MinAdsSensitivity = 0.5f;
        public const float MaxAdsSensitivity = 1.5f;
        public const float DefaultAdsSensitivity = 1f;

        public static float ClampLookSensitivity(float value)
            => float.IsNaN(value) ? DefaultLookSensitivity : System.Math.Clamp(value, MinLookSensitivity, MaxLookSensitivity);

        public static float ClampAdsSensitivity(float value)
            => float.IsNaN(value) ? DefaultAdsSensitivity : System.Math.Clamp(value, MinAdsSensitivity, MaxAdsSensitivity);
    }
}
