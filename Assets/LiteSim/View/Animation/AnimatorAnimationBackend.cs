using System;
using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// Animator 后端（《动画模块专项设计》§7"首版角色后端用 AnimatorControllerPlayable 承载控制器，
    /// PlayableGraph 采用 Manual 更新"）：把已解析的播放方案落到 PlayableGraph，姿态推进只经
    /// <see cref="Tick"/>（Graph.Evaluate——§7"Graph Evaluate 只由一个驱动器调用"）。
    ///
    /// **2026-09-27 分层混合版（三通道）**：在既有 Clip 直驱（可播任意 Clip）之上引入
    /// <see cref="AnimationLayerMixerPlayable"/>——通道叠加/上半身混合落地，同时保持
    /// "不回到控制器参数/Trigger 驱动"（该资产参数全是 Trigger、多层无 Mask，
    /// §7 字面的控制器参数驱动在本资产上不成立，走 §4 允许的「Clip 资源键」分支）。
    ///
    /// **图拓扑（固定 6 输入，反复打断不增长）**：
    /// <code>
    /// _output(source = _mixer)
    /// _mixer = AnimationLayerMixerPlayable.Create(graph, 6)
    ///   [0] locomotion 当前   weight 1（基础层；初值 = 控制器默认姿态）
    ///   [1] locomotion 尾部   weight 1→0   （旧片段在**上层**淡出——交叉淡化= 旧覆盖新再让位）
    ///   [2] fullbody   尾部   weight 1（旧片段**在下层**保持）
    ///   [3] fullbody   当前   weight 0→1   （新片段淡入；无掩码 → 全量覆盖）
    ///   [4] upperbody  尾部   weight 1     + 上半身 LayerMask
    ///   [5] upperbody  当前   weight 0→1   + 上半身 LayerMask
    /// </code>
    /// 基础层（locomotion）的旧片段必须在上层淡出（层 0 的权重被混合器忽略，放下层等于不可见）；
    /// 上层通道反之——新片段在下层之上淡入，旧片段在下层以权重 1 保持，淡化完成即销毁 →
    /// 每通道**恰好一个当前 + 一个尾部**，节点数回基线（§12"反复打断后节点数稳定"）。
    /// **FullBody 不需要"记忆并恢复旧移动动作"**：Locomotion 从未离开层 0、时间持续推进，
    /// FullBody 权重归零即自然回到当前移动姿态（不引 shadow、不恢复过期旧动作——§6）。
    ///
    /// **完成判定归定义（§5）**：读 <see cref="AnimationResolvedPlayback.Loop"/>，**不读资产 loop 设置**——
    /// 定义为循环却资产不循环时手工回绕，定义为一次性却资产循环时按回卷检测；两个方向都成立。
    ///
    /// **能力位诚实声明（§4）**：<c>Looping | StartAtNormalized | SpeedOverride | ClipBlending</c>，
    /// **仅当上半身 Mask 构造成功才追加 <c>LayeredChannels</c>**（非 humanoid 时 UpperBody 被显性拒绝）。
    /// 速度经 <c>SetSpeed</c>；<see cref="Tick"/> 只把已缩放的 delta 交给 Evaluate，不再乘 Speed（§7 归属分离）。
    ///
    /// **普通混合器（同通道多片段按权重混合）**：<see cref="TryPlayBlend"/> 用 `AnimationMixerPlayable`
    /// （无 Mask、纯权重，典型用途 Walk↔Run 按速度连续混合），**且是唯一的混合提交入口**——
    /// 语义 ID → 槽位绑定的解析归 Profile，经 <c>CharacterAnimationPlayer.PlayBlend</c> 进入本方法
    /// （不存在"绕过播放器直接按名字播"的第二条路径）。混合节点与单片段节点共用同一套机制——
    /// 让位到尾部、淡入淡出、回收，见内部 <c>Node</c>；**混合节点按循环对待，不产生 Completed**。
    /// **淡化时长**：由构造函数显式传入（<see cref="DefaultBlendSeconds"/> 只是默认值，**不是**淡化数学里的常量），
    /// 并在构造时分发给每个通道（<c>ChannelState.BlendSeconds</c>）；`0` = 瞬时落位。
    /// </summary>
    public sealed class AnimatorAnimationBackend : IAnimationBackend
    {
        /// <summary>默认淡入/淡出时长（秒）——**只作构造参数的默认值**；淡化数学一律用实例持有的
        /// <see cref="BlendSeconds"/>（构造时显式传入，不从常量读）。§12"混合尾部必须有固定上限"。</summary>
        public const float DefaultBlendSeconds = 0.12f;

        /// <summary>同通道混合（普通混合器）的输入片段数上限——**与 <see cref="AnimationProfile.MaxBlendSlots"/> 同源**
        /// （登记期已挡住越界形态，这里只作执行面的防御与定长数组容量；§12 节点数有界）。</summary>
        public const int MaxBlendInputs = AnimationProfile.MaxBlendSlots;

        /// <summary>完成/回卷判定容差（秒）。</summary>
        private const float TimeEpsilon = 1e-4f;

        // ---- 固定图拓扑（层序即混合序）----
        private const int LayerLocomotionCurrent = 0;
        private const int LayerLocomotionTail = 1;
        private const int LayerFullBodyTail = 2;
        private const int LayerFullBodyCurrent = 3;
        private const int LayerUpperBodyTail = 4;
        private const int LayerUpperBodyCurrent = 5;
        private const int InputCount = 6;

        private readonly Animator _animator;
        private readonly PlayableGraph _graph;
        private readonly AnimationLayerMixerPlayable _mixer;
        private readonly AnimatorControllerPlayable _controller;
        private readonly AnimationPlayableOutput _output;
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        private readonly AvatarMask _upperBodyMask;
        private readonly ChannelState _locomotion;
        private readonly ChannelState _fullBody;
        private readonly ChannelState _upperBody;

        private bool _defaultPoseConnected;      // 基础层仍挂控制器默认姿态（开局不露 T-pose）
        private bool _disposed;

        /// <summary>本后端的淡入/淡出时长（秒；**构造时显式传入**，0 = 瞬时落位）。
        /// 已分发给每个通道（<c>ChannelState.BlendSeconds</c>），淡化数学不读任何常量。</summary>
        public float BlendSeconds { get; }

        /// <param name="animator">视图实例上的 Animator（必须已挂 RuntimeAnimatorController——缺控制器的
        /// 灰盒视图不建后端，由驱动层跳过，表现为无动画而非报错）。</param>
        /// <param name="blendSeconds">淡入/淡出时长（秒）：**当作必填对待**——默认值只是常见手感的兜底，
        /// 调用方应显式给（0 = 瞬时落位，测试与"直接切"用）。该值在构造时分发给每个通道，
        /// 每个通道的淡化数学只用自己的那一份。</param>
        public AnimatorAnimationBackend(Animator animator, float blendSeconds = DefaultBlendSeconds)
        {
            _animator = animator ?? throw new ArgumentNullException(nameof(animator));
            if (_animator.runtimeAnimatorController == null)
                throw new ArgumentException("Animator 缺少 RuntimeAnimatorController——无法建动画后端", nameof(animator));
            if (float.IsNaN(blendSeconds) || float.IsInfinity(blendSeconds) || blendSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(blendSeconds), blendSeconds,
                    "混合时长必须 ≥ 0 且有限（0 = 瞬时落位）");
            BlendSeconds = blendSeconds;

            _graph = PlayableGraph.Create("CharacterAnimation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);          // 只由本类 Tick 推进（§7）
            _controller = AnimatorControllerPlayable.Create(_graph, _animator.runtimeAnimatorController);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, InputCount);
            _output = AnimationPlayableOutput.Create(_graph, "CharacterAnim", _animator);
            _output.SetSourcePlayable(_mixer);
            _graph.Play();

            RegisterControllerClips();

            _locomotion = new ChannelState(AnimationChannel.Locomotion, isBase: true, LayerLocomotionCurrent, LayerLocomotionTail, BlendSeconds);
            _fullBody = new ChannelState(AnimationChannel.FullBody, isBase: false, LayerFullBodyCurrent, LayerFullBodyTail, BlendSeconds);
            _upperBody = new ChannelState(AnimationChannel.UpperBody, isBase: false, LayerUpperBodyCurrent, LayerUpperBodyTail, BlendSeconds);

            _upperBodyMask = UpperBodyMaskFactory.TryBuild(_animator.avatar);   // 纯函数，见 UpperBodyMaskFactory
            if (_upperBodyMask != null)
            {
                _mixer.SetLayerMaskFromAvatarMask((uint)LayerUpperBodyCurrent, _upperBodyMask);
                _mixer.SetLayerMaskFromAvatarMask((uint)LayerUpperBodyTail, _upperBodyMask);
            }

            // 层 0 的输入权重**默认为 0，必须显式置 1**：Unity 2022.3 实测——多层混合器上
            // "层 0 权重恒 1、不可改"并不成立（不设时 GetInputWeight(0) 读回 0，基础层整体不输出，
            // 表现为"通道在播、时间在走，角色姿态纹丝不动"）。未连接的其余层不需要占位。
            _mixer.SetInputWeight(LayerLocomotionCurrent, 1f);
            for (int i = 1; i < InputCount; i++) _mixer.SetInputWeight(i, 0f);

            // 开局默认姿态：控制器 playable 占基础层（首帧不露 T-pose）；第一次播放后让位给片段
            _mixer.ConnectInput(LayerLocomotionCurrent, _controller, 0);
            _defaultPoseConnected = true;
        }

        // ---- 能力位（§4 诚实声明，不静默降级）----

        public AnimationBackendCapabilities Capabilities
            => AnimationBackendCapabilities.Looping
             | AnimationBackendCapabilities.StartAtNormalized
             | AnimationBackendCapabilities.SpeedOverride
             | AnimationBackendCapabilities.ClipBlending                                  // 普通混合器路径（TryPlayBlend）
             | (_upperBodyMask != null ? AnimationBackendCapabilities.LayeredChannels : AnimationBackendCapabilities.None);

        // ---- 诊断（§11；零分配，调用方复用容器）----

        /// <summary>未知绑定被拒次数（§13-4；后端不认识该片段名）。</summary>
        public int UnknownBindings { get; private set; }

        /// <summary>混合尾部被确定性截断的次数（§12"确定截断策略"——新请求打断在途尾部）。</summary>
        public int TruncatedBlends { get; private set; }

        /// <summary>当前占用中的通道数（0..3）。</summary>
        public int ActiveChannels
        {
            get
            {
                int n = 0;
                if (_locomotion.Active) n++;
                if (_fullBody.Active) n++;
                if (_upperBody.Active) n++;
                return n;
            }
        }

        /// <summary>图内 playable 节点数（§12 节点稳定用例的观测量）。</summary>
        public int PlayableCount => _graph.IsValid() ? _graph.GetPlayableCount() : 0;

        /// <summary>上半身 LayerMask 是否可用（false = 非 humanoid，UpperBody 通道不可用）。</summary>
        public bool HasUpperBodyMask => _upperBodyMask != null;

        /// <summary>单通道诊断读数（纯值，零分配）。</summary>
        public bool TryGetChannelDebug(AnimationChannel channel, out AnimationChannelDebug debug)
        {
            debug = default;
            ChannelState ch = ChannelOf(channel);
            if (ch == null || _disposed) return false;

            bool hasNode = ch.Current.IsValid;
            string source = !hasNode
                ? (ch.IsBase && _defaultPoseConnected ? "controller" : "none")
                : ch.Current.IsBlend
                    ? $"mixer({ch.Current.InputCount})"                       // 普通混合器节点：报输入片段数
                    : (ch.Current.ClipAsset != null ? ch.Current.ClipAsset.name : "clip");

            // 权重报**混合器实际值**而不是本类记账值：2026-09-27 实测缺陷里记账值恒 1、层 0 实际为 0，
            // 记账值会把"基础层没落地"伪装成全绿——诊断必须报引擎真值（§11 可诊断）。
            float weight = hasNode ? _mixer.GetInputWeight(ch.CurrentLayer) : 0f;
            float time = hasNode
                ? (ch.Current.IsBlend ? (float)ch.Current.Mixer.GetTime() : (float)ch.Current.Clip.GetTime())
                : 0f;
            float length = hasNode && !ch.Current.IsBlend && ch.Current.ClipAsset != null ? ch.Current.ClipAsset.length : 0f;

            debug = new AnimationChannelDebug(ch.Active, ch.Binding, weight, ch.Tail.IsValid ? ch.TailWeight : 0f,
                time, length, ch.Completion.Loop, ch.Speed, source);
            return true;
        }

        // ---- 提交 / 停止 ----

        /// <summary>登记控制器引用到的全部片段（按片段名索引）——"播放任意 Clip"的基本盘：
        /// 控制器用到的片段无需外部加载即可直驱。</summary>
        private void RegisterControllerClips()
        {
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

        public bool TryPlay(in AnimationResolvedPlayback playback)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(playback.Binding)) return false;

            ChannelState ch = ChannelOf(playback.Channel);
            if (ch == null) return false;                                          // 未知通道：显性拒绝
            if (playback.Channel == AnimationChannel.UpperBody && _upperBodyMask == null)
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
        /// **原子性**（§6"不能只占一半"）：任何一条不合法（通道未知 / UpperBody 无 Mask / 条目数 0 或超过
        /// <see cref="MaxBlendInputs"/> / 权重数与条目数不符 / 起点或速度非法 / 任一绑定未知 / 任一权重非有限或为负 /
        /// 权重和为 0）→ **整组拒绝**，通道保持原播放不动、不建任何节点。
        ///
        /// **语义**：混合节点按**循环**对待——**不产生 <c>Completed</c>**（混合集合没有单一结束边界，§5）；
        /// 输入片段共用同一 `speed`、起点按 `startNormalized × 自身时长` 对齐（**长度不同的片段其归一化相位
        /// 会随时间漂移**：需要严格步态同步时应用等长片段，或后续再加"按归一化速率对齐"选项）；
        /// **输入片段的回绕沿用资产自身设置**（首版不逐输入强制回绕：槽位应当是循环片段——见
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
            if (blend.Channel == AnimationChannel.UpperBody && _upperBodyMask == null)
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

        /// <summary>释放通道（幂等）。基础层保持当前帧（无下层可回退，重新提交即恢复推进）；
        /// 上层权重淡出到 0，露出下方 Locomotion（§3"释放即权重淡出"）。
        /// 若此时仍有在途混合尾部，丢弃它即一次确定性截断（§12"截断策略并计数"）——尾部一旦存续
        /// 就说明交叉淡化尚未落位（落位会在 <see cref="AdvanceBlend"/> 里自行销毁）。</summary>
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
                DestroyTail(ch);
                if (ch.Current.IsValid) ch.Current.Root.Pause();  // 姿态冻结在当前帧（单片段/混合节点通用）
                return true;
            }

            DestroyTail(ch);                                      // 淡出不再需要旧片段在下层保持
            ch.TargetWeight = 0f;                                 // 权重淡出 → 露出 Locomotion
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

            AdvanceBlend(_locomotion, dt);
            AdvanceBlend(_fullBody, dt);
            AdvanceBlend(_upperBody, dt);

            _graph.Evaluate(dt);                                   // 每帧恰好一次（多通道也不重复推进时间）

            AnimationChannelMask done = AnimationChannelMask.None;
            if (CheckCompletion(_locomotion)) done |= AnimationChannelMask.Locomotion;
            if (CheckCompletion(_fullBody)) done |= AnimationChannelMask.FullBody;
            if (CheckCompletion(_upperBody)) done |= AnimationChannelMask.UpperBody;
            return done;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_graph.IsValid()) _graph.Destroy();                 // §9 销毁序的最后引擎步骤
        }

        // ---- 内部：提交（两条路径共用"让位 → 落位 → 接层"三段）----

        /// <summary>单片段提交（<see cref="TryPlay"/> 的落地）：建 Clip 节点并在通道上落位。</summary>
        private void StartOnChannel(ChannelState ch, AnimationClip clip, in AnimationResolvedPlayback playback)
        {
            Node node = Node.ClipNode(_graph, clip, playback.Speed, playback.StartNormalized);
            ch.Completion.Reset(clip.length, playback.Loop,
                playback.StartNormalized > 0f ? clip.length * playback.StartNormalized : 0f);
            CommitNode(ch, node, playback.Binding, playback.Speed);
        }

        /// <summary>同通道多片段混合提交（<see cref="TryPlayBlend"/> 的落地）：建普通混合器节点并落位。</summary>
        private void StartBlendOnChannel(ChannelState ch, in AnimationResolvedBlend blend, float weightSum)
        {
            Node node = Node.BlendNode(_graph, in blend, _clips, weightSum);
            ch.Completion.Reset(0f, loop: true, startTime: 0f);      // 混合节点按循环对待：不做边界判定（§5）
            CommitNode(ch, node, DescribeBlend(in blend), blend.Speed);
        }

        /// <summary>让位（旧当前 → 尾部）→ 落位（新节点成为当前）→ 接层（含基础层权重显式置 1）。</summary>
        private void CommitNode(ChannelState ch, in Node node, string binding, float speed)
        {
            // 确定性截断（§12）：已有在途尾部 → 立刻销毁，不排第二个尾巴（上限恒为每通道一条）
            if (ch.Tail.IsValid)
            {
                TruncatedBlends++;
                DestroyTail(ch);
            }

            // 旧当前 → 尾部（每通道恰好一条）：基础层尾部在**上层**淡出 1→0；
            // 上层通道尾部在**下层**以权重 1 保持，由新当前从 0 淡入完成交叉淡化。
            // 槽位换了层，必须先断开原层再连到尾部层（同一输入不能同时连两个源）。
            if (ch.Current.IsValid)
            {
                _mixer.DisconnectInput(ch.CurrentLayer);
                _mixer.SetInputWeight(ch.CurrentLayer, 0f);

                ch.Tail = ch.Current;
                ch.TailWeight = 1f;
                _mixer.ConnectInput(ch.TailLayer, ch.Tail.Root, 0);
                _mixer.SetInputWeight(ch.TailLayer, 1f);
            }

            if (ch.IsBase) ReleaseDefaultPose();                       // 第一次基础层播放：控制器默认姿态让位

            ch.Current = node;
            ch.Binding = binding;
            ch.Speed = speed;
            ch.Active = true;

            _mixer.ConnectInput(ch.CurrentLayer, ch.Current.Root, 0);
            // 基础层（层 0）必须显式置 1（默认值 0，见构造函数注释）；上层通道从 0 淡入（旧片段由尾部在下层保持）
            ch.CurrentWeight = ch.IsBase ? 1f : 0f;
            ch.TargetWeight = 1f;
            _mixer.SetInputWeight(ch.CurrentLayer, ch.IsBase ? 1f : ch.CurrentWeight);
        }

        /// <summary>混合节点的诊断绑定串（`blend:A+B`；≤ <see cref="MaxBlendInputs"/> 段，长度有界）。</summary>
        private static string DescribeBlend(in AnimationResolvedBlend blend)
        {
            string binding = "blend:" + blend.Bindings[0];
            for (int i = 1; i < blend.SlotCount; i++) binding += "+" + blend.Bindings[i];
            return binding;
        }

        /// <summary>第一次播放时摘掉控制器默认姿态（其 playable 保留不销毁——节点数恒定，仅不再被采样）。</summary>
        private void ReleaseDefaultPose()
        {
            if (!_defaultPoseConnected) return;
            _mixer.DisconnectInput(LayerLocomotionCurrent);
            _defaultPoseConnected = false;
        }

        // ---- 内部：混合推进 ----

        /// <summary>推进本通道的层权重。时长 **0 = 瞬时落位**（显式传入的语义：测试与"直接切"）——
        /// 不做除法、不读任何常量（`ch.BlendSeconds` 是构造时分发给该通道的显式值）。</summary>
        private void AdvanceBlend(ChannelState ch, float dt)
        {
            if (ch.IsBase)
            {
                if (!ch.Tail.IsValid) return;
                ch.TailWeight = ch.BlendSeconds > 0f
                    ? Mathf.Max(0f, ch.TailWeight - dt / ch.BlendSeconds)
                    : 0f;
                _mixer.SetInputWeight(ch.TailLayer, ch.TailWeight);
                if (ch.TailWeight <= 0f) DestroyTail(ch);           // 淡化完成 → 节点回基线
                return;
            }

            ch.CurrentWeight = ch.BlendSeconds > 0f
                ? Mathf.MoveTowards(ch.CurrentWeight, ch.TargetWeight, dt / ch.BlendSeconds)
                : ch.TargetWeight;
            if (ch.Current.IsValid) _mixer.SetInputWeight(ch.CurrentLayer, ch.CurrentWeight);

            if (ch.CurrentWeight >= 1f) DestroyTail(ch);            // 新节点完全接管：旧的落位销毁

            // 通道已释放且淡出结束：销毁当前，节点回基线
            if (!ch.Active && ch.CurrentWeight <= 0f && ch.Current.IsValid) DestroyCurrent(ch);
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

        // ---- 内部：节点管理（节点所有权只在 DestroyTail/DestroyCurrent/DestroyNode 收口）----

        private void DestroyTail(ChannelState ch)
        {
            if (ch.Tail.IsValid)
            {
                _mixer.DisconnectInput(ch.TailLayer);
                _mixer.SetInputWeight(ch.TailLayer, 0f);
                DestroyNode(ref ch.Tail);
            }
            ch.Tail = default;
            ch.TailWeight = 0f;
        }

        private void DestroyCurrent(ChannelState ch)
        {
            if (ch.Current.IsValid)
            {
                _mixer.DisconnectInput(ch.CurrentLayer);
                _mixer.SetInputWeight(ch.CurrentLayer, 0f);
                DestroyNode(ref ch.Current);
            }
            ch.Current = default;
            ch.CurrentWeight = 0f;
            ch.Completion.Completed = false;
        }

        /// <summary>销毁一个节点：混合节点要**先销毁它的全部输入片段**（Unity 不会替你级联销毁）。</summary>
        private static void DestroyNode(ref Node node)
        {
            if (node.IsValid)
            {
                if (node.IsBlend)
                {
                    for (int i = 0; i < node.InputCount; i++)
                        if (node.Inputs[i].IsValid()) node.Inputs[i].Destroy();
                    node.Mixer.Destroy();
                }
                else
                {
                    node.Clip.Destroy();
                }
            }
            node = default;
        }

        private ChannelState ChannelOf(AnimationChannel channel)
            => channel switch
            {
                AnimationChannel.Locomotion => _locomotion,
                AnimationChannel.UpperBody => _upperBody,
                AnimationChannel.FullBody => _fullBody,
                _ => null,
            };

        /// <summary>
        /// 通道上的一个节点（当前/尾部共用同一表示）：**单片段**（<see cref="AnimationClipPlayable"/>）或
        /// **同通道多片段混合**（普通 <see cref="AnimationMixerPlayable"/>：无 Mask、纯权重）。
        /// 两者都能直接作为分层混合器的输入（<see cref="Root"/>），因此通道机制（让位 / 淡入淡出 / 回收）
        /// 对两种形态一视同仁——这也是"普通混合器"不必再建一套通道状态的原因。
        /// </summary>
        private struct Node
        {
            /// <summary>层混合器的输入源（单片段 = <see cref="Clip"/>；混合 = <see cref="Mixer"/>）。</summary>
            public Playable Root;
            public AnimationClipPlayable Clip;
            /// <summary>单片段节点的资产（时长/完成判据；混合节点为 null）。</summary>
            public AnimationClip ClipAsset;
            public AnimationMixerPlayable Mixer;
            /// <summary>混合节点的输入片段（固定容量 <see cref="MaxBlendInputs"/>；单片段节点不用）。</summary>
            public AnimationClipPlayable[] Inputs;
            public int InputCount;
            public bool IsBlend;

            public bool IsValid => Root.IsValid();

            /// <summary>建单片段节点：足部 IK 关（与 Sim 位移不叠加，后置批）、速度与归一化起点显式给。</summary>
            public static Node ClipNode(PlayableGraph graph, AnimationClip clip, float speed, float startNormalized)
            {
                AnimationClipPlayable cp = AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(false);
                cp.SetSpeed(speed);                                          // 速度归 SetSpeed，Tick 不再乘（§7）
                if (startNormalized > 0f) cp.SetTime(clip.length * startNormalized);   // 起点能力
                return new Node { Root = cp, Clip = cp, ClipAsset = clip };
            }

            /// <summary>建混合节点（普通混合器）：权重按 <paramref name="weightSum"/> 归一化（集合内部总和恒 1，
            /// 通道的层权重另行表达），输入共用同一 speed、起点按各自归一化位置对齐。</summary>
            public static Node BlendNode(PlayableGraph graph, in AnimationResolvedBlend blend,
                Dictionary<string, AnimationClip> clips, float weightSum)
            {
                int count = blend.SlotCount;
                AnimationMixerPlayable mixer = AnimationMixerPlayable.Create(graph, count);
                var inputs = new AnimationClipPlayable[MaxBlendInputs];
                for (int i = 0; i < count; i++)
                {
                    AnimationClip clip = clips[blend.Bindings[i]];
                    AnimationClipPlayable cp = AnimationClipPlayable.Create(graph, clip);
                    cp.SetApplyFootIK(false);
                    cp.SetSpeed(blend.Speed);
                    if (blend.StartNormalized > 0f) cp.SetTime(clip.length * blend.StartNormalized);
                    graph.Connect(cp, 0, mixer, i);
                    mixer.SetInputWeight(i, blend.Weights[i] / weightSum);
                    inputs[i] = cp;
                }
                return new Node { Root = mixer, Mixer = mixer, Inputs = inputs, InputCount = count, IsBlend = true };
            }
        }

        /// <summary>
        /// 单通道运行态（§6"每通道最多一个当前播放"）：当前节点 + 至多一条混合尾部。
        /// 基础层（Locomotion）的尾部在**上层**淡出（1→0）；上层通道的尾部在**下层**保持（权重 1）而
        /// 当前节点从 0 淡入——两种角色共用同一对（当前层 / 尾部层）槽位，节点数恒定。
        /// </summary>
        private sealed class ChannelState
        {
            public readonly AnimationChannel Channel;
            public readonly bool IsBase;
            public readonly int CurrentLayer;
            public readonly int TailLayer;
            /// <summary>本通道的淡入/淡出时长（秒；**构造时显式传入**，0 = 瞬时落位）——淡化数学只用它。</summary>
            public readonly float BlendSeconds;

            public Node Current;
            public Node Tail;

            public string Binding;
            /// <summary>结束边界判定状态（纯逻辑，见 <see cref="ClipCompletionTracker"/>；混合节点不用）。</summary>
            public ClipCompletionTracker Completion;
            public float Speed = 1f;

            public float CurrentWeight;
            public float TargetWeight;
            public float TailWeight;

            public bool Active;

            public ChannelState(AnimationChannel channel, bool isBase, int currentLayer, int tailLayer, float blendSeconds)
            {
                Channel = channel;
                IsBase = isBase;
                CurrentLayer = currentLayer;
                TailLayer = tailLayer;
                BlendSeconds = blendSeconds;
            }
        }
    }
}