using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 动画 Profile 行读口（客户端单世界便捷读，同 <c>CombatConfig</c>/<c>WeaponConfig</c> 家族）：
    /// 配置适配器（LiteClient 的 Luban 服务）装载 tbanimationprofile 后把翻译好的
    /// <see cref="AnimationProfileRow"/> 列表**原子发布**到这里；消费方（对局装配/构建器/测试）经
    /// <see cref="AnimationProfileLoader.FromRows"/> 按模型族装配 Profile。行翻译（类别/通道字符串→枚举）
    /// 发生在配置适配器的装载边界——本读口只持有已翻译的纯行数据。
    /// 装载失败＝候选校验拒绝，读口保留旧态（快照化纪律归配置服务）。
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
