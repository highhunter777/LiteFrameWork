using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 动画定义（《动画模块专项设计》§4）：只读定义，**播放实例另存**当前位置/层权重/Handle/结束状态——
    /// 本类是不可变的共享数据，不在其上存角色状态。
    /// </summary>
    public readonly struct AnimationDefinition
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>后端绑定：Controller 状态完整路径/层，或后续 Clip 资源键。</summary>
        public readonly string Binding;
        /// <summary>是否循环（§5"循环播放不会自然 Completed"）。</summary>
        public readonly bool Loop;
        /// <summary>合法速度区间（含端点）；越界请求按 <see cref="FallbackPolicy.Reject"/> 拒绝。</summary>
        public readonly float MinSpeed;
        public readonly float MaxSpeed;
        /// <summary>是否需要装载（false = 预加载集合，提交即生效）。</summary>
        public readonly bool RequiresLoad;

        public AnimationDefinition(AnimationId id, AnimationChannel channel, string binding,
            bool loop = false, float minSpeed = 0.01f, float maxSpeed = 4f, bool requiresLoad = false)
        {
            Id = id;
            Channel = channel;
            Binding = binding;
            Loop = loop;
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
            RequiresLoad = requiresLoad;
        }

        public bool IsValid => Id.IsValid && !string.IsNullOrEmpty(Binding);
    }

    /// <summary>定义缺失或资源缺失时的策略（§4 Fallback："拒绝、回退或保持已有姿态的明确策略"）。</summary>
    public enum FallbackPolicy
    {
        /// <summary>拒绝请求，保持当前姿态（默认）。</summary>
        Reject = 0,
        /// <summary>回退到同通道已登记的回退 ID（<see cref="AnimationProfile.FallbackOf"/>），回退深度受限。</summary>
        UseFallback = 1,
    }

    /// <summary>
    /// Profile/Resolver（§3"Profile/Resolver：动画 ID、角色/武器配置、可用后端能力 → 已验证的状态/资源/通道/混合方案"）：
    /// **只做解析，不做播放**——后端与业务都不认识"哪个 ID 对应哪个状态路径"。
    ///
    /// 校验纪律（§4"不得到处散写参数字符串"）：登记即校验，非法定义显性失败；
    /// 回退链**限制深度**并禁止成环（§4"限制回退深度，禁止环"）。
    /// </summary>
    public sealed class AnimationProfile
    {
        /// <summary>回退链最大深度（超过即视为环或过深配置，登记时拒绝）。</summary>
        public const int MaxFallbackDepth = 4;

        private readonly Dictionary<AnimationId, AnimationDefinition> _defs = new Dictionary<AnimationId, AnimationDefinition>();
        private readonly Dictionary<AnimationId, AnimationId> _fallback = new Dictionary<AnimationId, AnimationId>();

        public FallbackPolicy Policy { get; }

        public int Count => _defs.Count;

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

            _defs[def.Id] = def;
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
                request.StartNormalized, request.Speed, def.RequiresLoad);
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

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
