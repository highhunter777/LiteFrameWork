using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 俯视角战斗相机（《联机战斗演示专项设计》§3"相机跟本地预测位置；和解期间跟衰减后的表现位置"；
    /// 《M11实施指导》§2.4 C3"相机模式档位（俯角/距离/FOV/夹取）来自策略包参数面——相机不认识'模式'"）。
    ///
    /// 纪律：相机**只读表现位置**（SimView 的输出），不认识 Sim、不认识"游戏模式"——档位差异全部
    /// 由 <see cref="Rig"/> 参数表达。跟随时对目标做指数平滑（复用
    /// <see cref="ViewTransformMath.Decay"/>，帧率无关），避免逐帧硬跟带来的抖动。
    /// </summary>
    public sealed class BattleCameraRig
    {
        /// <summary>档位参数（俯角/距离/FOV/跟随平滑）。策略包选择整组，相机不解释含义。</summary>
        public struct Rig
        {
            /// <summary>俯角（度，向下为负）。俯视角默认 -55°。</summary>
            public float PitchDegrees;
            /// <summary>相机到焦点的距离。</summary>
            public float Distance;
            /// <summary>视场角。</summary>
            public float FieldOfView;
            /// <summary>跟随平滑（越大跟得越紧；≤0 = 硬跟）。</summary>
            public float FollowSharpness;
            /// <summary>焦点抬升（把镜头中心从脚下抬到角色胸口附近）。</summary>
            public float FocusHeight;

            public static Rig Default => new Rig
            {
                PitchDegrees = -55f,
                Distance = 18f,
                FieldOfView = 55f,
                FollowSharpness = 12f,
                FocusHeight = 1.5f,
            };
        }

        private readonly Transform _camera;
        private readonly Rig _rig;
        private Vector3 _focused;

        /// <summary>当前焦点（表现空间，SimView 输出的平滑位置）。</summary>
        public Vector3 Focus => _focused;

        /// <summary>是否已收到过焦点（首次直接落位，不做平滑——避免开局从原点飞过去）。</summary>
        public bool HasFocus { get; private set; }

        public BattleCameraRig(Transform camera, Rig rig)
        {
            _camera = camera != null ? camera : throw new System.ArgumentNullException(nameof(camera));
            _rig = rig;
        }

        /// <summary>设置朝向（解除控制时归位看向焦点）。</summary>
        public void Reset() => HasFocus = false;

        /// <summary>
        /// 每渲染帧跟随（<paramref name="target"/> = 本地玩家**表现位置**：预测位置，和解期间是衰减后的位置）。
        /// 只写相机 Transform，不写 Sim。
        /// </summary>
        public void Tick(in Vector3 target, float deltaSeconds)
        {
            var focus = new Vector3(target.x, target.y + _rig.FocusHeight, target.z);

            if (!HasFocus)
            {
                _focused = focus;                     // 首帧直接落位
                HasFocus = true;
            }
            else if (_rig.FollowSharpness <= 0f)
            {
                _focused = focus;                     // 硬跟
            }
            else
            {
                // 指数平滑（帧率无关）：半衰期只与真实经过时间相关
                float alpha = 1f - Mathf.Exp(-_rig.FollowSharpness * deltaSeconds);
                _focused = Vector3.Lerp(_focused, focus, alpha);
            }

            float pitch = _rig.PitchDegrees * Mathf.Deg2Rad;
            var offset = new Vector3(
                0f,
                -Mathf.Sin(pitch) * _rig.Distance,
                -Mathf.Cos(pitch) * _rig.Distance);

            _camera.position = _focused + offset;
            _camera.rotation = Quaternion.LookRotation(_focused - _camera.position, Vector3.up);
        }

        /// <summary>应用档位的 FOV（容器/策略切换时调；不每帧写以免覆盖外部调整）。</summary>
        public void ApplyLens(Camera camera)
        {
            if (camera != null) camera.fieldOfView = _rig.FieldOfView;
        }
    }
}
