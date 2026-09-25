using System;
using LiteFramework;
using LiteFramework.Animation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// Animator 后端（《动画模块专项设计》§7"首版角色后端用 AnimatorControllerPlayable 承载控制器，
    /// PlayableGraph 采用 Manual 更新"）：把已解析的播放方案（<see cref="AnimationResolvedPlayback.Binding"/>
    /// = 控制器状态名）落到 <see cref="AnimatorControllerPlayable"/>，姿态推进只经
    /// <see cref="Tick"/>（Graph.Evaluate——§7"Graph Evaluate 只由一个驱动器调用"）。
    ///
    /// **首版能力边界（如实声明，不静默降级，§4）**：
    /// - 只支持 <see cref="AnimationChannel.Locomotion"/>（控制器第 0 层）；上半身叠加/多通道归 G2 垂直切片
    ///   （播放器按 <see cref="AnimationBackendCapabilities"/> 缺 LayeredChannels 自动拒绝 UpperBody/FullBody）；
    /// - 不支持起点/速度倍率（不声明对应能力，播放器拒绝而非假装支持）；
    /// - 循环状态（Idle/Walk/Run）不产生 Completed；一次性状态（Hit/Die 等）到结束边界即 Completed 恰好一次。
    ///
    /// 状态存在性经 <see cref="Animator.HasState"/> 校验——未知绑定返回 false（后端执行失败必须回报，
    /// 不能静默留在旧姿态假装成功）。
    /// </summary>
    public sealed class AnimatorAnimationBackend : IAnimationBackend
    {
        private readonly Animator _animator;
        private readonly PlayableGraph _graph;
        private readonly AnimatorControllerPlayable _controller;

        private bool _active;                        // Locomotion 通道是否有已提交播放
        private int _currentHash;                    // 当前播放的状态短名哈希（完成检测比对用）
        private bool _completed;                     // 一次性状态完成只回报一次

        public AnimationBackendCapabilities Capabilities => AnimationBackendCapabilities.Looping;

        /// <param name="animator">视图实例上的 Animator（必须已挂 RuntimeAnimatorController——缺控制器的
        /// 灰盒视图不建后端，由驱动层跳过，表现为无动画而非报错）。</param>
        public AnimatorAnimationBackend(Animator animator)
        {
            _animator = animator ?? throw new ArgumentNullException(nameof(animator));
            if (_animator.runtimeAnimatorController == null)
                throw new ArgumentException("Animator 缺少 RuntimeAnimatorController——无法建动画后端", nameof(animator));

            _graph = PlayableGraph.Create();
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);          // 只由本类 Tick 推进（§7）
            _controller = AnimatorControllerPlayable.Create(_graph, _animator.runtimeAnimatorController);
            var output = AnimationPlayableOutput.Create(_graph, "CharacterAnim", _animator);
            output.SetSourcePlayable(_controller);
            _graph.Play();
        }

        public bool TryPlay(in AnimationResolvedPlayback playback)
        {
            if (playback.Channel != AnimationChannel.Locomotion) return false;    // 首版只有 Locomotion（层 0）
            if (string.IsNullOrEmpty(playback.Binding)) return false;

            int hash = Animator.StringToHash(playback.Binding);
            if (!_animator.HasState(0, hash)) return false;                      // 未知状态：显性失败

            _controller.Play(hash, 0);
            _currentHash = hash;
            _active = true;
            _completed = false;
            return true;
        }

        public bool TryStop(AnimationChannel channel)
        {
            if (channel != AnimationChannel.Locomotion || !_active) return false;

            // 停止 = 通道释放（姿态冻结在当前帧）——重新提交新播放时 Play 恢复推进
            _controller.Pause();
            _active = false;
            _currentHash = 0;
            return true;
        }

        public bool IsChannelActive(AnimationChannel channel)
            => channel == AnimationChannel.Locomotion && _active;

        /// <summary>
        /// 采样推进：先 Evaluate（含眨眼层等控制器内全部逻辑），再做完成检测。
        /// 返回 true = 当前播放自然到达结束边界（一次性状态恰好一次）。
        /// </summary>
        public bool Tick(AnimationChannel channel, float deltaSeconds)
        {
            _graph.Evaluate(deltaSeconds > 0f ? deltaSeconds : 0f);

            if (!_active || _completed) return false;
            if (channel != AnimationChannel.Locomotion) return false;

            AnimatorStateInfo info = _controller.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash != _currentHash) return false;               // 尚在切换中（Play 即时切换，正常首帧即命中）
            if (info.loop) return false;                                          // 循环状态不产生 Completed（§5）

            if (info.normalizedTime >= 1f)
            {
                _completed = true;                                                // 恰好一次：之后本通道不再重复回报
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
