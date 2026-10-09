using System;
using System.Collections.Generic;
using LiteFramework.Animation;

namespace LiteClient
{
    /// <summary>
    /// tbanimationprofile 生成行 → Core <see cref="AnimationProfileRow"/> 的**翻译边界**
    /// （类别/通道字符串 → 枚举——字符串语义不出适配器，同 fire_mode 先例）。
    /// 纯函数、无状态：<see cref="ConfigService.ValidateCandidate"/> 用它做候选校验
    /// （含全族 FromRows 装载演练——语义校验前置到启动），装载期再取一次结果发布读口。
    /// </summary>
    public static class AnimationProfileTableMapper
    {
        /// <summary>整表翻译。任何非法字符串（类别/通道）抛 <see cref="ArgumentException"/>（带行 id）。</summary>
        public static List<AnimationProfileRow> ToRows(cfg.Tbanimationprofile table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var rows = new List<AnimationProfileRow>(table.DataList.Count);
            foreach (var row in table.DataList)
            {
                var kind = ParseKind(row.Kind, row.Id);
                rows.Add(new AnimationProfileRow
                {
                    Id = row.Id,
                    ModelFamily = row.ModelFamily,
                    Kind = kind,
                    Channel = ParseChannel(row.Channel, kind, row.Id),
                    Binding = row.Binding,
                    Bindings = ParseBindings(row.Bindings),   // 表列是逗号分隔串——拆分归本翻译边界
                    Loop = row.Loop,
                    MinSpeed = row.MinSpeed,
                    MaxSpeed = row.MaxSpeed,
                    HoldOnFinish = row.HoldOnFinish,
                    RequiresLoad = row.RequiresLoad,
                    FallbackId = row.FallbackId,
                });
            }
            return rows;
        }

        /// <summary>混合槽位绑定串拆分（空串→空集：RegisterBlend 对空槽位显性拒绝——校验归登记面）。</summary>
        private static string[] ParseBindings(string bindings)
            => string.IsNullOrEmpty(bindings) ? Array.Empty<string>() : bindings.Split(',');

        private static AnimationProfileRowKind ParseKind(string kind, string id)
            => kind switch
            {
                "single" => AnimationProfileRowKind.Single,
                "blend" => AnimationProfileRowKind.Blend,
                "fallback" => AnimationProfileRowKind.Fallback,
                "mask" => AnimationProfileRowKind.MaskExclusion,
                _ => throw new ArgumentException(
                    $"行 id={id} 非法类别：'{kind}'（合法集 single/blend/fallback/mask——表列与装载器漂移）"),
            };

        /// <summary>通道翻译：single/blend 必填且须合法；fallback/mask 不用通道（空串→默认值）。</summary>
        private static AnimationChannel ParseChannel(string channel, AnimationProfileRowKind kind, string id)
        {
            if (kind != AnimationProfileRowKind.Single && kind != AnimationProfileRowKind.Blend)
                return default;                                   // 通道字段不属于该行类别——忽略
            return channel switch
            {
                "base" => AnimationChannel.Base,
                "overlay" => AnimationChannel.Overlay,
                "override" => AnimationChannel.Override,
                _ => throw new ArgumentException(
                    $"行 id={id} 非法通道：'{channel}'（合法集 base/overlay/override）"),
            };
        }
    }
}
