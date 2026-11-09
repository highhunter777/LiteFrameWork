using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 分层混合图（《动画模块专项设计》§7"PlayableGraph 采用 Manual 更新"、§12"节点数有界"）： **图拓扑、层序常量、连/断线与层权重的唯一持有者**——后端本体只按通道角色调用它。
    /// </summary>
    internal sealed class AnimationLayerGraph
    {
        // ---- 图拓扑层序常量（层序即混合序；不分层模式只建前 2 层）----。
        public const int BaseCurrent = 0;
        public const int BaseTail = 1;
        public const int OverrideTail = 2;
        public const int OverrideCurrent = 3;
        public const int OverlayTail = 4;
        public const int OverlayCurrent = 5;

        private readonly PlayableGraph _graph;
        private readonly AnimationLayerMixerPlayable _mixer;
        private readonly AnimatorControllerPlayable _controller;
        private readonly AnimationPlayableOutput _output;
        private bool _defaultPoseConnected;      // 基础层仍挂控制器默认姿态（开局不露 T-pose）

        /// <summary>混合器输入数＝构造模式取界：分层 <c>OverlayCurrent + 1</c>（层序常量上界，即 6）；
        /// 不分层 <c>BaseTail + 1</c>（仅基础通道当前+尾部，即 2）——两种模式均有界（§12）。</summary>
        public int InputCount { get; }

        /// <param name="animator">视图实例上的 Animator（**不要求 RuntimeAnimatorController**——
        /// 直 Clip 模型（带片段、无控制器资产）不建默认姿态位：T-pose 风险由驱动层
        /// "机 Start 即提交 MoveBlend(Idle=1)" 的同帧落位兜底；控制器存在时仍占基础层作默认姿态）。
        /// 缺 Animator 的灰盒视图不建后端，由驱动层跳过，表现为无动画而非报错。</param>
        /// <param name="layered">分层＝三通道 6 输入；不分层＝仅基础通道 2 输入（与后端构造模式同源同参）。</param>
        public AnimationLayerGraph(Animator animator, bool layered = true)
        {
            InputCount = layered ? OverlayCurrent + 1 : BaseTail + 1;

            _graph = PlayableGraph.Create("CharacterAnimation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);          // 只由后端 Tick 推进（§7）
            _mixer = AnimationLayerMixerPlayable.Create(_graph, InputCount);
            _output = AnimationPlayableOutput.Create(_graph, "CharacterAnim", animator);
            _output.SetSourcePlayable(_mixer);
            _graph.Play();

            // 层 0 的输入权重**默认为 0，必须显式置 1**（见类注释）；未连接的其余层不需要占位。
            _mixer.SetInputWeight(BaseCurrent, 1f);
            for (int i = 1; i < InputCount; i++) _mixer.SetInputWeight(i, 0f);

            // 开局默认姿态（可选，仅控制器存在时）：控制器 playable 占基础层（首帧不露 T-pose），
            // 第一次播放后让位给片段；直 Clip 模型无控制器——基础层空起，由驱动层机 Start 即时提交兜底。
            if (animator.runtimeAnimatorController != null)
            {
                _controller = AnimatorControllerPlayable.Create(_graph, animator.runtimeAnimatorController);
                _mixer.ConnectInput(BaseCurrent, _controller, 0);
                _defaultPoseConnected = true;
            }
        }

        /// <summary>基础层是否仍挂控制器默认姿态（诊断：无节点时 source 报 "controller"）。</summary>
        public bool DefaultPoseConnected => _defaultPoseConnected;

        /// <summary>图本体（节点创建口——<see cref="ChannelNode"/> 的两个工厂都建在这张图上）。</summary>
        public PlayableGraph Graph => _graph;

        /// <summary>把某层的叠加层 LayerMask 挂上（非 humanoid 没有 Mask，调用方据此不声明 <see cref="AnimationBackendCapabilities.OverlayChannel"/>——分层模式专属，不分层无叠加层）。</summary>
        public void ApplyOverlayMask(int layer, AvatarMask mask)
            => _mixer.SetLayerMaskFromAvatarMask((uint)layer, mask);

        public void Connect(int layer, Playable source) => _mixer.ConnectInput(layer, source, 0);

        public void Disconnect(int layer) => _mixer.DisconnectInput(layer);

        public void SetWeight(int layer, float weight) => _mixer.SetInputWeight(layer, weight);

        /// <summary>读**混合器实际输入权重**（引擎真值；诊断必须报真值，记账值会把"层没落地"伪装成全绿）。</summary>
        public float GetWeight(int layer) => _mixer.GetInputWeight(layer);

        /// <summary>第一次基础层播放时摘掉控制器默认姿态（其 playable 保留不销毁——节点数恒定，仅不再被采样）。</summary>
        public void ReleaseDefaultPose()
        {
            if (!_defaultPoseConnected) return;
            _mixer.DisconnectInput(BaseCurrent);
            _defaultPoseConnected = false;
        }

        /// <summary>图内 playable 节点数（§12 节点稳定用例的观测量）。</summary>
        public int PlayableCount => _graph.IsValid() ? _graph.GetPlayableCount() : 0;

        /// <summary>单次 Evaluate（**每帧恰好一次**——多通道也不重复推进时间）。</summary>
        public void Evaluate(float deltaSeconds) => _graph.Evaluate(deltaSeconds);

        /// <summary>§9 销毁序的最后引擎步骤（幂等：图已销毁时不再碰）。</summary>
        public void Destroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}