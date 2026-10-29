using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 登记行类别——装载器按类分流到 <see cref="AnimationProfile"/> 的登记面，一一对应不发明新面。
    /// Fallback 行的源 ID 是**未登记**的语义 ID（回退链只在定义表未命中时生效——挂在已登记 ID 上是死链）；
    /// Mask 行以排除子树路径为 <see cref="AnimationProfileRow.Id"/>（行身份即排除目标）。
    /// </summary>
    public enum AnimationProfileRowKind
    {
        /// <summary>单片段定义 → <see cref="AnimationProfile.Register"/>（绑定/通道/循环/区间/持帧）。</summary>
        Single = 0,
        /// <summary>混合定义 → <see cref="AnimationProfile.RegisterBlend"/>（有序绑定槽位集）。</summary>
        Blend = 1,
        /// <summary>回退关系 → <see cref="AnimationProfile.RegisterFallback"/>：
        /// <see cref="AnimationProfileRow.Id"/>＝未登记的源语义 ID，
        /// <see cref="AnimationProfileRow.FallbackId"/>＝回退目标（须已登记——两遍扫保证行序无关）。</summary>
        Fallback = 2,
        /// <summary>叠加层 Mask 排除子树 → <see cref="AnimationProfile.RegisterOverlayMaskExclusions"/>
        ///（<see cref="AnimationProfileRow.Id"/> 即排除子树路径）。</summary>
        MaskExclusion = 3,
    }

    /// <summary>
    /// Profile 登记行：**配置面（数据表/资产）到 <see cref="AnimationProfile"/> 的唯一输送形态**——
    /// 零引擎、零表框架依赖的纯数据载体（表适配器把生成行翻译成本行，翻译边界在适配器）。
    /// 字段按 <see cref="Kind"/> 取用、未用字段保持默认。<see cref="Id"/> 在整张行集内唯一
    /// （单片段/混合按"同 ID 不得两栖"互斥，Mask 以路径为 id——该唯一性正是表的主键）。
    /// </summary>
    public sealed class AnimationProfileRow
    {
        /// <summary>行身份＝表主键：Single/Blend 为语义 ID 串；Fallback 为未登记的源语义 ID；
        /// MaskExclusion 为排除子树路径。整张行集内唯一（定义间"同 ID 不得两栖"互斥、回退源无定义不撞、
        /// 路径行身份独占）。</summary>
        public string Id = "";

        /// <summary>模型族（装载时按族过滤——一表多族的分派键；行归本族才登记）。</summary>
        public string ModelFamily = "";

        public AnimationProfileRowKind Kind;

        /// <summary>通道（Single/Blend 用）。</summary>
        public AnimationChannel Channel;

        /// <summary>Single：后端绑定键（与片源清单键同规）。</summary>
        public string Binding = "";

        /// <summary>Blend：槽位绑定集（槽位序＝权重数组序）。</summary>
        public string[] Bindings = Array.Empty<string>();

        /// <summary>Single：循环定义（循环播放不产生自然 Completed）。</summary>
        public bool Loop;

        /// <summary>Single/Blend：合法速度区间（含端点；越界请求拒绝）。</summary>
        public float MinSpeed;

        /// <summary>Single/Blend：合法速度区间上界。</summary>
        public float MaxSpeed;

        /// <summary>Single：自然完成后持末帧（帧锁定——通道不停机）。</summary>
        public bool HoldOnFinish;

        /// <summary>Single：是否需要资源装载（false＝预加载命中即提交）。</summary>
        public bool RequiresLoad;

        /// <summary>Fallback：回退目标 ID（须已登记——装载器两遍扫保证行序无关；其余类别不用）。</summary>
        public string FallbackId = "";
    }

    /// <summary>
    /// 行装载器（配置面唯一装载路径）：模型族过滤 → **两遍扫**逐行调用
    /// <see cref="AnimationProfile"/> 的登记 API——
    /// ①定义遍（Single/Blend 登记与校验：区间/两栖/空绑定）；
    /// ②关联遍（回退关系 + Mask 排除——回退目标的"已登记"校验由此与行序无关）。
    /// 校验/防环/两栖裁决全部复用登记期机制；本类只做行分流与行上下文包装
    /// （第 N 行、什么内容、为什么被拒）。按模型族装配：同一张行集服务多族。
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

            // ① 定义遍：Single/Blend 登记（登记即校验——区间/两栖/空绑定在此生效）
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

            // ② 关联遍：回退关系（源 ID 无定义——目标已由①登记，行序无关）+ Mask 排除
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
