using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// TPS 相机视角数学（纯函数）：视角状态 → Cinemachine POV 轴角、恒定臂长俯仰弧、视线方向。
    /// **符号单源**：yaw 正 = 向右（世界 Y 轴正向）；pitch 正 = 上仰；POV 垂直轴正 = 向下，
    /// 故垂直轴取负。视图侧纯计算，不读场景、不写相机。
    /// </summary>
    public static class TpsCameraMath
    {
        /// <summary>yaw（弧度）→ POV 水平轴（度，归一化到 [-180,180)）。</summary>
        public static float PovHorizontalDegrees(float yawRad)
            => NormalizeDegrees(yawRad * Mathf.Rad2Deg);

        /// <summary>pitch（弧度，正 = 上仰）→ POV 垂直轴（度，正 = 向下）。</summary>
        public static float PovVerticalDegrees(float pitchRad)
            => -pitchRad * Mathf.Rad2Deg;

        /// <summary>
        /// 恒定臂长俯仰弧：把相对枢轴的基准臂 (armY, armZ) 绕角色右轴旋转 <c>-pitch × follow</c>，
        /// 返回旋转后的 (y, z)。**臂长守恒**——相机与枢轴的距离不随俯仰变化。
        /// <paramref name="follow"/> = 0 时相机完全不跟俯仰（角色随视线漂向画面边缘）；
        /// 1 时完全跟随（角色被锁在同一屏幕位置）。中间值给 PUBG 量级的“角色漂移但相机不跑远”。
        /// </summary>
        public static Vector2 PitchArm(float pitchRad, float follow, float armY, float armZ)
        {
            float arc = -pitchRad * follow;
            float cos = Mathf.Cos(arc);
            float sin = Mathf.Sin(arc);
            return new Vector2(
                armY * cos - armZ * sin,   // y
                armY * sin + armZ * cos);  // z
        }

        /// <summary>视线方向（轨道坐标系：x 右、y 上、z 前）：pitch 正 = 上仰。</summary>
        public static Vector3 ViewDirection(float pitchRad)
            => new Vector3(0f, Mathf.Sin(pitchRad), Mathf.Cos(pitchRad));

        /// <summary>角度归一化到 [-180,180)。</summary>
        public static float NormalizeDegrees(float degrees)
            => Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }
}
