using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 行装载器（配置面唯一装载路径）：模型族过滤 → **两遍扫**逐行调用 <see cref="AnimationProfile"/> 的登记 API—— ①定义遍（Single/Blend 登记与校验：区间/两栖/空绑定。
    /// </summary>
    public static class AnimationProfileLoader
    {
        /// <summary>把行集装载成指定模型族的 Profile。任何非法行抛 <see cref="InvalidOperationException"/>
        /// （带行号/行内容）——装载失败＝配置错误，装配期显性失败、不留半份 Profile。</summary>
        public static AnimationProfile FromRows(IEnumerable<AnimationProfileRow> rows,
            string modelFamily, FallbackPolicy policy = FallbackPolicy.Reject)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (string.IsNullOrEmpty(modelFamily))
                throw new ArgumentException("模型族为空——装载须指明取哪一族的登记面", nameof(modelFamily));

            var family = new List<(AnimationProfileRow row, int index)>();
            int seen = 0;
            foreach (var row in rows)
            {
                seen++;
                if (row == null)
                    throw RowError(seen, null, "行为 null");
                if (string.Equals(row.ModelFamily, modelFamily, StringComparison.Ordinal))
                    family.Add((row, seen));                          // 他族行跳过（分派键不命中）
            }

            var profile = new AnimationProfile(policy);

            // ① 定义遍：Single/Blend 登记（登记即校验——区间/两栖/空绑定在此生效）。
            foreach (var (row, index) in family)
            {
                try
                {
                    switch (row.Kind)
                    {
                        case AnimationProfileRowKind.Single:
                            profile.Register(new AnimationDefinition(new AnimationId(row.Id), row.Channel,
                                row.Binding, row.Loop, row.MinSpeed, row.MaxSpeed, row.RequiresLoad, row.HoldOnFinish));
                            break;
                        case AnimationProfileRowKind.Blend:
                            profile.RegisterBlend(new AnimationBlendDefinition(new AnimationId(row.Id), row.Channel,
                                row.Bindings, row.MinSpeed, row.MaxSpeed));
                            break;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    throw RowError(index, row, ex.Message, ex);
                }
            }

            // ② 关联遍：回退关系（源 ID 无定义——目标已由①登记，行序无关）+ Mask 排除。
            foreach (var (row, index) in family)
            {
                try
                {
                    switch (row.Kind)
                    {
                        case AnimationProfileRowKind.Fallback:
                            profile.RegisterFallback(new AnimationId(row.Id), new AnimationId(row.FallbackId));
                            break;
                        case AnimationProfileRowKind.MaskExclusion:
                            profile.RegisterOverlayMaskExclusions(row.Id);
                            break;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    throw RowError(index, row, ex.Message, ex);
                }
            }
            return profile;
        }

        /// <summary>行上下文包装（行号 + 行身份——不吞登记 API 的原始裁决理由）。</summary>
        private static InvalidOperationException RowError(int index, AnimationProfileRow row,
            string reason, Exception inner = null)
        {
            string who = row == null ? "null 行" : $"id={row.Id} kind={row.Kind} family={row.ModelFamily}";
            return new InvalidOperationException($"动画 Profile 行装载失败（第 {index} 行，{who}）：{reason}", inner);
        }
    }
}
