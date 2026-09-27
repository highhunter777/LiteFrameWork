using System;
using System.Collections.Generic;

namespace LiteFramework.Animation
{
    /// <summary>定义缺失或资源缺失时的策略（§4 Fallback："拒绝、回退或保持已有姿态的明确策略"）。</summary>
    public enum FallbackPolicy
    {
        /// <summary>拒绝请求，保持当前姿态（默认）。</summary>
        Reject = 0,
        /// <summary>回退到同通道已登记的回退 ID（<see cref="AnimationProfile.RegisterFallback"/>），回退深度受限。</summary>
        UseFallback = 1,
    }

    /// <summary>
    /// Profile/Resolver（§3"Profile/Resolver：动画 ID、角色/武器配置、可用后端能力 → 已验证的状态/资源/通道/混合方案"）：
    /// **只做解析，不做播放**——后端与业务都不认识"哪个 ID 对应哪个状态路径"。
    ///
    /// 校验纪律（§4"不得到处散写参数字符串"）：登记即校验，非法定义显性失败；
    /// 回退链**限制深度**并禁止成环（§4"限制回退深度，禁止环"）。
    /// 单片段与混合**各有一张 ID 表**（同 ID 不得两栖——解析形态会歧义，登记期即拒绝）；
    /// 回退链只作用于单片段路径（混合形态不同，不做跨形态回退）。
    /// </summary>
    public sealed class AnimationProfile
    {
        /// <summary>回退链最大深度（超过即视为环或过深配置，登记时拒绝）。</summary>
        public const int MaxFallbackDepth = 4;

        /// <summary>混合槽位上限（§12 上限纪律；与首个后端的固定图容量同源——
        /// <c>AnimatorAnimationBackend.MaxBlendInputs</c> 直接取本值，防两处漂移）。</summary>
        public const int MaxBlendSlots = 4;

        private readonly Dictionary<AnimationId, AnimationDefinition> _defs = new Dictionary<AnimationId, AnimationDefinition>();
        private readonly Dictionary<AnimationId, AnimationBlendDefinition> _blends = new Dictionary<AnimationId, AnimationBlendDefinition>();
        private readonly Dictionary<AnimationId, AnimationId> _fallback = new Dictionary<AnimationId, AnimationId>();

        public FallbackPolicy Policy { get; }

        public int Count => _defs.Count;

        /// <summary>已登记的混合定义数。</summary>
        public int BlendCount => _blends.Count;

        public AnimationProfile(FallbackPolicy policy = FallbackPolicy.Reject)
        {
            Policy = policy;
        }

        /// <summary>登记定义（同 ID 覆盖）。非法定义（空 ID/空绑定/非法速度区间）显性拒绝。</summary>
        public AnimationProfile Register(in AnimationDefinition def)
        {
            if (!def.Id.IsValid) throw new ArgumentException("动画定义缺少合法 AnimationId", nameof(def));
            if (string.IsNullOrEmpty(def.Binding)) throw new ArgumentException($"动画定义 {def.Id} 缺少后端绑定", nameof(def));
            if (!(def.MaxSpeed >= def.MinSpeed) || def.MinSpeed <= 0f || float.IsInfinity(def.MaxSpeed))
                throw new ArgumentException($"动画定义 {def.Id} 速度区间非法:[{def.MinSpeed},{def.MaxSpeed}]", nameof(def));
            if (_blends.ContainsKey(def.Id))
                throw new ArgumentException($"ID {def.Id} 已登记为混合定义——同一 ID 不能既是混合又是单片段", nameof(def));

            _defs[def.Id] = def;
            return this;
        }

        /// <summary>
        /// 登记混合定义（同 ID 覆盖）。非法形态**登记即拒绝**：槽位数不在 1..<see cref="MaxBlendSlots"/>、
        /// 任一槽位绑定为空、速度区间非法、**ID 已是单片段定义**（同 ID 两栖会让解析形态歧义）。
        /// </summary>
        public AnimationProfile RegisterBlend(in AnimationBlendDefinition def)
        {
            if (!def.Id.IsValid) throw new ArgumentException("混合定义缺少合法 AnimationId", nameof(def));
            if (def.SlotCount == 0 || def.SlotCount > MaxBlendSlots)
                throw new ArgumentException($"混合定义 {def.Id} 槽位数非法（1..{MaxBlendSlots}）", nameof(def));
            for (int i = 0; i < def.SlotCount; i++)
            {
                if (string.IsNullOrEmpty(def.Bindings[i]))
                    throw new ArgumentException($"混合定义 {def.Id} 槽位 {i} 缺少绑定", nameof(def));
            }
            if (!(def.MaxSpeed >= def.MinSpeed) || def.MinSpeed <= 0f || float.IsInfinity(def.MaxSpeed))
                throw new ArgumentException($"混合定义 {def.Id} 速度区间非法:[{def.MinSpeed},{def.MaxSpeed}]", nameof(def));
            if (_defs.ContainsKey(def.Id))
                throw new ArgumentException($"ID {def.Id} 已登记为单片段定义——同一 ID 不能既是混合又是单片段", nameof(def));

            _blends[def.Id] = def;
            return this;
        }

        /// <summary>
        /// 登记回退关系（id 缺失时改用 fallback）。**成环在登记期即拒绝**；
        /// **深度上限在解析期约束**（<see cref="TryFollowFallback"/> 最多走 <see cref="MaxFallbackDepth"/> 跳）——
        /// 链长是随登记逐步生长的性质，单次登记看不到全貌，放在解析期才是可靠的守卫。
        /// </summary>
        public AnimationProfile RegisterFallback(AnimationId id, AnimationId fallback)
        {
            if (!id.IsValid || !fallback.IsValid)
                throw new ArgumentException("回退关系两端都必须是合法 AnimationId", nameof(fallback));

            // 环检测：沿已有 fallback 链走——若回到 id 则成环。步数上限防手写坏数据把这里转成死循环。
            AnimationId cursor = fallback;
            for (int guard = 0; guard <= MaxFallbackDepth * 4; guard++)
            {
                if (cursor.Equals(id))
                    throw new ArgumentException($"回退链成环:{id} → … → {cursor}", nameof(fallback));
                if (!_fallback.TryGetValue(cursor, out cursor)) break;
            }

            if (_defs.ContainsKey(id) && !_defs.ContainsKey(fallback))
                throw new ArgumentException($"回退目标未登记:{fallback}", nameof(fallback));

            _fallback[id] = fallback;
            return this;
        }

        /// <summary>
        /// 解析请求 → 播放方案。拒绝原因见 <see cref="AnimationStartResult.Reason"/>——
        /// **调用方据拒绝原因保持原播放，不做静默降级**（§4/§6）。
        /// </summary>
        public bool TryResolve(in AnimationRequest request, out AnimationResolvedPlayback resolved,
            out AnimationStartResult.Reason rejectReason)
        {
            resolved = default;
            rejectReason = AnimationStartResult.Reason.None;

            // 字段校验先于定义查找：非法请求不该因为"刚好没定义"而报成 InvalidDefinition
            if (!request.Id.IsValid) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
            if (!IsFinite(request.Speed) || request.Speed <= 0f) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
            if (!IsFinite(request.StartNormalized) || request.StartNormalized < 0f || request.StartNormalized > 1f)
            { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }

            if (!_defs.TryGetValue(request.Id, out AnimationDefinition def))
            {
                if (Policy == FallbackPolicy.UseFallback && TryFollowFallback(request.Id, out def))
                {
                    // 落到回退定义继续校验（速度区间按回退定义判）
                }
                else
                {
                    rejectReason = AnimationStartResult.Reason.InvalidDefinition;
                    return false;
                }
            }

            if (request.Speed < def.MinSpeed || request.Speed > def.MaxSpeed)
            {
                rejectReason = AnimationStartResult.Reason.UnsupportedCapability;   // 能力/区间不支持，不静默夹取
                return false;
            }

            // 通道以后端绑定所在定义为权威（状态路径绑死在某通道上）；请求通道与之不符时以请求为准会让
            // "上半身动作被塞进全身通道"这类错误静默生效——故按定义通道提交，调用方可用 TryGetDefinition 自查。
            resolved = new AnimationResolvedPlayback(def.Id, def.Channel, def.Binding,
                request.StartNormalized, request.Speed, def.RequiresLoad, def.Loop);
            return true;
        }

        /// <summary>沿回退链找第一个已登记的定义（深度受限；登记期已防环，这里仍按深度兜底）。</summary>
        private bool TryFollowFallback(AnimationId id, out AnimationDefinition def)
        {
            AnimationId cursor = id;
            for (int depth = 0; depth < MaxFallbackDepth; depth++)
            {
                if (!_fallback.TryGetValue(cursor, out cursor)) break;
                if (_defs.TryGetValue(cursor, out def)) return true;
            }
            def = default;
            return false;
        }

        public bool TryGetDefinition(AnimationId id, out AnimationDefinition def) => _defs.TryGetValue(id, out def);

        public bool TryGetBlendDefinition(AnimationId id, out AnimationBlendDefinition def) => _blends.TryGetValue(id, out def);

        /// <summary>
        /// 解析混合请求 → 混合方案。与 <see cref="TryResolve"/> 同一纪律：**先字段校验再查定义**，
        /// 非法请求不因"刚好没登记"报成 InvalidDefinition；缺定义为 InvalidDefinition（**不参与回退链**）。
        /// **权重整组校验**（§6"不能只占一半"的解析面）：任一条不合法（长度与槽位数不符、非有限、负值、
        /// 总和 ≤ 0 或溢出）即整组拒绝，不做部分接受。混合**不需要装载**——槽位绑定必须已可直接提交。
        /// </summary>
        public bool TryResolveBlend(in AnimationBlendRequest request, out AnimationResolvedBlend resolved,
            out AnimationStartResult.Reason rejectReason)
        {
            resolved = default;
            rejectReason = AnimationStartResult.Reason.None;

            if (!request.Id.IsValid) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
            if (!IsFinite(request.Speed) || request.Speed <= 0f) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
            if (!IsFinite(request.StartNormalized) || request.StartNormalized < 0f || request.StartNormalized > 1f)
            { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
            if (request.Weights == null) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }

            if (!_blends.TryGetValue(request.Id, out AnimationBlendDefinition def))
            {
                rejectReason = AnimationStartResult.Reason.InvalidDefinition;
                return false;
            }

            if (request.Weights.Length != def.SlotCount)
            {
                rejectReason = AnimationStartResult.Reason.InvalidRequest;    // 权重必须逐槽位给全
                return false;
            }

            float weightSum = 0f;
            for (int i = 0; i < request.Weights.Length; i++)
            {
                float w = request.Weights[i];
                if (!IsFinite(w) || w < 0f) { rejectReason = AnimationStartResult.Reason.InvalidRequest; return false; }
                weightSum += w;
            }
            if (!IsFinite(weightSum) || weightSum <= 0f)
            {
                rejectReason = AnimationStartResult.Reason.InvalidRequest;    // 全零/溢出：没有可播的东西
                return false;
            }

            if (request.Speed < def.MinSpeed || request.Speed > def.MaxSpeed)
            {
                rejectReason = AnimationStartResult.Reason.UnsupportedCapability;   // 能力/区间不支持，不静默夹取
                return false;
            }

            resolved = new AnimationResolvedBlend(def.Id, def.Channel, def.Bindings, request.Weights,
                request.StartNormalized, request.Speed);
            return true;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}