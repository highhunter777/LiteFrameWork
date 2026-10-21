using LiteFramework;
using LiteView;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cinemachine;

namespace LiteClient
{
    /// <summary>
    /// Cinemachine 相机服务（<see cref="ICameraService"/> 的 Cinemachine 实现，
    /// 《联机战斗演示专项设计》§3"相机跟本地预测位置；和解期间跟衰减后的表现位置"）。
    ///
    /// **位置与视线分开**：位置由 <c>OrbitalTransposer</c> 的 yaw 轨道 + 场景 FollowOffset 表达；
    /// 视线由 <c>CinemachinePOV</c> 的 yaw/pitch 轴表达（鼠标 1:1 跟手、yaw 360°、pitch 钳制）。
    /// 两者都只写**场景配置的轴与偏移**——距离/肩偏/FOV/阻尼仍是场景单源，本类不复制档位常量。
    /// 理由（《客户端总设计》§12.2 视觉单一来源的同一条判据）：相机构图是产品表现决策，
    /// 用代码再表达一遍就有了第二处事实源，调完编辑器发现"改了没用"是最难查的一类分歧。
    ///
    /// **例外：三处派生写入**——① <see cref="SetAiming"/> 的优先级（主 vcam 场景值 + 1）；
    /// ② 主相机按场景 <see cref="TpsCameraRigTuning.PitchFollow"/>/<see cref="TpsCameraRigTuning.PivotHeight"/>
    /// 做恒定臂长俯仰弧（地面下限防穿地）；③ 瞄准机沿主视线前移（<see cref="AimOffsetCurve"/> 近距收短）。
    /// 三者下降沿/解绑都**还原回场景配置原值**；Lens/FOV/阻尼/肩偏仍全归场景。语义态（"瞄准中"）
    /// 来自端口调用方，映射成 Cinemachine 机制是适配器的本分。
    ///
    /// **场景切换后的重新解析（本类的关键行为）**：虚拟相机是**场景对象**，随场景加载/卸载而生灭
    /// （Single 模式切场景后，启动场景里的 vcam 会被一并销毁）。因此本类在每次
    /// <see cref="Follow"/> 时做一次**廉价有效性检查**，发现接管的 vcam 所在场景已失效就重新解析并
    /// 重新接线。这样"启动场景配 vcam""Rig 场景配 vcam""叠加加载"三种形态都能工作，不需要调用方
    /// 在切场景后记得手动重建服务（那种"记得调"的约定迟早会漏）。
    ///
    /// **本服务不创建虚拟相机**：解析不到就是装配缺口，如实回报
    /// （<see cref="LastResolveReason"/> / <see cref="HasCamera"/>），由调用方决定降级还是报错；
    /// 不在运行期凭空造一台——否则"美术调好的相机没生效"会被自己造的那台掩盖。
    /// </summary>
    public sealed class CinemachineCameraService : ICameraService
    {
        /// <summary>瞄准 vcam 的场景约定名（Rig 场景 `/Aim Camera`；构图/基础优先级归场景配置）。
        /// 主相机解析时**排除**它——瞄准接管期间它优先级被抬到主之上，不排除会在场景切换重解析时
        /// 把主跟随错误地绑到瞄准机上。</summary>
        private const string AimCameraName = "Aim Camera";

        /// <summary>瞄准机接管优先级增量（主 vcam 场景优先级 + 本值）。</summary>
        private const int AimPriorityBoost = 1;

        private CinemachineVirtualCamera _vcam;
        private Transform _target;               // 当前接线用的跟随目标
        private bool _ownsTarget;                // 自建的空目标才销毁；场景里的不动
        private Transform _followAtBind;         // 接线前的 vcam.Follow（解绑时还原，不留指向已销毁对象的引用）
        private bool _hasFocus;
        private bool _snapNextFollow;            // Reset 后的下一帧跟随直接 warp（复活/传送硬切）
        private bool _shutdown;
        private string _lastResolveReason;
        private readonly System.Collections.Generic.List<Transform> _warpTargets =
            new System.Collections.Generic.List<Transform>(8);   // warp 目标去重表（复用，零分配）

        // ---- 瞄准相机（ADS 接管，持续预放置形态：接线前置到绑定时刻，全程跟着玩家预先就位）----
        private CinemachineVirtualCamera _aimVcam;
        private int _aimPriorityAtBind;            // 接管前优先级（场景配置值——抬升基准由主相机派生，释放时还原回场景值）
        private bool _aiming;                      // 当前语义态（变化沿生效）
        private Vector3 _aimPoint;                 // 最近一帧解算的瞄准点（世界）
        private bool _hasAimPoint;                 // 本帧是否有解算结果（false = 不动作，保持现值）

        // ---- 视角派生基准（bind 采集场景 FollowOffset/轨道轴——俯仰派生与还原的端点；解绑/关闭还原场景值）----
        private Vector3 _mainFollowOffsetAtBind;   // 主 vcam 场景基准 offset
        private Vector3 _aimFollowOffsetAtBind;    // 瞄准 vcam 场景基准 offset
        private bool _hasMainFollowOffset;
        private bool _hasAimFollowOffset;
        private float _mainXAxisAtBind;            // 主 vcam 轨道轴场景基准（视角派生/还原端点）
        private float _aimXAxisAtBind;             // 瞄准 vcam 轨道轴场景基准
        private bool _hasMainXAxis;
        private bool _hasAimXAxis;
        private TpsCameraRigTuning _mainRig;       // 主 vcam 的机位调参（俯仰弧跟随/枢轴/下限；场景单源）
        private float _aimForwardDelta;            // 主/瞄准场景基座的 z 差（ADS 沿主视线前移距离）

        /// <summary>装配点指定的跟随目标（null = 用 vcam 自己配的 Follow，再没有就自建空目标）。</summary>
        private readonly Transform _preferredTarget;

        // ---- 本地视角状态（第三人称自由视角：AddLookDelta 累进；纯视图侧，不进意图三件/不上行）----

        /// <summary>视角 pitch 限位（弧度，正 = 上仰）——机制初档常量单源：下俯 -80°/上仰 +80°
        /// 覆盖 PUBG 量级的抬头看天/低头看脚；再往外越肩构图收益归零、穿头/贴地/穿天风险陡增
        /// （收臂与渐隐兜不住的极端位）。上半身姿态区间另由 <c>AimPitchPosture</c> 钳制，
        /// 不把相机限位直接当骨骼限位。</summary>
        public const float PitchClampMinRad = -80f * Mathf.Deg2Rad;
        public const float PitchClampMaxRad = 80f * Mathf.Deg2Rad;

        private float _lookYaw;     // 累计视角 yaw（弧度，环绕）
        private float _lookPitch;   // 累计视角 pitch（弧度，限位钳制后值）

        /// <summary>当前视角 yaw（弧度；未收增量 = 0）。诊断/测试读口。</summary>
        public float LookYaw => _lookYaw;

        /// <summary>当前视角 pitch（弧度，正 = 上仰，已按限位钳制）。诊断/测试读口。</summary>
        public float LookPitch => _lookPitch;

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
            // 即使玩法场景已经加载完。
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
        /// 解析规则：全场景中**优先级最高**的启用 vcam（**排除瞄准机**——见 <see cref="AimCameraName"/>；
        /// 瞄准接管期间它优先级在主之上，不排除会把主跟随绑错）——多台 vcam 并存时
        /// （如每场景各配一台、或叠加场景各带一台）由优先级决定接哪台，与 Cinemachine 自身的选机口径一致。
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
                if (all[i].name == AimCameraName) continue;   // 瞄准机不参与主跟随竞选
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
            CaptureMainFollowOffset();           // 场景基准（视角派生/还原的端点）

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

            BindAimCamera();                     // 瞄准机**绑定即预放置**（接线前置——不等右键）
            ApplyRigDerived();                   // 重解析/切场景后保持视角派生写入

            _lastResolveReason = null;
            Log.Info($"[Camera] 已接线 vcam「{_vcam.name}」(scene={_vcam.gameObject.scene.name}, priority={_vcam.Priority})", LogTag.Camera);
        }

        /// <summary>解除当前接线（销毁自建目标、还原 vcam 的 Follow 引用）。</summary>
        private void Unbind()
        {
            if (_aimVcam != null && !ReferenceEquals(_aimVcam, null))
            {
                _aimVcam.Priority = _aimPriorityAtBind;   // 归还场景优先级（Follow/LookAt 保持接线——持续预放置）
                _aiming = false;
            }
            RestoreFollowOffsets();                       // 视角派生的写入归还场景基准
            _aimVcam = null;
            _mainRig = null;
            _aimForwardDelta = 0f;
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

        public void Follow(in Vector3 target, in Quaternion facing, float deltaSeconds)
        {
            if (_shutdown) return;

            // 场景切换后 vcam 会随旧场景销毁：这里做一次廉价检查并重解析（切场景不需要调用方记得重建）
            if (!IsBoundCameraAlive())
            {
                Resolve();
                if (_vcam == null) return;       // 新场景也没有 vcam：如实不动（装配缺口由 HasCamera 暴露）
            }

            Focus = target;                      // 表现空间的目标位置（诊断/HUD 用）
            ApplyLetterbox();                    // 固定 16:9 视口（N1）：X/Y 可见范围不随屏幕画幅变化
            if (_target != null && !ReferenceEquals(_target, null))
            {
                Vector3 previous = _target.position;
                // 位置每帧写；**朝向不写焦点（第三人称自由视角）**：视线由 POV 轴驱动，焦点旋转
                // 不参与构图；越肩构图由相机位置的肩偏/高度形成，焦点恒 identity ⇒ 角色朝向或
                // 场景目标自带姿态都不会把画面构图带偏。场景配置的 Follow 目标同样只写位置。
                _target.position = Focus;   // 阻尼/平滑在 Cinemachine 侧按 vcam 配置生效

                // 复活/传送是瞬移：仅 Reset 后这一帧要求相机**直接 warp**，否则 vcam 阻尼会从上一条命
                // 的位置慢慢飞过去，模型在镜头追上之前不在画面内（用户实测“复活后模型过一会才出现”）。
                if (_snapNextFollow)
                {
                    _snapNextFollow = false;
                    Vector3 delta = target - previous;
                    if (delta.sqrMagnitude > 0f) WarpCameras(delta);
                }
            }
            _hasFocus = true;                     // 先置焦点再派生：首次 Follow 也要落到 vcam
            ApplyRigDerived();                    // 视角派生写入（POV 轴 + 轨道 yaw + 恒定臂长俯仰弧——每拍随焦点喂入）
        }

        /// <summary>
        /// 瞬移 warp（主 + 瞄准两台 vcam 的 Follow/LookAt **全量**前移）：
        /// 只 warp 主机时，瞄准机（<see cref="AimCameraName"/>）仍停在旧构图——死亡瞬间处于 ADS 的
        /// 玩家复活后会看到镜头压在旧位置/旧构图上（"复活了但相机没对准"的成因之一）。
        /// 同名目标去重：`_target` 与 `_vcam.Follow` 常常是同一件，重复 warp 会让相机偏移两倍。
        /// </summary>
        private void WarpCameras(Vector3 delta)
        {
            _warpTargets.Clear();
            AddWarpTarget(_target);
            if (_vcam != null && !ReferenceEquals(_vcam, null)) { AddWarpTarget(_vcam.Follow); AddWarpTarget(_vcam.LookAt); }
            if (_aimVcam != null && !ReferenceEquals(_aimVcam, null)) { AddWarpTarget(_aimVcam.Follow); AddWarpTarget(_aimVcam.LookAt); }

            var core = CinemachineCore.Instance;
            for (int i = 0; i < _warpTargets.Count; i++)
                core.OnTargetObjectWarped(_warpTargets[i], delta);

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // [Diag] warp 留痕（联机实测表现诊断批，收口时删除）：判"没对准"是没 warp 还是 warp 后目标仍偏。
            Log.Info($"[Diag][Camera] warp delta={delta.x:0.00},{delta.y:0.00},{delta.z:0.00}"
                + $" focus={Focus.x:0.00},{Focus.y:0.00},{Focus.z:0.00}"
                + $" vm={_vcam?.name}{(_aiming ? "+aim" : string.Empty)} targets={_warpTargets.Count}", LogTag.Camera);
#endif
        }

        /// <summary>warp 目标登记（同名去重：ReferenceEquals 判 Unity 伪 null 后的同一件）。</summary>
        private void AddWarpTarget(Transform t)
        {
            if (t == null || ReferenceEquals(t, null)) return;
            for (int i = 0; i < _warpTargets.Count; i++)
                if (ReferenceEquals(_warpTargets[i], t)) return;
            _warpTargets.Add(t);
        }

        public void Reset()
        {
            _hasFocus = false;
            Focus = Vector3.zero;
            _snapNextFollow = true;
            _lookYaw = 0f;
            _lookPitch = 0f;
        }

        /// <summary>视角增量累进（<see cref="ICameraService.AddLookDelta"/> 契约）：累进自持 yaw/pitch 状态
        /// （yaw 按 2π 环绕、pitch 钳在限位，**pitch 正 = 上仰**）；零增量无操作。**纯视图侧**——不进意图三件、不上行。
        /// 状态到 vcam 的应用面（POV 轴 + 轨道 yaw + 恒定臂长俯仰弧）在接线与 <see cref="Follow"/> 拍内写入，
        /// 构图基准（距离/肩偏/FOV）仍归场景配置单源。**关闭后静默 no-op**（与"关闭后不写相机"同契约）。</summary>
        public void AddLookDelta(float yawDelta, float pitchDelta)
        {
            if (_shutdown) return;
            if (yawDelta == 0f && pitchDelta == 0f) return;
            _lookYaw = Mathf.Repeat(_lookYaw + yawDelta, Mathf.PI * 2f);
            _lookPitch = Mathf.Clamp(_lookPitch + pitchDelta, PitchClampMinRad, PitchClampMaxRad);
            ApplyRigDerived();   // 有焦点时输入当帧落到 POV 轴/轨道（不等下一次 Follow；无焦点由 Follow 首拍落）
        }

        /// <summary>记录主 vcam 的场景基准（FollowOffset + 轨道轴 m_XAxis.Value + 机位调参——
        /// 视角派生/还原的端点；无 transposer = 不参与）。</summary>
        private void CaptureMainFollowOffset()
        {
            _hasMainFollowOffset = TryReadFollowOffset(_vcam, out _mainFollowOffsetAtBind);
            _hasMainXAxis = TryReadOrbitHeading(_vcam, out _mainXAxisAtBind);
            _mainRig = _vcam.GetComponent<TpsCameraRigTuning>();
        }

        /// <summary>视角派生写入（双机同写）：轨道 yaw 与 POV 水平轴共用 <c>_lookYaw</c>，POV 垂直轴
        /// 按 <see cref="TpsCameraMath.PovVerticalDegrees"/>（pitch 正 = 上仰）。位置侧：主相机绕
        /// <see cref="TpsCameraRigTuning.PivotHeight"/> 做**恒定臂长**俯仰弧（跟随系数
        /// <see cref="TpsCameraRigTuning.PitchFollow"/>，地面下限 <see cref="TpsCameraRigTuning.MinBoomHeight"/>）；
        /// 瞄准机沿主相机视线前移（<see cref="AimOffsetCurve"/> 近距收短），保证 ADS 视线共线。
        /// **首次 Follow 前 no-op**（没焦点就不写场景 vcam——纯状态测试/装配期不产生脏改）。
        /// 构图基准仍归场景单源，幂等可重入（<see cref="Follow"/> 每拍调用）。</summary>
        private void ApplyRigDerived()
        {
            if (!_hasFocus) return;
            float yawDegrees = TpsCameraMath.PovHorizontalDegrees(_lookYaw);
            ApplyPovAndYaw(_vcam, yawDegrees);
            ApplyPovAndYaw(_aimVcam, yawDegrees);

            if (!_hasMainFollowOffset || _vcam == null || ReferenceEquals(_vcam, null)) return;
            Vector3 mainOffset = ResolveMainOffset();
            ApplyFollowOffset(_vcam, mainOffset);
            if (_aimVcam == null || ReferenceEquals(_aimVcam, null) || !_hasAimFollowOffset) return;
            ApplyFollowOffset(_aimVcam, ResolveAimOffset(mainOffset));
        }

        private void ApplyPovAndYaw(CinemachineVirtualCamera vcam, float yawDegrees)
        {
            if (vcam == null || ReferenceEquals(vcam, null)) return;
            CinemachineOrbitalTransposer orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbital != null && !ReferenceEquals(orbital, null)) orbital.m_XAxis.Value = yawDegrees;

            CinemachinePOV pov = vcam.GetCinemachineComponent<CinemachinePOV>();
            if (pov != null && !ReferenceEquals(pov, null))
            {
                pov.m_HorizontalAxis.Value = yawDegrees;
                pov.m_VerticalAxis.Value = TpsCameraMath.PovVerticalDegrees(_lookPitch);
            }
        }

        private static void ApplyFollowOffset(CinemachineVirtualCamera vcam, in Vector3 offset)
        {
            if (vcam == null || ReferenceEquals(vcam, null)) return;
            CinemachineOrbitalTransposer orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbital == null || ReferenceEquals(orbital, null)) return;
            orbital.m_FollowOffset = offset;
        }

        /// <summary>主相机位置：场景基准偏移绕枢轴做恒定臂长俯仰弧。臂长由场景
        /// <c>FollowOffset</c> 与 <see cref="TpsCameraRigTuning.PivotHeight"/> 决定，服务不另写距离常量。</summary>
        private Vector3 ResolveMainOffset()
        {
            Vector3 offset = _mainFollowOffsetAtBind;
            if (_mainRig == null) return offset;

            Vector2 arm = TpsCameraMath.PitchArm(
                _lookPitch, _mainRig.PitchFollow,
                offset.y - _mainRig.PivotHeight, offset.z);
            offset.y = _mainRig.PivotHeight + arm.x;
            offset.z = arm.y;
            offset.y = Mathf.Max(offset.y, _mainRig.MinBoomHeight);
            return offset;
        }

        /// <summary>瞄准机位置：主相机位置沿**当前视线方向**前移
        /// <see cref="AimOffsetCurve"/> 给出的距离（场景主/瞄准基座差为端点）——ADS 只变焦/贴近，
        /// 不改变屏心射线（俯仰任意角度都共线）。</summary>
        private Vector3 ResolveAimOffset(in Vector3 mainOffset)
        {
            float forward = _aimForwardDelta;
            if (_aiming && _hasAimPoint && _target != null && !ReferenceEquals(_target, null))
            {
                Vector3 viewWorld = Quaternion.Euler(
                    0f, TpsCameraMath.PovHorizontalDegrees(_lookYaw), 0f)
                    * TpsCameraMath.ViewDirection(_lookPitch);
                float distance = Vector3.Dot(_aimPoint - _target.position, viewWorld);
                forward = AimOffsetCurve.Z(Mathf.Max(0f, distance), forward);
            }
            return mainOffset + TpsCameraMath.ViewDirection(_lookPitch) * forward;
        }

        /// <summary>解绑/关闭：把派生写入（offset + 轨道轴）还原为场景基准（幂等；对象已销毁则跳过）。</summary>
        private void RestoreFollowOffsets()
        {
            RestoreFollowOffset(_vcam, _hasMainFollowOffset, _mainFollowOffsetAtBind);
            RestoreFollowOffset(_aimVcam, _hasAimFollowOffset, _aimFollowOffsetAtBind);
            RestoreOrbitHeading(_vcam, _hasMainXAxis, _mainXAxisAtBind);
            RestoreOrbitHeading(_aimVcam, _hasAimXAxis, _aimXAxisAtBind);
            _hasMainFollowOffset = false;
            _hasAimFollowOffset = false;
            _hasMainXAxis = false;
            _hasAimXAxis = false;
        }

        private static void RestoreFollowOffset(CinemachineVirtualCamera vcam, bool hasOffset, in Vector3 sceneOffset)
        {
            if (!hasOffset || vcam == null || ReferenceEquals(vcam, null)) return;
            CinemachineTransposer transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer == null || ReferenceEquals(transposer, null)) return;
            transposer.m_FollowOffset = sceneOffset;
        }

        /// <summary>读取轨道轴当前值（非 Orbital rig 返回 false——不参与视角派生）。</summary>
        private static bool TryReadOrbitHeading(CinemachineVirtualCamera vcam, out float heading)
        {
            heading = 0f;
            if (vcam == null || ReferenceEquals(vcam, null)) return false;
            CinemachineOrbitalTransposer orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbital == null || ReferenceEquals(orbital, null)) return false;
            heading = orbital.m_XAxis.Value;
            return true;
        }

        private static void RestoreOrbitHeading(CinemachineVirtualCamera vcam, bool hasHeading, float sceneHeading)
        {
            if (!hasHeading || vcam == null || ReferenceEquals(vcam, null)) return;
            CinemachineOrbitalTransposer orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbital == null || ReferenceEquals(orbital, null)) return;
            orbital.m_XAxis.Value = sceneHeading;
        }

        private static bool TryReadFollowOffset(CinemachineVirtualCamera vcam, out Vector3 offset)
        {
            offset = default;
            if (vcam == null || ReferenceEquals(vcam, null)) return false;
            CinemachineTransposer transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer == null || ReferenceEquals(transposer, null)) return false;
            offset = transposer.m_FollowOffset;
            return true;
        }

        /// <summary>固定 16:9 信箱式视口（N1）：3D 相机视口居中裁成 16:9，非 16:9 屏幕留黑边；
        /// X/Y 可见范围在任意屏幕上一致。只在视口变化时写 Camera.rect 并留痕。</summary>
        private void ApplyLetterbox()
        {
            Camera camera = TryGetRenderCamera();
            if (camera == null) return;
            Rect viewport = CameraAspectLetterbox.Viewport(Screen.width, Screen.height);
            if (camera.rect == viewport) return;
            camera.rect = viewport;
            Log.Info($"[Camera] letterbox {Screen.width}x{Screen.height} viewport={viewport.width:F3}x{viewport.height:F3} offset=({viewport.x:F3},{viewport.y:F3})", LogTag.Camera);
        }

        /// <summary>
        /// 瞄准态接管（<see cref="ICameraService.SetAiming"/> 契约；持续预放置形态）：
        /// 瞄准机的 Follow/LookAt 在**绑定主相机时即接线**（<see cref="BindAimCamera"/>），且
        /// StandbyUpdate=Always 全程跟着玩家——**接管瞬间瞄准机已在正确位姿**，切换只剩
        /// 优先级翻转与 brain 的 FOV/构图混合，无陈旧位姿甩动；近距前移按
        /// <see cref="AimOffsetCurve"/> 随瞄准点拉近（<see cref="ApplyRigDerived"/> 派生写入、释放还原场景值）。
        /// 幂等（已接管则只刷新派生写入）；主机未接线/瞄准机缺失时保持未接管逐帧重试，缺机记
        /// <see cref="LastAimResolveReason"/>（不代场景自建）。
        /// </summary>
        public void SetAiming(bool aiming)
        {
            if (_shutdown) return;

            if (aiming)
            {
                if (_aiming && _aimVcam != null && !ReferenceEquals(_aimVcam, null)
                    && _aimVcam.gameObject.scene.IsValid())
                {
                    ApplyRigDerived();                        // 已接管：每帧按瞄准点刷新近距前移与 POV 轴
                    return;
                }
                if (_vcam == null || ReferenceEquals(_vcam, null)) return;   // 主机未接线：保持未接管重试
                BindAimCamera();                              // 幂等：未接线则接线（预放置语义见上）
                if (_aimVcam == null || ReferenceEquals(_aimVcam, null)) return;

                if (!_aiming)                                 // 上升沿：翻优先级 + 刷新派生写入
                {
                    _aiming = true;
                    _aimPriorityAtBind = _aimVcam.Priority;
                    _aimVcam.Priority = _vcam.Priority + AimPriorityBoost;   // 基准派生：主 vcam 场景优先级 + 本值
                    ApplyRigDerived();
                    Log.Info($"[Camera] 瞄准机接管「{_aimVcam.name}」prio={_aimVcam.Priority}", LogTag.Camera);
                }
            }
            else
            {
                if (!_aiming) return;
                _aiming = false;
                if (_aimVcam != null && !ReferenceEquals(_aimVcam, null))
                {
                    _aimVcam.Priority = _aimPriorityAtBind;   // 还原优先级；Follow/LookAt 保持接线（持续预放置）
                    ApplyRigDerived();                        // 近距前移同样还原场景值
                    Log.Info($"[Camera] 瞄准机释放「{_aimVcam.name}」prio={_aimVcam.Priority}", LogTag.Camera);
                }
            }
        }

        /// <summary>
        /// 瞄准机**持续预放置**（幂等）：按约定名解析 + Follow/LookAt 接主相机同源焦点 +
        /// StandbyUpdate=Always。全程跟着玩家的意义：接管瞬间瞄准机已在正确位姿，无陈旧位姿甩动。解析失败记
        /// <see cref="LastAimResolveReason"/>（不代场景自建——"美术调好的相机没生效"不能被掩盖；
        /// 日志只在新原因时打一次，防每帧重试刷屏）。
        /// </summary>
        private void BindAimCamera()
        {
            if (!EnsureAimCameraResolved()) return;
            if (_aimVcam.Follow == _target
                && _aimVcam.m_StandbyUpdate == CinemachineVirtualCameraBase.StandbyUpdateMode.Always) return;

            _aimVcam.Follow = _target;
            if (_aimVcam.LookAt == null) _aimVcam.LookAt = _target;
            _aimVcam.m_StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
            if (!_hasAimFollowOffset)   // 每台瞄准机只采一次场景基准（重绑不把派生值再当基准）
            {
                _hasAimFollowOffset = TryReadFollowOffset(_aimVcam, out _aimFollowOffsetAtBind);
                _hasAimXAxis = TryReadOrbitHeading(_aimVcam, out _aimXAxisAtBind);
                // 主/瞄准场景基座的 z 差 = ADS 沿主视线前移的基座距离（近距由 AimOffsetCurve 收短）。
                _aimForwardDelta = _hasMainFollowOffset
                    ? Mathf.Max(0f, _aimFollowOffsetAtBind.z - _mainFollowOffsetAtBind.z)
                    : 0f;
            }
            ApplyRigDerived();
            ApplyLetterbox();
            Log.Info($"[Camera] 瞄准机预放置「{_aimVcam.name}」(follow/lookAt=主焦点, standby=Always)", LogTag.Camera);
        }

        /// <summary>解析瞄准机（按约定名；已解析且存活直接复用）。返回 false = 场景未配置瞄准机。</summary>
        private bool EnsureAimCameraResolved()
        {
            if (_aimVcam != null && !ReferenceEquals(_aimVcam, null)
                && _aimVcam.gameObject.scene.IsValid())
            { LastAimResolveReason = null; return true; }

            _aimVcam = null;
            var all = UnityEngine.Object.FindObjectsByType<CinemachineVirtualCamera>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].name != AimCameraName) continue;
                _aimVcam = all[i];
                _hasAimFollowOffset = false;   // 新解析的瞄准机：场景基准从当前值重新采集
                _aimForwardDelta = 0f;
                LastAimResolveReason = null;
                return true;
            }

            string reason = $"场景里没有启用的「{AimCameraName}」——ADS 接管 no-op（瞄准相机配置归场景，不由代码自建）";
            if (LastAimResolveReason != reason)               // 只在原因变化时打（防每帧重试刷屏）
            {
                Log.Warning("[Camera] " + reason, LogTag.Camera);   // 装配缺口＝内容降级口径（Warning 可见化，修审计 L42"静默 no-op"面）
                LastAimResolveReason = reason;
            }
            return false;
        }

        /// <summary>最近一次瞄准机解析失败的原因（解析成功/未尝试 = null）。诊断/装配检查用。</summary>
        public string LastAimResolveReason { get; private set; }

        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;   // 退订：静态事件不平账 = 跨 Play 的悬挂回调
            Unbind();
        }

        /// <summary>瞄准点通知（<see cref="ICameraService.SetAimPoint"/> 契约）：缓存本帧解算结果；
        /// 瞄准期立即刷新瞄准机近距前移（<see cref="ApplyRigDerived"/>），非瞄准期只存值。</summary>
        public void SetAimPoint(in Vector3 point, bool hasPoint)
        {
            _aimPoint = point;
            _hasAimPoint = hasPoint;
            if (_aiming) ApplyRigDerived();
        }
    }
}
