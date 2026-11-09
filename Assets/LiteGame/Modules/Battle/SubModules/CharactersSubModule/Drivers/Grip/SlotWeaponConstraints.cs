using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 单个槽位的武器相关约束三件套（持枪 / 俯仰 / 枪口对齐）。
    /// </summary>
    internal readonly struct SlotWeaponConstraints
    {
        public readonly WeaponGripConstraint Grip;
        public readonly AimPitchPostureDriver Posture;
        public readonly WeaponAimConstraint WeaponAim;

        private SlotWeaponConstraints(WeaponGripConstraint grip, AimPitchPostureDriver posture,
            WeaponAimConstraint weaponAim)
        {
            Grip = grip;
            Posture = posture;
            WeaponAim = weaponAim;
        }

        /// <summary>
        /// 按视图骨架建立三项约束：任一约束的骨路径/映射在骨架上不存在 → 该项不建并留一条警告 （调用方据此走"片段曲线驱动"降级）。
        /// </summary>
        public static SlotWeaponConstraints Create(Animator animator, WeaponGripCatalog catalog,
            int slot, GameObject view, ref bool warnedMissingCatalog)
        {
            if (catalog == null)
            {
                if (!warnedMissingCatalog)
                {
                    warnedMissingCatalog = true;
                    Log.Warning($"grip.bin 未装载：view「{view.name}」——换弹期枪由片段曲线驱动，"
                        + "俯仰姿态/枪口对齐一并跳过（持枪约束降级）", LogTag.Anim);
                }
                return default;
            }

            // 持枪约束：换弹叠加层在场时武器骨跟随持枪手。骨路径在当前模型族上解析不到 → 不建。
            WeaponGripConstraint grip = new WeaponGripConstraint(animator, catalog);
            if (!grip.IsValid)
            {
                Log.Warning($"slot {slot} 持枪骨路径在骨架上不存在"
                    + $"：view「{view.name}」——换弹期枪将完全由片段曲线驱动", LogTag.Anim);
                grip = null;
            }

            // 俯仰姿态（TPS 自由视角）：射击语境内上半身随相机 pitch——Chest 骨经人形映射解析
            // （任意人形 Rig 通用）。缺映射 → 不建（显性跳过，与持枪约束同失败面）。
            AimPitchPostureDriver posture = new AimPitchPostureDriver(animator, catalog);
            if (!posture.IsValid)
            {
                Log.Warning($"slot {slot} 人形 Chest 骨映射缺失：view「{view.name}」——俯仰姿态不生效", LogTag.Anim);
                posture = null;
            }

            // 枪口对齐（TPS ADS）：只补枪骨残余下偏，不改手/臂动画；缺枪骨或 Muzzle → 不建。
            WeaponAimConstraint weaponAim = new WeaponAimConstraint(animator, catalog);
            if (!weaponAim.IsValid)
            {
                Log.Warning($"slot {slot} 枪骨/Muzzle 缺失：view「{view.name}」——ADS 枪口对齐不生效", LogTag.Anim);
                weaponAim = null;
            }

            return new SlotWeaponConstraints(grip, posture, weaponAim);
        }
    }
}
