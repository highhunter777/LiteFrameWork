using System;
using System.Collections.Generic;
using LiteFramework.Animation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;   // PlayableExtensions：GetTime/SetTime/SetInputWeight/Pause 等扩展方法都在这

namespace LiteView.Animation
{
    /// <summary>
    /// Animator 后端（《动画模块专项设计》§7"PlayableGraph 采用 Manual 更新"）：**Clip 直驱**——
    /// 绑定键 → <see cref="AnimationClip"/> → 片段 Playable 直连分层图（§4 允许的「Clip 资源键」分支，
    /// 不走控制器参数/Trigger 驱动）。**RuntimeAnimatorController 可选**：存在时①占图基础层作开局
    /// 默认姿态②其片段自动按名索引（便利源）；缺失时（**直 Clip 模型**——带片段无控制器资产）无默认姿态位、
    /// 片段经 <see cref="RegisterClip"/> 外部登记（T-pose 由驱动层"机 Start 即提交"的同帧落位兜底）。
    /// 姿态推进只经 <see cref="Tick"/>（Graph.Evaluate——§7"Graph Evaluate 只由一个驱动器调用"）。
    ///
    /// **本类只做编排 + 能力声明 + 诊断聚合**，机制各有归属：
    /// - 图拓扑 / 层序 / 层权重 / 开局默认姿态 → <see cref="AnimationLayerGraph"/>（分层 6 输入 / 不分层 2 输入——
    ///   按构造模式取，均有界；反复打断不增长）；
    /// - 节点两形态（单片段 / 普通混合器）、通道运行态、断线与销毁 → <see cref="ChannelNode"/>；
    /// - 层权重推进与落位回收 → <see cref="ChannelBlendAdvance"/>；
    /// - 结束边界判定 → Core 的 <see cref="ClipCompletionTracker"/>（读定义的 Loop，不读资产 loop 设置）。
    ///
    /// **分层混合（三通道）**：在 Clip 直驱（可播任意 Clip）之上引入
    /// AnimationLayerMixerPlayable——通道叠加/叠加层混合落地，同时保持"不回到控制器参数/Trigger 驱动"
    /// （该资产参数全是 Trigger、多层无 Mask，§7 字面的控制器参数驱动在本资产上不成立，
    /// 走 §4 允许的「Clip 资源键」分支）。
    /// **Override 不需要"记忆并恢复旧移动动作"**：Base 从未离开层 0、时间持续推进，
    /// Override 权重归零即自然回到当前移动姿态（不引 shadow、不恢复过期旧动作——§6）。
    ///
    /// **能力位诚实声明（§4）**：<c>Looping | StartAtNormalized | SpeedOverride | ClipBlending</c>
    /// ＋分层模式下的 <c>OverrideChannel</c>（覆盖层不需 Mask——任何 rig 可用）＋Mask 构造成功时的
    /// <c>OverlayChannel</c>（非 humanoid 无 Mask ⇒ 叠加层请求被播放器提交前拒绝）。
    /// 速度经 <c>SetSpeed</c>；<see cref="Tick"/> 只把已缩放的 delta 交给 Evaluate，不再乘 Speed（§7 归属分离）。
    ///
    /// **构造模式（分层/不分层）**：`layered: true`（默认）＝三通道分层图（6 输入）＋Mask 叠加——多通道消费者
    /// （角色等）的形态；`layered: false`＝**仅基础通道**（2 输入图：当前+尾部交叉淡化——淡化是单通道内
    /// 新旧交替，不属于分层）＋跳过 Mask 构造（省骨架遍历）——门/物件等单通道直放消费者的精简形态，
    /// Override/Overlay 通道请求经 <see cref="ChannelOf"/> 返回 null 被显性拒绝（非分层不声明两位能力位）。
    ///
    /// **普通混合器（同通道多片段按权重混合）**：<see cref="TryPlayBlend"/> 用 <c>AnimationMixerPlayable</c>
    /// （无 Mask、纯权重，典型用途 Walk↔Run 按速度连续混合），**且是唯一的混合提交入口**——
    /// 语义 ID → 槽位绑定的解析归 Profile，经 <c>AnimationPlayer.PlayBlend</c> 进入本方法
    /// （不存在"绕过播放器直接按名字播"的第二条路径）；**混合节点按循环对待，不产生 Completed**。
    /// **淡化时长**：由构造函数显式传入（<see cref="DefaultBlendSeconds"/> 只是默认值，**不是**淡化数学里的常量），
    /// 并在构造时分发给每个通道（<see cref="ChannelState.BlendSeconds"/>）；`0` = 瞬时落位。
    /// </summary>
    public sealed class AnimatorAnimationBackend : IAnimationBackend
    {
        /// <summary>默认淡入/淡出时长（秒）——**只作构造参数的默认值**；淡化数学一律用实例持有的
        /// <see cref="BlendSeconds"/>（构造时显式传入，不从常量读）。§12"混合尾部必须有固定上限"。</summary>
        public const float DefaultBlendSeconds = 0.12f;

        /// <summary>同通道混合（普通混合器）的输入片段数上限——**与 <see cref="AnimationProfile.MaxBlendSlots"/> 同源**
        /// （登记期已挡住越界形态，这里只作执行面的防御；§12 节点数有界）。</summary>
        public const int MaxBlendInputs = AnimationProfile.MaxBlendSlots;

        /// <summary>完成/回卷判定容差（秒）。</summary>
        private const float TimeEpsilon = 1e-4f;

        private readonly Animator _animator;
        private readonly AnimationLayerGraph _layers;
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        private readonly bool _layered;
        private readonly AvatarMask _overlayMask;
        private readonly ChannelState _base;
        private readonly ChannelState _override;
        private readonly ChannelState _overlay;

        private bool _disposed;

        /// <summary>本后端的淡入/淡出时长（秒；**构造时显式传入**，0 = 瞬时落位）。
        /// 已分发给每个通道（<see cref="ChannelState.BlendSeconds"/>），淡化数学不读任何常量。</summary>
        public float BlendSeconds { get; }

        /// <param name="animator">视图实例上的 Animator。**不要求 RuntimeAnimatorController**：
        /// 控制器存在时——①占图基础层作开局默认姿态（首帧不露 T-pose）②其片段自动按名索引（便利源）；
        /// 控制器缺失时（**直 Clip 模型**）——无默认姿态位，片段必须经 <see cref="RegisterClip"/>
        /// 外部登记（T-pose 由驱动层"机 Start 即提交"的同帧落位兜底）。</param>
        /// <param name="blendSeconds">淡入/淡出时长（秒）：**当作必填对待**——默认值只是常见手感的兜底，
        /// 调用方应显式给（0 = 瞬时落位，测试与"直接切"用）。该值在构造时分发给每个通道，
        /// 每个通道的淡化数学只用自己的那一份。</param>
        /// <param name="overlayMaskExclusions">叠加层 Mask 的排除子树（Profile 登记面，见
        /// <see cref="OverlayMaskFactory"/>；null/空 = 无排除——transform 段由骨架自动派生，
        /// 排除面只承载"动画层不得触碰"的域，典型为布料骨。仅在分层模式下消费）。</param>
        /// <param name="layered">构造模式：true（默认）＝三通道分层图＋Mask 叠加；false＝仅基础通道
        /// （2 输入精简图、不构造 Mask、不声明 <c>OverrideChannel</c>/<c>OverlayChannel</c>——
        /// 门/物件等单通道直放消费者的精简形态，误用非基础通道被显性拒绝）。</param>
        public AnimatorAnimationBackend(Animator animator, float blendSeconds = DefaultBlendSeconds,
            IReadOnlyList<string> overlayMaskExclusions = null, bool layered = true)
        {
            _animator = animator ?? throw new ArgumentNullException(nameof(animator));
            if (float.IsNaN(blendSeconds) || float.IsInfinity(blendSeconds) || blendSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(blendSeconds), blendSeconds,
                    "混合时长必须 ≥ 0 且有限（0 = 瞬时落位）");
            BlendSeconds = blendSeconds;
            _layered = layered;

            // 图拓扑按构造模式取界（分层 6 输入 / 不分层 2 输入，见 AnimationLayerGraph）
            _layers = new AnimationLayerGraph(_animator, layered); // 控制器可选（默认姿态/片段索引见类注释）

            if (_animator.runtimeAnimatorController != null)
                RegisterControllerClips();                         // 控制器片段索引（便利源；直 Clip 模型跳过）

            _base = new ChannelState(AnimationChannel.Base, isBase: true,
                AnimationLayerGraph.BaseCurrent, AnimationLayerGraph.BaseTail, BlendSeconds);
            if (layered)
            {
                _override = new ChannelState(AnimationChannel.Override, isBase: false,
                    AnimationLayerGraph.OverrideCurrent, AnimationLayerGraph.OverrideTail, BlendSeconds);
                _overlay = new ChannelState(AnimationChannel.Overlay, isBase: false,
                    AnimationLayerGraph.OverlayCurrent, AnimationLayerGraph.OverlayTail, BlendSeconds);

                // 纯函数（含排除路径存在性诊断），见 OverlayMaskFactory——不分层跳过（省骨架遍历）
                _overlayMask = OverlayMaskFactory.TryBuild(_animator, overlayMaskExclusions);
                if (_overlayMask != null)
                {
                    _layers.ApplyOverlayMask(AnimationLayerGraph.OverlayCurrent, _overlayMask);
                    _layers.ApplyOverlayMask(AnimationLayerGraph.OverlayTail, _overlayMask);
                }
            }
        }

        // ---- 能力位（§4 诚实声明，不静默降级）----

        public AnimationBackendCapabilities Capabilities
            => AnimationBackendCapabilities.Looping
             | AnimationBackendCapabilities.StartAtNormalized
             | AnimationBackendCapabilities.SpeedOverride
             | AnimationBackendCapabilities.ClipBlending                                  // 普通混合器路径（TryPlayBlend）
             | (_layered ? AnimationBackendCapabilities.OverrideChannel : AnimationBackendCapabilities.None)      // 覆盖层不需 Mask——任何 rig
             | (_overlayMask != null ? AnimationBackendCapabilities.OverlayChannel : AnimationBackendCapabilities.None); // 叠加层需 Mask 构造成功

        // ---- 诊断（§11；零分配，调用方复用容器）----

        /// <summary>未知绑定被拒次数（§13-4；后端不认识该片段名）。</summary>
        public int UnknownBindings { get; private set; }

        /// <summary>混合尾部被确定性截断的次数（§12"确定截断策略"——新请求打断在途尾部）。</summary>
        public int TruncatedBlends { get; private set; }

        /// <summary>当前占用中的通道数（0..分层 3 / 不分层 1）。</summary>
        public int ActiveChannels
        {
            get
            {
                int n = 0;
                if (_base.Active) n++;
                if (_override != null && _override.Active) n++;
                if (_overlay != null && _overlay.Active) n++;
                return n;
            }
        }

        /// <summary>图内 playable 节点数（§12 节点稳定用例的观测量）。</summary>
        public int PlayableCount => _layers.PlayableCount;

        /// <summary>叠加层 LayerMask 是否可用（false = 非 humanoid，Overlay 通道不可用）。</summary>
        public bool HasOverlayMask => _overlayMask != null;

        /// <summary>单通道诊断读数（纯值，零分配）。</summary>
        public bool TryGetChannelDebug(AnimationChannel channel, out AnimationChannelDebug debug)
        {
            debug = default;
            ChannelState ch = ChannelOf(channel);
            if (ch == null || _disposed) return false;

            bool hasNode = ch.Current.IsValid;
            string source = !hasNode
                ? (ch.IsBase && _layers.DefaultPoseConnected ? "controller" : "none")
                : ch.Current.IsBlend
                    ? $"mixer({ch.Current.InputCount})"                       // 普通混合器节点：报输入片段数
                    : (ch.Current.ClipAsset != null ? ch.Current.ClipAsset.name : "clip");

            // 权重报**混合器实际值**而不是本类记账值：
            // 记账值会把"基础层没落地"伪装成全绿——诊断必须报引擎真值（§11 可诊断）。
            float weight = hasNode ? _layers.GetWeight(ch.CurrentLayer) : 0f;
            float time = hasNode
                ? (ch.Current.IsBlend ? (float)ch.Current.Mixer.GetTime() : (float)ch.Current.Clip.GetTime())
                : 0f;
            float length = hasNode && !ch.Current.IsBlend && ch.Current.ClipAsset != null ? ch.Current.ClipAsset.length : 0f;

            // 速度同样报**引擎真值**（混合器/片段 SetSpeed 后的值）：就地倍率更新（TrySetBlendSpeed）
            // 只动节点不动记账，报记账值会把"倍率没生效"伪装成全绿——与权重同一口径（§11）。
            float speed = hasNode
                ? (ch.Current.IsBlend ? (float)ch.Current.Mixer.GetSpeed() : (float)ch.Current.Clip.GetSpeed())
                : 0f;

            debug = new AnimationChannelDebug(ch.Active, ch.Binding, weight, ch.Tail.IsValid ? ch.TailWeight : 0f,
                time, length, ch.Completion.Loop, speed, source);
            return true;
        }

        // ---- 提交 / 停止 ----

        /// <summary>登记控制器引用到的全部片段（按片段名索引）——控制器存在时的"播放任意 Clip"便利源；
        /// 直 Clip 模型无控制器，片段走 <see cref="RegisterClip"/> 外部登记。</summary>
        private void RegisterControllerClips()
        {
            if (_animator.runtimeAnimatorController == null) return;      // 直 Clip 模型：无便利源
            foreach (var clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;
                _clips[clip.name] = clip;
            }
        }

        /// <summary>外部登记片段（控制器之外的 Clip；key = 绑定字符串）。重复登记覆盖。</summary>
        public void RegisterClip(string key, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(key) || clip == null) return;
            _clips[key] = clip;
        }

        /// <summary>清单内未能登记的条目计数（键越界防御 + 片段引用缺失——缺包克隆的资源态观测；
        /// 恒 0 = 片源健康）。</summary>
        public int MissingManifestClips { get; private set; }

        /// <summary>
        /// 装配期逐键登记片段清单（去 AC 主路径——§4 片源载体裁决的唯一登记面）：键→Clip 显式引用。
        /// 引用缺失（null）是资源缺失态（缺包克隆）——跳过并计数，不猜、不拦其余键生效。
        /// 与控制器便利源并存时**后注册为权威**（构造期索引先行、本方法覆盖同名键——清单是声明的配置面）。
        /// </summary>
        public void RegisterManifest(AnimationClipManifest manifest)
        {
            if (manifest == null) return;
            for (int i = 0; i < manifest.EntryCount; i++)
            {
                if (!manifest.TryGetEntry(i, out var entry) || entry.Clip == null) { MissingManifestClips++; continue; }
                RegisterClip(entry.Key, entry.Clip);
            }
        }

        /// <summary>
        /// 查询已登记绑定的**片段时长**（秒）——消费方：驱动器装配期派生**换弹播放倍率**
        /// （倍率 = 片段时长 ÷ Sim 换弹时长，使动画收势与弹药回国同帧；见
        /// <c>SlotAnimContext.ReloadPlaybackSpeed</c>）。**时长单一来源 = 片段资产**——不在 Profile/驱动里写时长。
        /// 未知绑定 / 空片段 / 零长 → false（调用方据此显性处理，不猜兜底时长）。
        /// **原生时长**（不受播放倍率影响；倍率由调用方自行相除）。
        /// </summary>
        public bool TryGetClipSeconds(string binding, out float seconds)
        {
            seconds = 0f;
            if (_disposed || string.IsNullOrEmpty(binding)) return false;
            if (!_clips.TryGetValue(binding, out AnimationClip clip) || clip == null) return false;
            seconds = clip.length;
            return seconds > 0f;
        }

        public bool TryPlay(in AnimationResolvedPlayback playback)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(playback.Binding)) return false;

            ChannelState ch = ChannelOf(playback.Channel);
            if (ch == null) return false;                                          // 未知通道：显性拒绝
            if (playback.Channel == AnimationChannel.Overlay && _overlayMask == null)
                return false;                                                      // 无 Mask 不叠加（不静默降级）

            if (!_clips.TryGetValue(playback.Binding, out AnimationClip clip) || clip == null)
            {
                UnknownBindings++;                                                 // §13-4 未知绑定计数
                return false;                                                      // 显性失败，不假装在播
            }

            StartOnChannel(ch, clip, in playback);
            return true;
        }

        /// <summary>
        /// 提交"**同通道多片段按权重混合**"（**普通混合器**路径：<see cref="AnimationMixerPlayable"/>——无 Mask、纯权重）。
        ///
        /// **谁给权重**：调用方（Driver/消费者）——后端不解释业务优先级（§6"业务优先级由 Sim/Driver 解释"）；
        /// 本方法只做三件事：解析绑定、按权重总和归一化、把相位对齐后接进该通道（典型用途：Walk↔Run 按速度连续混合）。
        /// **唯一的混合提交入口**（<see cref="IAnimationBackend.TryPlayBlend"/>）——语义 ID → 绑定的解析归 Profile，
        /// 不存在"绕过播放器直接按名字播"的第二条路径。
        ///
        /// **原子性**（§6"不能只占一半"）：任何一条不合法（通道未知 / Overlay 无 Mask / 条目数 0 或超过
        /// <see cref="MaxBlendInputs"/> / 权重数与条目数不符 / 起点或速度非法 / 任一绑定未知 / 任一权重非有限或为负 /
        /// 权重和为 0）→ **整组拒绝**，通道保持原播放不动、不建任何节点。
        ///
        /// **语义**：混合节点按**循环**对待——**不产生 <c>Completed</c>**（混合集合没有单一结束边界，§5）；
        /// 输入片段共用同一 `speed`、起点按 `startNormalized × 自身时长` 对齐（**长度不同的片段其归一化相位
        /// 会随时间漂移**：需要严格步态同步时应用等长片段，或后续再加"按归一化速率对齐"选项）；
        /// **输入片段的回绕沿用资产自身设置**（不逐输入强制回绕：槽位应当是循环片段——见
        /// <see cref="AnimationBlendDefinition"/>）。
        /// 混合节点与单片段节点可互相打断，走同一套"一个当前 + 至多一条尾部"规则（§12 节点有界）。
        /// </summary>
        public bool TryPlayBlend(in AnimationResolvedBlend blend)
        {
            if (_disposed) return false;
            if (blend.Bindings == null || blend.Weights == null) return false;
            int count = blend.Bindings.Length;
            if (count == 0 || count > MaxBlendInputs || blend.Weights.Length != count) return false;

            ChannelState ch = ChannelOf(blend.Channel);
            if (ch == null) return false;                                          // 未知通道：显性拒绝
            if (blend.Channel == AnimationChannel.Overlay && _overlayMask == null)
                return false;                                                      // 无 Mask 不叠加（不静默降级）
            if (float.IsNaN(blend.StartNormalized) || blend.StartNormalized < 0f || blend.StartNormalized > 1f) return false;
            if (float.IsNaN(blend.Speed) || float.IsInfinity(blend.Speed) || blend.Speed <= 0f) return false;

            // 绑定与权重先**整体校验**（原子：不合法时一个节点都不建、通道不动）
            float weightSum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(blend.Bindings[i])) return false;
                float weight = blend.Weights[i];
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f) return false;
                if (!_clips.TryGetValue(blend.Bindings[i], out AnimationClip clip) || clip == null)
                {
                    UnknownBindings++;                                             // §13-4 未知绑定计数
                    return false;
                }
                weightSum += weight;
            }
            if (weightSum <= 0f) return false;                                     // 全零权重：没有可播的东西

            StartBlendOnChannel(ch, in blend, weightSum);
            return true;
        }

        /// <summary>**就地更新**混合权重（同形态连续调参；节点/相位/句柄都不动）。
        /// 校验：当前必须是混合节点、权重数一致、逐条有限且 ≥0、总和 &gt; 0——任一不满足返回 false 且不改动现状。
        /// 权重按总和归一化（与 <see cref="TryPlayBlend"/> 同一口径：集合内部恒 1，通道层权重另行表达）。</summary>
        public bool TrySetBlendWeights(AnimationChannel channel, float[] weights)
        {
            if (_disposed || weights == null) return false;

            ChannelState ch = ChannelOf(channel);
            if (ch == null || !ch.Current.IsValid || !ch.Current.IsBlend) return false;
            if (weights.Length != ch.Current.InputCount) return false;

            float weightSum = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                float w = weights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f) return false;
                weightSum += w;
            }
            if (weightSum <= 0f) return false;

            for (int i = 0; i < weights.Length; i++)
                ch.Current.Mixer.SetInputWeight(i, weights[i] / weightSum);
            return true;
        }

        /// <summary>**就地更新**混合节点播放倍率（步频同步）：倍率乘在混合器节点上，各输入片段的
        /// 原生 Speed 不动（Playable 速度沿图相乘，等效整体变速）。只对混合节点生效——
        /// 单片段节点的速度在提交时给定，不在此路。</summary>
        public bool TrySetBlendSpeed(AnimationChannel channel, float speedScale)
        {
            if (_disposed) return false;
            if (float.IsNaN(speedScale) || float.IsInfinity(speedScale) || speedScale <= 0f) return false;

            ChannelState ch = ChannelOf(channel);
            if (ch == null || !ch.Current.IsValid || !ch.Current.IsBlend) return false;

            ch.Current.Mixer.SetSpeed(speedScale);
            return true;
        }

        /// <summary>释放通道（幂等）。基础层保持当前帧（无下层可回退，重新提交即恢复推进）；
        /// 上层权重淡出到 0，露出下方基础层（§3"释放即权重淡出"）。
        /// 若此时仍有在途混合尾部，丢弃它即一次确定性截断（§12"截断策略并计数"）——尾部一旦存续
        /// 就说明交叉淡化尚未落位（落位会在 <see cref="ChannelBlendAdvance.Advance"/> 里自行销毁）。</summary>
        public bool TryStop(AnimationChannel channel)
        {
            if (_disposed) return false;
            ChannelState ch = ChannelOf(channel);
            if (ch == null || !ch.Active) return false;

            ch.Active = false;
            ch.Completion.Completed = false;
            ch.Binding = null;

            if (ch.Tail.IsValid) TruncatedBlends++;               // 打断在途尾部 → 计数（§12）

            if (ch.IsBase)
            {
                ch.DestroyTail(_layers);
                if (ch.Current.IsValid) ch.Current.Root.Pause();  // 姿态冻结在当前帧（单片段/混合节点通用）
                return true;
            }

            ch.DestroyTail(_layers);                              // 淡出不再需要旧片段在下层保持
            ch.TargetWeight = 0f;                                 // 权重淡出 → 露出基础层
            return true;
        }

        public bool IsChannelActive(AnimationChannel channel)
        {
            ChannelState ch = ChannelOf(channel);
            return ch != null && ch.Active;
        }

        /// <summary>
        /// 采样推进（唯一驱动入口，§7）：先推进本帧混合权重，再**单次** Evaluate 全图，
        /// 最后逐通道做完成检测（读定义的 Loop——§5），返回自然到达结束边界的通道集合。
        /// </summary>
        public AnimationChannelMask Tick(float deltaSeconds)
        {
            if (_disposed) return AnimationChannelMask.None;

            float dt = deltaSeconds > 0f ? deltaSeconds : 0f;

            ChannelBlendAdvance.Advance(_base, _layers, dt);
            if (_override != null) ChannelBlendAdvance.Advance(_override, _layers, dt);
            if (_overlay != null) ChannelBlendAdvance.Advance(_overlay, _layers, dt);

            _layers.Evaluate(dt);                                  // 每帧恰好一次（多通道也不重复推进时间）

            AnimationChannelMask done = AnimationChannelMask.None;
            if (CheckCompletion(_base)) done |= AnimationChannelMask.Base;
            if (_override != null && CheckCompletion(_override)) done |= AnimationChannelMask.Override;
            if (_overlay != null && CheckCompletion(_overlay)) done |= AnimationChannelMask.Overlay;
            return done;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layers.Destroy();                                      // §9 销毁序的最后引擎步骤
        }

        // ---- 内部：提交（两条路径共用"让位 → 落位 → 接层"三段）----

        /// <summary>单片段提交（<see cref="TryPlay"/> 的落地）：建 Clip 节点并在通道上落位。</summary>
        private void StartOnChannel(ChannelState ch, AnimationClip clip, in AnimationResolvedPlayback playback)
        {
            ChannelNode node = ChannelNode.ClipNode(_layers.Graph, clip, playback.Speed, playback.StartNormalized);
            ch.Completion.Reset(clip.length, playback.Loop,
                playback.StartNormalized > 0f ? clip.length * playback.StartNormalized : 0f);
            CommitNode(ch, node, playback.Binding, playback.Speed);
        }

        /// <summary>同通道多片段混合提交（<see cref="TryPlayBlend"/> 的落地）：建普通混合器节点并落位。</summary>
        private void StartBlendOnChannel(ChannelState ch, in AnimationResolvedBlend blend, float weightSum)
        {
            ChannelNode node = ChannelNode.BlendNode(_layers.Graph, in blend, _clips, weightSum);
            ch.Completion.Reset(0f, loop: true, startTime: 0f);      // 混合节点按循环对待：不做边界判定（§5）
            CommitNode(ch, node, DescribeBlend(in blend), blend.Speed);
        }

        /// <summary>让位（旧当前 → 尾部）→ 落位（新节点成为当前）→ 接层（含基础层权重显式置 1）。</summary>
        private void CommitNode(ChannelState ch, in ChannelNode node, string binding, float speed)
        {
            // 确定性截断（§12）：已有在途尾部 → 立刻销毁，不排第二个尾巴（上限恒为每通道一条）
            if (ch.Tail.IsValid)
            {
                TruncatedBlends++;
                ch.DestroyTail(_layers);
            }

            // 旧当前 → 尾部（每通道恰好一条）：基础层尾部在**上层**淡出 1→0；
            // 上层通道尾部在**下层**以权重 1 保持，由新当前从 0 淡入完成交叉淡化。
            // 槽位换了层，必须先断开原层再连到尾部层（同一输入不能同时连两个源）。
            if (ch.Current.IsValid)
            {
                _layers.Disconnect(ch.CurrentLayer);
                _layers.SetWeight(ch.CurrentLayer, 0f);

                ch.Tail = ch.Current;
                ch.TailWeight = 1f;
                _layers.Connect(ch.TailLayer, ch.Tail.Root);
                _layers.SetWeight(ch.TailLayer, 1f);
            }

            if (ch.IsBase) _layers.ReleaseDefaultPose();               // 第一次基础层播放：控制器默认姿态让位

            ch.Current = node;
            ch.Binding = binding;
            ch.Speed = speed;
            ch.Active = true;

            _layers.Connect(ch.CurrentLayer, ch.Current.Root);
            // 基础层（层 0）必须显式置 1（默认值 0，见 AnimationLayerGraph 注释）；上层通道从 0 淡入（旧片段由尾部在下层保持）
            ch.CurrentWeight = ch.IsBase ? 1f : 0f;
            ch.TargetWeight = 1f;
            _layers.SetWeight(ch.CurrentLayer, ch.IsBase ? 1f : ch.CurrentWeight);
        }

        /// <summary>混合节点的诊断绑定串（`blend:A+B`；≤ <see cref="MaxBlendInputs"/> 段，长度有界）。</summary>
        private static string DescribeBlend(in AnimationResolvedBlend blend)
        {
            string binding = "blend:" + blend.Bindings[0];
            for (int i = 1; i < blend.SlotCount; i++) binding += "+" + blend.Bindings[i];
            return binding;
        }

        // ---- 内部：完成检测（判定逻辑在 Core 的 ClipCompletionTracker，本类只做引擎读写）----

        private static bool CheckCompletion(ChannelState ch)
        {
            if (!ch.Active || ch.Completion.Completed) return false;
            if (!ch.Current.IsValid) return false;
            if (ch.Current.IsBlend) return false;                   // 混合节点按循环对待：无单一结束边界（§5）
            if (ch.Current.ClipAsset == null) return false;

            // 判定归定义（§5）：读 ch.Completion.Loop（定义），不读资产 loop 设置——
            // 定义为循环却资产不循环时手工回绕，定义为一次性却资产循环时按回卷检测。
            ClipTickAction action = ch.Completion.Advance(ch.Current.Clip.GetTime(), TimeEpsilon);
            if (action == ClipTickAction.Wrap) ch.Current.Clip.SetTime(ch.Completion.WrapTarget);
            return action == ClipTickAction.Completed;
        }

        private ChannelState ChannelOf(AnimationChannel channel)
            => channel switch
            {
                AnimationChannel.Base => _base,
                AnimationChannel.Overlay => _overlay,
                AnimationChannel.Override => _override,
                _ => null,
            };
    }
}