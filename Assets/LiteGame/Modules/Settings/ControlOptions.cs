using LiteFramework;
namespace LiteGame.Modules.Settings
{
    /// <summary>
    /// 控制设置的值域（视角手感），单源。
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
