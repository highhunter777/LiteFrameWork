using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 视图侧动画绑定组件（《动画模块专项设计》§4/§7 直 Clip 形态的视图入口）：**可动画视图的唯一挂件**，
    /// 也是**完整动画配置面**——挂上本组件即自动补 <see cref="Animator"/>（<c>RequireComponent</c>），
    /// 引擎绑定与片源全部在本组件上配置（"只挂这一个组件"），并单向下发到 Animator；
    /// 驱动侧（<see cref="CharacterLocomotionDriver"/>）**只认本组件**——无本组件的视图即灰盒
    /// （裸 Animator 不构成"可动画视图"的判据）。
    ///
    /// **配置归属**：Animator 的一切设置以本组件为**唯一配置源**（单向下发，编辑期 OnValidate 与
    /// 运行期 OnEnable 各生效一次）——不要直接改 Animator 侧（会被本组件按面板值改回）。
    /// - <see cref="Manifest"/>：片段清单（键→Clip 显式引用——去 AC 主片源；键单源在 Profile）；
    /// - <see cref="Controller"/>：可选便利源（存在时后端占图基础层作开局默认姿态 + 片段自动按名索引；
    ///   直 Clip 模型保持空——首帧由驱动机 Start 同帧落位兜底）；
    /// - Avatar/ApplyRootMotion/UpdateMode/CullingMode：Animator 引擎参数的配置面。
    ///
    /// **职责边界**（排除项）：不持图、不建后端——PlayableGraph 归驱动/后端 C# 层。
    ///
    /// **结构性防复发**：Animator 因 <c>RequireComponent</c> 在本组件在场时不可被单独删除——
    /// 要移除动画能力必须先删本组件（有名字、可审计的显式操作），杜绝"匿名组件被顺手剥除、
    /// 视图静默灰盒"的事故形态。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class LiteAnimator : MonoBehaviour
    {
        /// <summary>片段清单（我们的资产槽——键→Clip 显式引用的纯配置）。驱动装配期逐键登记供片；
        /// 键单源在 <c>CombatGirlsAnimationProfile</c>（改绑定键后重跑清单构建器同步重建）。</summary>
        [SerializeField] private AnimationClipManifest manifest;

        /// <summary>可选控制器（便利源：开局默认姿态位 + 片段名自动索引）。直 Clip 模型保持空。
        /// 局部例外语义（视图私有、单实例、不需热更换的小物件）仍可用控制器承载。</summary>
        [Header("可选Controller")]
        [SerializeField] private RuntimeAnimatorController controller;

        /// <summary>Avatar（humanoid 重定向源）。可空——非人形模型合法（无 Avatar ⇒ 后端不声明叠加层
        /// 能力位（<c>OverlayChannel</c>），Overlay 通道显性拒绝；覆盖层（Override）不需 Mask——
        /// Base/Override 任何 rig 照常直驱）。</summary>
        [SerializeField] private Avatar avatar;

        /// <summary>是否应用根运动（对局实体的权威位移归 Sim——联机角色保持关，见 §8）。</summary>
        [SerializeField] private bool applyRootMotion;

        /// <summary>更新模式（Manual Graph 由驱动单点推进——保持 Normal；AnimatePhysics/Unscaled
        /// 语义随模型形态需要时在此配置）。</summary>
        [SerializeField] private AnimatorUpdateMode updateMode = AnimatorUpdateMode.Normal;

        /// <summary>裁剪模式。默认 <see cref="AnimatorCullingMode.AlwaysAnimate"/>——插值位置/俯视角
        /// 形态下视锥外也须推进（远离镜头的实体照常插值行走）；可见性裁剪类优化走驱动层降频
        /// （§12），不走引擎裁剪。</summary>
        [SerializeField] private AnimatorCullingMode cullingMode = AnimatorCullingMode.AlwaysAnimate;

        private Animator _animator;

        /// <summary>本视图的引擎输出目标（RequireComponent 保证在场；懒取缓存）。</summary>
        public Animator Animator
        {
            get
            {
                if (_animator == null) _animator = GetComponent<Animator>();
                return _animator;
            }
        }

        /// <summary>本视图声明的片段清单（驱动装配期优先于此引用供片——视图自含；
        /// 装配层显式装载的清单作为回退）。</summary>
        public AnimationClipManifest Manifest => manifest;

        /// <summary>本视图声明的可选控制器（便利源；诊断/校验用读面）。</summary>
        public RuntimeAnimatorController Controller => controller;

        /// <summary>本视图声明的 Avatar（诊断/校验用读面）。</summary>
        public Avatar Avatar => avatar;

        private void OnEnable() => ApplySettings();

#if UNITY_EDITOR
        /// <summary>编辑期同步：组件面板改动即时落到 Animator（prefab 资产侧所见即所得；
        /// 自动补的 Animator 默认值由本组件首次编辑即写正）。</summary>
        private void OnValidate()
        {
            _animator = null;                                       // 层级变更后重取（防御性）
            ApplySettings();
        }
#endif

        /// <summary>把面板配置单向下发到 Animator（编辑期与运行期共用；幂等）。
        /// **Animator 是引擎输出目标，不是配置源**——两处漂移的裁决：面板为准。</summary>
        private void ApplySettings()
        {
            var animator = Animator;
            if (animator == null) return;                            // 防御：RequireComponent 语义外的异常层级
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = applyRootMotion;
            animator.updateMode = updateMode;
            animator.cullingMode = cullingMode;
        }
    }
}
