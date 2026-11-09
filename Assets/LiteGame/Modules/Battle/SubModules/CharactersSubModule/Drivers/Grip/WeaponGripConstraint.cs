using System;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 持枪姿态约束（换弹上半身叠加期间的**姿态覆写器**）：把武器骨的局部变换按 WeaponGripCatalog（grip.bin 装载的跟随骨路径/偏移/幅度）跟随**持枪手**骨 （为什么跟随右手而非躯干——武器骨 。）。
    /// </summary>
    public sealed class WeaponGripConstraint
    {
        private readonly Transform _weaponBone;
        private readonly Transform _followBone;
        private readonly Vector3 _offsetPos;
        private readonly Quaternion _offsetRot;
        private readonly float _bakedWeight;

        /// <summary>本约束是否可用（两骨都解析到才可用；不可用时 Update 显性拒绝，不静默无效）。</summary>
        public bool IsValid => _weaponBone != null && _followBone != null;

        /// <summary>已实际覆写的帧数（诊断/测试读值：换弹期应随帧增长，非换弹期恒 0）。</summary>
        public int AppliedFrames { get; private set; }

        /// <summary>构造：按 <paramref name="catalog"/> 的骨路径解析两骨，偏移/幅度全部取数据产物。
        /// 换模型族不匹配时骨解析失败 → <see cref="IsValid"/> 为 false 的显性失败面，
        /// 由驱动侧决定跳过并计入诊断，不静默假装生效。
        /// <paramref name="catalog"/> 必填（null 抛 <see cref="ArgumentNullException"/>——默认值唯一来源是 grip.bin）。</summary>
        public WeaponGripConstraint(Animator animator, WeaponGripCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (animator == null) return;

            _followBone = animator.transform.Find(catalog.FollowPath);
            _weaponBone = animator.transform.Find(catalog.WeaponPath);
            _offsetPos = catalog.LocalPosition;
            _offsetRot = Quaternion.Euler(catalog.LocalEuler);
            _bakedWeight = catalog.TorsoWeight;
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

            float weight = GripTuning.Resolve(_bakedWeight);
            _weaponBone.localPosition = Vector3.Lerp(animPos, targetPos, weight);
            _weaponBone.localRotation = Quaternion.Slerp(animRot, targetRot, weight);
            AppliedFrames++;
            return true;
        }
    }
}
