using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 持枪姿态约束（换弹上半身叠加期间的**姿态覆写器**）：把武器骨的局部变换按
    /// <see cref="WeaponGripOffset"/> 跟随躯干骨，按幅度缩放与片段自身曲线混合。
    ///
    /// **归属**：视图表现编排，不属动画播放机制——不碰 <c>IAnimationBackend</c>/分层图/通道仲裁，
    /// 也不进状态机（状态机只裁决"该播什么"）。约束对象与槽位同生共死（驱动器建槽时建、释放时弃）。
    ///
    /// **写时序（唯一写者契约）**：必须在驱动帧的**动画 Evaluate 之后**调用（驱动器在播放器 Tick
    /// 之后调本方法）——Graph.Evaluate 由后端手动推进，故顺序可控。每帧写、退出即停写：
    /// 不改任何持久状态，动画下一帧自然重新接管（无需快照/恢复，也就不存在"忘记恢复"的泄漏面）。
    ///
    /// **混合口径**：写前先读武器骨当前局部变换——此刻它就是**本帧动画给出的值**（Evaluate 刚落地），
    /// 再按权重与跟随目标插值。故幅度缩放 0 时写回等值（等于不约束），1 时完全跟随，中间线性混合。
    /// </summary>
    public sealed class WeaponGripConstraint
    {
        private readonly Transform _weaponBone;
        private readonly Transform _followBone;
        private readonly Vector3 _offsetPos;
        private readonly Quaternion _offsetRot;
        private readonly float _weight;

        /// <summary>本约束是否可用（两骨都解析到才可用；不可用时 Update 显性拒绝，不静默无效）。</summary>
        public bool IsValid => _weaponBone != null && _followBone != null;

        /// <summary>已实际覆写的帧数（诊断/测试读值：换弹期应随帧增长，非换弹期恒 0）。</summary>
        public int AppliedFrames { get; private set; }

        /// <summary>构造：按偏移资产解析两骨。缺骨 → <see cref="IsValid"/> 为 false（显性失败面，
        /// 由驱动侧决定跳过并计入诊断，不静默假装生效）。</summary>
        public WeaponGripConstraint(Animator animator, WeaponGripOffset offset)
        {
            if (animator == null || offset == null || !offset.IsComplete)
                return;

            _followBone = animator.transform.Find(offset.FollowBonePath);
            _weaponBone = animator.transform.Find(offset.WeaponBonePath);
            if (_followBone == null || _weaponBone == null) return;

            _offsetPos = offset.LocalPosition;
            _offsetRot = offset.LocalRotation;
            _weight = offset.TorsoWeight;
        }

        /// <summary>换弹叠加层在场时覆写武器骨位姿；不在场则本帧不写（动画值原样保留）。
        /// 不可用 → 返回 false（调用方据此计入诊断）。</summary>
        public bool Update(bool reloadUpperActive)
        {
            if (!IsValid) return false;
            if (!reloadUpperActive) return true;

            // 写前读数 = 本帧动画 Evaluate 刚落地的值（幅度缩放的另一端）。
            var animPos = _weaponBone.localPosition;
            var animRot = _weaponBone.localRotation;

            // 跟随目标：偏移是"枪在跟随骨局部空间"的位姿（烘焙同理，两边同一套 API）。
            // 位置：跟随骨局部 → 世界 → 武器骨父空间。
            var targetWorld = _followBone.TransformPoint(_offsetPos);
            var targetPos = _weaponBone.parent.InverseTransformPoint(targetWorld);
            // 旋转：跟随骨世界旋转 × 偏移旋转 → 武器骨父空间。
            var targetRot = Quaternion.Inverse(_weaponBone.parent.rotation) * (_followBone.rotation * _offsetRot);

            _weaponBone.localPosition = Vector3.Lerp(animPos, targetPos, _weight);
            _weaponBone.localRotation = Quaternion.Slerp(animRot, targetRot, _weight);
            AppliedFrames++;
            return true;
        }
    }
}
