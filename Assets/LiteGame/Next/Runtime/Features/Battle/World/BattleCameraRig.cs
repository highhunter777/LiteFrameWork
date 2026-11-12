using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>第三人称视角状态与相机位姿：yaw/pitch 驱动臂长俯仰弧，输出相机位置与朝向。</summary>
    public sealed class BattleCameraRig
    {
        private const float MinPitchDegrees = -35f;
        private const float MaxPitchDegrees = 65f;
        private const float ArmLength = 6.5f;
        private const float PivotHeight = 1.5f;

        public float YawDegrees { get; private set; }

        public float PitchDegrees { get; private set; } = 18f;

        /// <summary>按鼠标增量推进视角（度）；pitch 夹在上下限内。</summary>
        public void Look(float deltaX, float deltaY)
        {
            YawDegrees = Mathf.Repeat(YawDegrees + deltaX, 360f);
            PitchDegrees = Mathf.Clamp(PitchDegrees - deltaY, MinPitchDegrees, MaxPitchDegrees);
        }

        /// <summary>把相机摆到目标身后上方并朝向视线方向；枢轴取角色头部高度。</summary>
        public void Apply(Camera camera, Vector3 targetPosition)
        {
            if (camera == null) return;
            Vector3 pivot = targetPosition + Vector3.up * PivotHeight;
            Quaternion yawRotation = Quaternion.Euler(0f, YawDegrees, 0f);
            Quaternion pitchRotation = Quaternion.Euler(PitchDegrees, 0f, 0f);
            Vector3 armDirection = yawRotation * pitchRotation * Vector3.back;
            camera.transform.position = pivot + armDirection * ArmLength;
            camera.transform.rotation = Quaternion.LookRotation(pivot - camera.transform.position, Vector3.up);
        }

        /// <summary>屏幕中心射线的瞄准点：先打物理体，未命中取远处（不是自身脚下）。</summary>
        public static Vector3 ResolveAimPoint(Camera camera, float fallbackDistance = 40f)
        {
            if (camera == null) return Vector3.zero;
            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return ray.GetPoint(fallbackDistance);
        }
    }
}
