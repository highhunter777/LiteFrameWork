using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// TPS 相机机位调参（场景单源）：挂在主 vcam 根上，由相机服务读取（瞄准机位置由主视线派生）。
    /// 只表达"恒定臂长俯仰弧"的跟随系数/枢轴高度/地面下限——视角/灵敏度/FOV/肩偏
    /// 仍在各自端口或 vcam 场景值，不在这里复制。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TpsCameraRigTuning : MonoBehaviour
    {
        /// <summary>俯仰跟随系数（0..1）：0 = 相机不跟俯仰（角色随视线漂向画面边缘）；
        /// 1 = 完全跟随（角色锁在同一屏幕位置）。中间值给 PUBG 量级的漂移。</summary>
        [SerializeField, Range(0f, 1f)] private float _pitchFollow = 0.4f;

        /// <summary>俯仰枢轴高度（米，角色根以上）：恒定臂长弧的旋转中心（胸/头附近）。</summary>
        [SerializeField] private float _pivotHeight = 1.6f;

        /// <summary>相机最低高度（米，角色根以上）：极端俯仰/调大 follow 时的地面下限，防穿地。</summary>
        [SerializeField] private float _minBoomHeight = 0.4f;

        /// <summary>俯仰跟随系数（0..1）。</summary>
        public float PitchFollow => _pitchFollow;

        /// <summary>俯仰枢轴高度（米，角色根以上）。</summary>
        public float PivotHeight => _pivotHeight;

        /// <summary>相机最低高度（米，角色根以上）。</summary>
        public float MinBoomHeight => _minBoomHeight;
    }
}
