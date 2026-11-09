using System;
using System.Collections.Generic;
using LiteFramework.Animation;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 动画 Profile 行读口（客户端单世界便捷读，同 CombatConfig/WeaponConfig 家族）。
    /// </summary>
    public static class AnimationProfileConfig
    {
        private static volatile IReadOnlyList<AnimationProfileRow> _rows = Array.Empty<AnimationProfileRow>();

        /// <summary>已发布的全族登记行（多族共存——装配方按 ModelFamily 过滤）。</summary>
        public static IReadOnlyList<AnimationProfileRow> Rows => _rows;

        /// <summary>候选校验后的原子发布（配置适配器专用——失败不触碰本读口）。</summary>
        public static void Publish(IReadOnlyList<AnimationProfileRow> rows)
            => _rows = rows ?? Array.Empty<AnimationProfileRow>();
    }
}
