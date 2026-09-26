using LiteFramework;
using LiteSim.View;
using UnityEngine;
using Cinemachine;

namespace LiteGame
{
    /// <summary>
    /// Cinemachine 相机服务（<see cref="ICameraService"/> 的 Cinemachine 实现，
    /// 《联机战斗演示专项设计》§3"相机跟本地预测位置；和解期间跟衰减后的表现位置"）。
    ///
    /// **职责只有一个：把表现层每帧给出的位置喂给虚拟相机。** 相机怎么摆（俯角/距离/FOV/阻尼/
    /// 跟随偏移/优先级/Brain）全部是**场景与预制配置**，由美术/关卡在 Inspector 里设定并随场景入库
    /// ——本类**不写任何档位常量**，也不改 vcam 的 Lens/Transposer/Priority。理由（《客户端总设计》
    /// §12.2 视觉单一来源的同一条判据）：相机构图是产品表现决策，用代码再表达一遍就有了第二处事实源，
    /// 调完编辑器发现"改了没用"是最难查的一类分歧。
    ///
    /// **跟随目标**：优先用虚拟相机 Inspector 里已设的 <c>Follow</c>（那样目标可以带自己的
    /// 阻尼/偏移配置）；未设时才自建一个空的 <c>[CameraFocus]</c> 并记账所有权，关闭时一并销毁。
    /// 两种情况下相机跟随的都只是**位置**——表现位置是 SimView 每帧算出的平滑量，没有对应实体
    /// Transform（实体视图可能还没建/在池里），中间目标是最小接缝。
    ///
    /// **本服务不创建虚拟相机**：场景里没有配置好的 vcam 就是装配缺口，由调用方显性处理
    /// （<see cref="TryCreateFromScene"/> 返回 false 并给出原因），不在运行期凭空造一台
    /// ——否则"美术调好的相机没生效"会被自己造的那台掩盖。
    /// </summary>
    public sealed class CinemachineCameraService : ICameraService
    {
        private readonly CinemachineVirtualCamera _vcam;
        private readonly Transform _target;
        private readonly bool _ownsTarget;      // 自建的焦点目标才销毁；场景/预制里的不动
        private readonly Transform _followAtStart;   // 接线前的 Follow（关闭时还原，避免留下指向已销毁目标的引用）
        private bool _hasFocus;
        private bool _shutdown;

        public bool HasFocus => _hasFocus;

        public Vector3 Focus { get; private set; }

        /// <summary>本服务接管的虚拟相机（诊断用）。</summary>
        public CinemachineVirtualCamera VirtualCamera => _vcam;

        /// <param name="virtualCamera">场景中已配置好的虚拟相机（**不**由本类创建、不销毁）。</param>
        /// <param name="followTarget">跟随目标；null = 用 vcam 已设的 Follow，仍未设则自建空目标。</param>
        public CinemachineCameraService(CinemachineVirtualCamera virtualCamera, Transform followTarget = null)
        {
            _vcam = virtualCamera != null
                ? virtualCamera
                : throw new System.ArgumentNullException(nameof(virtualCamera));

            _followAtStart = _vcam.Follow;

            if (followTarget != null)
            {
                _target = followTarget;
                _ownsTarget = false;
            }
            else if (_vcam.Follow != null)
            {
                _target = _vcam.Follow;          // 尊重场景配置：目标可自带偏移/阻尼组件
                _ownsTarget = false;
            }
            else
            {
                var go = new GameObject("[CameraFocus]");
                _target = go.transform;
                _ownsTarget = true;
            }

            _vcam.Follow = _target;
            if (_vcam.LookAt == null) _vcam.LookAt = _target;   // 已配置的 LookAt 不覆盖（可能是独立的看向目标）
        }

        /// <summary>
        /// 从场景解析一台配置好的虚拟相机并建服务（装配点用）。
        /// 返回 false = 场景里没有 vcam（装配缺口）——**不在这里造一台**，由调用方决定降级还是报错。
        /// </summary>
        public static bool TryCreateFromScene(out CinemachineCameraService service, out string reason,
            Transform followTarget = null)
        {
            service = null;
            CinemachineVirtualCamera vcam = UnityEngine.Object.FindAnyObjectByType<CinemachineVirtualCamera>();
            if (vcam == null)
            {
                reason = "场景里没有 CinemachineVirtualCamera（相机配置归场景/预制，不由代码自建）";
                return false;
            }

            service = new CinemachineCameraService(vcam, followTarget);
            reason = null;
            return true;
        }

        public void Follow(in Vector3 target, float deltaSeconds)
        {
            if (_shutdown || _target == null) return;

            Focus = target;                      // 表现空间的目标位置（诊断/HUD 用）
            _target.position = Focus;            // 阻尼/平滑在 Cinemachine 侧按 vcam 配置生效
            _hasFocus = true;
        }

        public void Reset()
        {
            _hasFocus = false;
            Focus = Vector3.zero;
        }

        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            if (_vcam != null) _vcam.Follow = _followAtStart;   // 还原接线前的引用（本服务不改场景语义）
            if (_ownsTarget && _target != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_target.gameObject);
                else UnityEngine.Object.DestroyImmediate(_target.gameObject);
            }
        }
    }
}
