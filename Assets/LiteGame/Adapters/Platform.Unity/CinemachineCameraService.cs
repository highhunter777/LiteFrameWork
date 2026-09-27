using LiteFramework;
using LiteSim.View;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    /// **场景切换后的重新解析（本类的关键行为）**：虚拟相机是**场景对象**，随场景加载/卸载而生灭
    /// （Single 模式切场景后，启动场景里的 vcam 会被一并销毁）。因此本类在每次
    /// <see cref="Follow"/> 时做一次**廉价有效性检查**，发现接管的 vcam 所在场景已失效就重新解析并
    /// 重新接线。这样"启动场景配 vcam""训练场配 vcam""叠加加载"三种形态都能工作，不需要调用方
    /// 在切场景后记得手动重建服务（那种"记得调"的约定迟早会漏）。
    ///
    /// **本服务不创建虚拟相机**：解析不到就是装配缺口，如实回报
    /// （<see cref="LastResolveReason"/> / <see cref="HasCamera"/>），由调用方决定降级还是报错；
    /// 不在运行期凭空造一台——否则"美术调好的相机没生效"会被自己造的那台掩盖。
    /// </summary>
    public sealed class CinemachineCameraService : ICameraService
    {
        private CinemachineVirtualCamera _vcam;
        private Transform _target;               // 当前接线用的跟随目标
        private bool _ownsTarget;                // 自建的空目标才销毁；场景里的不动
        private Transform _followAtBind;         // 接线前的 vcam.Follow（解绑时还原，不留指向已销毁对象的引用）
        private bool _hasFocus;
        private bool _shutdown;
        private string _lastResolveReason;

        /// <summary>装配点指定的跟随目标（null = 用 vcam 自己配的 Follow，再没有就自建空目标）。</summary>
        private readonly Transform _preferredTarget;

        public bool HasFocus => _hasFocus;

        public Vector3 Focus { get; private set; }

        /// <summary>当前是否已解析到可用的虚拟相机（未解析到 = <see cref="Follow"/> 为空操作）。</summary>
        public bool HasCamera => _vcam != null;

        /// <summary>
        /// 确保已接线一台虚拟相机并回报结果（**按需解析**）。
        /// 与 <see cref="HasCamera"/> 的区别：属性只反映**当前**状态，本方法在未接线时先尝试解析一次。
        /// 调用方在"计划使用相机、但还没进过 <see cref="Follow"/>"的时刻用（例如开打前的就绪检查
        /// ——那时视图还没建立，<see cref="Follow"/> 一次都没被调过，只读属性必然是 false）。
        /// </summary>
        public bool EnsureCamera()
        {
            if (_vcam == null || ReferenceEquals(_vcam, null)) Resolve();
            return _vcam != null;
        }

        /// <summary>最近一次解析的结果说明（解析成功 = null；失败 = 原因）。诊断/装配检查用。</summary>
        public string LastResolveReason => _lastResolveReason;

        /// <summary>已解析的虚拟相机所在场景名（诊断用；未解析 = null）。</summary>
        public string CameraSceneName => _vcam != null ? _vcam.gameObject.scene.name : null;

        /// <summary>
        /// 当前接线 vcam 的**渲染相机**（Cinemachine brain 所在 Camera；未解析到 = null）。
        /// 消费者：输入设备源（<c>NewInputIntentSource.SetAimCamera</c>）——叠加开发形态下 boot 相机
        /// 不随场景销毁，`Camera.main` 回落会拿错机，必须显式注入真对局渲染相机。
        /// 解析顺序：vcam 同物体或父链上的 brain → 全场 brain 中 Live 的一台 → null（如实回报）。
        /// </summary>
        public Camera TryGetRenderCamera()
        {
            if (_vcam == null || ReferenceEquals(_vcam, null)) return null;

            CinemachineBrain own = _vcam.GetComponentInParent<CinemachineBrain>();
            if (own != null) return own.OutputCamera;

            var brains = UnityEngine.Object.FindObjectsByType<CinemachineBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i] != null && brains[i].IsLive(_vcam)) return brains[i].OutputCamera;
            }
            return brains != null && brains.Length > 0 ? brains[0].OutputCamera : null;
        }

        /// <param name="followTarget">跟随目标；null = 用 vcam 已设的 Follow，仍未设则自建空目标。
        /// **只在解析时生效**（重新解析后沿用同一个偏好）。</param>
        public CinemachineCameraService(Transform followTarget = null)
        {
            _preferredTarget = followTarget;
            Resolve();

            // **场景加载完成即重新解析**：启动场景通常不带 vcam（引导场景只放引导件），
            // 玩法场景才带——没有这个订阅，服务会一直停在"启动场景没 vcam"的结论上，
            // 即使玩法场景已经加载完（实测就是这么暴露的：场景切换成功了，相机却始终没接线）。
            // 订阅而不是"要求调用方在切场景后记得重建服务"：后者迟早会漏，且漏了表现为画面不动。
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_shutdown) return;
            // 只在"当前没接线"时重解析：已经接上有效 vcam 就不动它（叠加场景加载不该打断正在用的相机）。
            if (!IsBoundCameraAlive()) Resolve();
        }

        /// <summary>
        /// 解析并接线一台虚拟相机（构造时调一次；此后由 <see cref="Follow"/> 在检测到失效时自动重解析）。
        /// 解析规则：全场景中**优先级最高**的启用 vcam——多台 vcam 并存时（如每场景各配一台、
        /// 或叠加场景各带一台）由优先级决定接哪台，与 Cinemachine 自身的选机口径一致。
        /// </summary>
        private void Resolve()
        {
            Unbind();

            CinemachineVirtualCamera best = null;
            var all = UnityEngine.Object.FindObjectsByType<CinemachineVirtualCamera>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].gameObject.activeInHierarchy) continue;
                if (best == null || all[i].Priority > best.Priority) best = all[i];
            }

            if (best == null)
            {
                _lastResolveReason = "场景里没有启用的 CinemachineVirtualCamera"
                    + "（相机配置归场景/预制，不由代码自建）";
                return;
            }

            _vcam = best;
            _followAtBind = _vcam.Follow;

            if (_preferredTarget != null)
            {
                _target = _preferredTarget;
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

            _lastResolveReason = null;
            Log.Info($"[Camera] 已接线 vcam「{_vcam.name}」(scene={_vcam.gameObject.scene.name}, priority={_vcam.Priority})", "Camera");
        }

        /// <summary>解除当前接线（销毁自建目标、还原 vcam 的 Follow 引用）。</summary>
        private void Unbind()
        {
            if (_vcam != null)
            {
                // vcam 可能已随场景销毁（Unity 的伪 null）——用 ReferenceEquals 判真身，避免误碰已销毁对象
                if (!ReferenceEquals(_vcam, null) && _vcam.Follow == _target) _vcam.Follow = _followAtBind;
            }
            if (_ownsTarget && _target != null && !ReferenceEquals(_target, null))
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_target.gameObject);
                else UnityEngine.Object.DestroyImmediate(_target.gameObject);
            }
            _vcam = null;
            _target = null;
            _ownsTarget = false;
            _followAtBind = null;
        }

        /// <summary>接管的 vcam 是否还有效（对象存活 + 场景仍已加载）。</summary>
        private bool IsBoundCameraAlive()
        {
            if (_vcam == null || ReferenceEquals(_vcam, null)) return false;   // 已随场景销毁（伪 null 走 ReferenceEquals）
            return _vcam.gameObject.scene.IsValid();
        }

        public void Follow(in Vector3 target, float deltaSeconds)
        {
            if (_shutdown) return;

            // 场景切换后 vcam 会随旧场景销毁：这里做一次廉价检查并重解析（切场景不需要调用方记得重建）
            if (!IsBoundCameraAlive())
            {
                Resolve();
                if (_vcam == null) return;       // 新场景也没有 vcam：如实不动（装配缺口由 HasCamera 暴露）
            }

            Focus = target;                      // 表现空间的目标位置（诊断/HUD 用）
            if (_target != null && !ReferenceEquals(_target, null)) _target.position = Focus;   // 阻尼/平滑在 Cinemachine 侧按 vcam 配置生效
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
            SceneManager.sceneLoaded -= OnSceneLoaded;   // 退订：静态事件不平账 = 跨 Play 的悬挂回调
            Unbind();
        }
    }
}
