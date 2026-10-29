using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 俯仰瞄准姿态（第三人称自由视角的**姿态覆写器**）：把上半身（人形 <see cref="HumanBodyBones.Chest"/>
    /// 骨）按相机 pitch 前俯/后仰，并把同一世界旋转镜像到**根级枪骨**——枪骨
    /// <c>root/add_weapon_r</c> 不是 Chest 的子级，只转胸不转枪会"手随胸走、枪留在原地"（俯仰错位）。
    /// aim up/down 素材批（Overlay 上半身混合）落地后退役为素材驱动。
    ///
    /// **归属**：视图表现编排，不属动画播放机制（同 <see cref="WeaponGripConstraint"/>——不碰后端/
    /// 通道仲裁/状态机）。约束对象与槽位同生共死（驱动器建槽时建、释放时弃）。
    ///
    /// **写时序（唯一写者契约）**：必须在驱动帧的**动画 Evaluate 之后**调用（播放器 Tick 之后）——
    /// 写前骨旋转即本帧动画值，前乘俯仰偏置；每帧写、退出即停写——不改持久状态，动画下一帧自然接管。
    ///
    /// **世界系前乘**：骨世界旋转前乘"绕角色右轴的俯仰旋转"——人形 Chest 骨局部轴随 Rig 而异，
    /// 角色右轴是唯一稳定参照（角色根由视图按 Sim yaw 摆位）。枪骨用同一旋转绕 Chest 枢轴做刚体
    /// 位移（保持动画自身的枪位/后坐相对姿态，不覆盖成固定持枪偏移）。
    ///
    /// **骨解析**：Chest 走 <see cref="Animator.GetBoneTransform"/>（人形映射——任意人形 Rig 零路径常量）；
    /// 枪骨走 <see cref="GripBake.WeaponPath"/>（与持枪约束同一烘焙常量）。缺 Chest → <see cref="IsValid"/>
    /// false 的显性失败面；缺枪骨只影响镜像（胸仍转）。
    /// </summary>
    public sealed class AimPitchPostureDriver
    {
        private readonly Animator _animator;
        private readonly Transform _chest;
        private readonly Transform _weapon;

        /// <summary>本约束是否可用（Chest 骨经人形映射解析到才可用；不可用时 Update 显性拒绝）。</summary>
        public bool IsValid => _chest != null;

        /// <summary>已实际覆写的帧数（诊断/测试读值：射击语境内应随帧增长，语境外恒 0）。</summary>
        public int AppliedFrames { get; private set; }

        public AimPitchPostureDriver(Animator animator)
        {
            _animator = animator;
            // 无 Avatar（灰盒替身/清单供片的测试视图）没有 humanoid 骨骼映射：
            // GetBoneTransform 会抛 "Avatar is null"——显性退化（IsValid=false）才是契约。
            if (animator == null || animator.avatar == null) return;
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _weapon = animator.transform.Find(GripBake.WeaponPath);
        }

        /// <summary>射击语境在场时按相机 pitch 覆写胸骨俯仰，并把同一世界旋转镜像到根级枪骨；
        /// 语境外本帧不写（动画值原样保留）。不可用 → 返回 false（调用方据此计入诊断）。</summary>
        public bool Update(bool aimingStance, float cameraPitchRad)
        {
            if (!IsValid) return false;
            if (!aimingStance) return true;

            // 角色右轴正角 = 前俯（+forward 向 −up 旋转）；相机上仰（pitch 正）= 胸腔后仰 ⇒ 取负。
            // 角度 = AimPitchPosture 映射（区间内恒等、越界钳制）。
            float angleDeg = -AimPitchPosture.UpperBodyPitch(cameraPitchRad) * Mathf.Rad2Deg;
            Quaternion pitch = Quaternion.AngleAxis(angleDeg, _animator.transform.right);
            Vector3 pivot = _chest.position;
            _chest.rotation = pitch * _chest.rotation;

            // 枪骨在 root 下（非 Chest 子级）：按 Chest 同一刚体旋转绕 Chest 枢轴补一次，
            // 手随胸转后枪仍在手里；枪自身的动画局部姿态（含后坐）原样保留。
            if (_weapon != null && !_weapon.IsChildOf(_chest))
            {
                _weapon.rotation = pitch * _weapon.rotation;
                _weapon.position = pivot + pitch * (_weapon.position - pivot);
            }
            AppliedFrames++;
            return true;
        }
    }
}
