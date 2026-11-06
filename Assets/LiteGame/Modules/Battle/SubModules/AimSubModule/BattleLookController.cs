using System;
using LiteFramework;
using LiteSim;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Input;
using LiteGame.Modules.Settings;

namespace LiteGame
{
    /// <summary>
    /// 对局**视角/指针输入控制器**：鼠标增量 → 相机 yaw/pitch（含 ADS 灵敏度收敛与 GM 调参覆写）、 相机瞄准混合门（含复活复位）、瞄准点注入。
    /// </summary>
    internal sealed class BattleLookController
    {
        /// <summary>视角灵敏度（弧度/像素）——机制初档常量单源：0.0025 ≈ 0.14°/px，1440px 横扫 ≈ 206°
        /// （约三分之一圈，主流 TPS 鼠标灵敏度中位起点）；ADS 档缩放与 GM/Inspector 调参面随 T6 落位
        /// （以本常量为默认基线）。</summary>
        internal const float LookSensitivityRadPerPixel = 0.0025f;

        /// <summary>ADS 期视角灵敏度收敛比——机制初档常量单源：ADS 视场收窄（FOV 60→50，收窄比 50/60≈0.83）
        /// 下同像素位移对应更大的视角变化，灵敏度须同比收敛再叠手感衰减（同类 TPS 常见 0.5~0.7 的中位）；
        /// ADS 构图档位本体＝双机优先级切换 + brain 的 FOV/构图混合（场景 rig 值），本常量只管增量缩放。</summary>
        internal const float AdsLookSensitivityScale = 0.6f;

        /// <summary>松开 Alt 后相机回正的时长（秒；用户裁决 0.15s）。</summary>
        internal const float FreeLookRecenterSeconds = 0.15f;

        /// <summary>回正指数锐度（1/s）；0.15s 内基本收敛。</summary>
        private const float FreeLookRecenterSharpness = 24f;

        private readonly IInputService _input;
        private readonly ICameraService _camera;
        private readonly ILookPreferences _lookPreferences;   // 玩家偏好（倍率/反转；null = 纯机制常量，测试装配）
        private readonly PointerLockService _pointer = new PointerLockService();   // Cursor 的唯一写者
        private readonly CameraAimBlendDriver _aimBlend;   // 瞄准事实 → 相机混合（宽度取相机域常量单源）

        private SimView _view;
        private ILookDevice _lookDevice;              // 自由视角设备能力（增量/肩位/自由观察冻结；null = 无形态）
        private Func<Vector2> _lookDeltaOf;           // 视角增量来源（设备源适配器 LookDelta；随本局设备源挂/摘）
        private Func<bool> _shoulderSwapOf;            // 左右肩切换沿（设备源适配器；随本局设备源挂/摘）
        private Func<Vector3?> _aimPointOf;           // 瞄准点来源（世界坐标；null = 无设备形态）
        private int _lastRespawnSerial;               // 本地复活序号（瞄准门一次性复位用）

        // 自由观察（Alt）：进入时保存相机角，松开后回正；开火/ADS 立即退出并保持当前视角。
        private bool _freeLooking;
        private bool _aimFrozen;
        private float _savedLookYaw;
        private float _savedLookPitch;
        private float _recenterRemaining;

        public BattleLookController(IInputService input, ICameraService camera, ILookPreferences lookPreferences = null)
        {
            _input = input;
            _camera = camera;
            _lookPreferences = lookPreferences;
            _aimBlend = new CameraAimBlendDriver(camera, CameraBlendDefaults.AimBlendSeconds);
        }

        /// <summary>本帧瞄准点（世界坐标；无设备形态/无点 = null）——传送命令等外部消费者读口。</summary>
        public Vector3? AimPoint => _aimPointOf?.Invoke();

        /// <summary>挂本局视图与设备源适配器（设备源已在装配根装好；本件只取增量/瞄准点读口）。</summary>
        public void Attach(SimView view)
        {
            _view = view;
            _lookDevice = _input?.Source as ILookDevice;
            _lookDeltaOf = LookDeltaOf(_input);
            _shoulderSwapOf = ShoulderSwapOf(_input);
            _aimPointOf = AimPointOf(_input);
        }

        /// <summary>每渲染帧：复活复位 → 计算控制态 → 视角增量 → 瞄准态/瞄准点 → 指针锁定（顺序固定）。</summary>
        public void Tick(float elapseSeconds)
        {
            // 复活复位：上一条命的 ADS 门控态不得跨命沿用（否则复活瞬间镜头被钉在瞄准机上）。
            if (_view != null && _view.LocalRespawnSerial != _lastRespawnSerial)
            {
                _lastRespawnSerial = _view.LocalRespawnSerial;
                ExitFreeLookImmediate();          // 复活不沿用上一条命的自由观察冻结
                _aimBlend.Reset();
            }
            // 对局控制中（视图建好、本地在场、未被上下文门拦、存活）才吃视角增量：
            // 模态 UI/死亡/加载段鼠标不应在背后带动相机（PUBG 的解锁形态同口径）。
            bool pointerLocked = _view != null && _view.HasLocalDisplay && _view.LocalEntityId != 0
                && (_input == null || !_input.IsBlocked) && !IsLocalDead();

            bool aiming = IsLocalAiming();
            bool fireHeld = (_input != null)
                && (_input.Pending.Buttons & SimInputFrame.ButtonFire) != 0u;
            bool freeLookHeld = pointerLocked && _camera != null && _lookDevice != null
                && _lookDevice.FreeLookHeld && !aiming && !fireHeld;

            // 自由观察开始：保存相机角，冻结 AimPoint/移动基准；鼠标继续转相机。
            if (freeLookHeld && !_freeLooking)
            {
                _freeLooking = true;
                _recenterRemaining = 0f;
                _savedLookYaw = _camera.LookYaw;
                _savedLookPitch = _camera.LookPitch;
                FreezeAim(true);
            }
            else if (_freeLooking && !freeLookHeld)
            {
                _freeLooking = false;
                // 开火/ADS/死亡/解锁：立即退出并保持当前相机视角；普通松 Alt 才回正。
                if (aiming || fireHeld || !pointerLocked) ExitFreeLookImmediate();
                else _recenterRemaining = FreeLookRecenterSeconds;
            }

            // 视角增量（第三人称自由视角）：鼠标 delta → 相机 yaw/pitch（纯视图侧，不上行）。
            Vector2 look = pointerLocked && _lookDeltaOf != null ? _lookDeltaOf() : Vector2.zero;
            bool mouseMoved = look != Vector2.zero;
            if (mouseMoved)
            {
                // 手动推鼠标 = 默认自由视角接管：取消回正并解冻 AimPoint。
                if (_recenterRemaining > 0f) ExitFreeLookImmediate();

                // 灵敏度 = 机制常量 × 玩家倍率；ADS 再乘机制收敛比 × 玩家 ADS 倍率。
                // 优先级：GM 覆写（开发三宏）> 产品倍率 > 机制常量。
                float sensitivity = LookTuning.SensitivityOf(LookSensitivityRadPerPixel)
                    * SensitivityMultiplier()
                    * (aiming
                        ? LookTuning.AdsScaleOf(AdsLookSensitivityScale) * AdsMultiplier()
                        : 1f);
                float invert = InvertY() ? -1f : 1f;
                _camera.AddLookDelta(
                    look.x * sensitivity,
                    look.y * sensitivity * invert);
            }
            if (pointerLocked && _shoulderSwapOf != null && _shoulderSwapOf())
                _camera?.ToggleShoulder();                 // 左右肩切换（纯视图侧，不上行）

            // 松开 Alt 回正：冻结 AimPoint 直到相机回到保存角，到位后解冻。
            if (_recenterRemaining > 0f && !_freeLooking)
            {
                if (aiming || fireHeld)
                {
                    ExitFreeLookImmediate();               // 开火/ADS 立即退出，保持当前视角
                }
                else
                {
                    float dt = Mathf.Max(0f, elapseSeconds);
                    float yawDelta = Mathf.DeltaAngle(
                        _camera.LookYaw * Mathf.Rad2Deg, _savedLookYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float pitchDelta = _savedLookPitch - _camera.LookPitch;
                    float alpha = 1f - Mathf.Exp(-dt * FreeLookRecenterSharpness);
                    _camera.AddLookDelta(yawDelta * alpha, pitchDelta * alpha);
                    _recenterRemaining -= dt;
                    if (_recenterRemaining <= 0f)
                    {
                        _camera.SetLookAngles(_savedLookYaw, _savedLookPitch);
                        FreezeAim(false);
                    }
                }
            }

            if (!pointerLocked && (_freeLooking || _recenterRemaining > 0f)) ExitFreeLookImmediate();

            _aimBlend.Tick(aiming, elapseSeconds);
            if (_camera != null)                 // 瞄准点（世界）→ 瞄准机近距后撤（适配器按视角前向投影换算）
            {
                Vector3? aimPoint = _aimPointOf?.Invoke();
                _camera.SetAimPoint(aimPoint ?? default, aimPoint.HasValue);
            }

            // 指针锁定（TPS 输入形态）：控制中 = 锁定+隐藏（准心是唯一瞄准标示）；
            // 门被拦/视图未建/死亡 = 解锁恢复（模态要点按钮）。Cursor 唯一写者是 PointerLockService。
            if (pointerLocked) _pointer.Lock(); else _pointer.Unlock();
        }

        /// <summary>离场：摘视角源/瞄准点、解锁指针、还原相机瞄准接管（不遗留锁定/接管态）。</summary>
        public void Dispose()
        {
            ExitFreeLookImmediate();              // 离场不得遗留 AimPoint 冻结
            _lookDevice = null;
            _aimPointOf = null;                   // 瞄准点来源随本局设备源一起摘（方法组不跨局持有）
            _lookDeltaOf = null;                  // 视角增量源同摘（离场不遗留设备源闭包）
            _shoulderSwapOf = null;               // 肩位切换沿同摘
            _pointer.Unlock();                    // 指针解锁恢复（离场不遗留锁定态——模态/桌面要可点）
            _aimBlend.Reset();                    // 瞄准机还原（优先级/Follow 归还场景值——离场不得遗留接管态）
            _view = null;
        }

        /// <summary>
        /// 视角增量来源：设备源适配器的 NewInputIntentSource.LookDelta（像素）。
        /// </summary>
        private static Func<Vector2> LookDeltaOf(IInputService input)
        {
            return input != null && input.Source is ILookDevice device
                ? () => device.LookDelta
                : null;
        }

        /// <summary>肩位切换沿来源：设备源适配器的 Q 沿（同一 R12 边界；非该实现 = 无肩切形态）。</summary>
        private static Func<bool> ShoulderSwapOf(IInputService input)
        {
            return input != null && input.Source is ILookDevice device
                ? () => device.ShoulderSwapPressed
                : null;
        }

        /// <summary>
        /// 瞄准点来源：**待上行输入帧**（IInputService.Pending）的 AimPoint 三分量—— 与准心/开火/上行同一份事实（AimPoint 单口径）。
        /// </summary>
        private static Func<Vector3?> AimPointOf(IInputService input)
        {
            if (input == null) return null;
            return () =>
            {
                SimInputFrame f = input.Pending;
                return (f.AimPointX != 0f || f.AimPointY != 0f || f.AimPointZ != 0f)
                    ? (Vector3?)new Vector3(f.AimPointX, f.AimPointY, f.AimPointZ)
                    : null;
            };
        }

        /// <summary>冻结/解冻 AimPoint（设备源写缓存点；解冻后下一帧恢复相机屏心）。</summary>
        private void FreezeAim(bool frozen)
        {
            if (_aimFrozen == frozen) return;
            _aimFrozen = frozen;
            _lookDevice?.SetAimFrozen(frozen);
        }

        /// <summary>立即退出自由观察：解冻、取消回正，相机保持当前视角（开火/ADS/死亡走这里）。</summary>
        private void ExitFreeLookImmediate()
        {
            _freeLooking = false;
            _recenterRemaining = 0f;
            FreezeAim(false);
        }

        /// <summary>本地是否阵亡（预测态锁存判据——<see cref="SimView.IsDead"/> 单源）。</summary>
        private bool IsLocalDead()
        {
            int slot = LocalSlot();
            return slot >= 0 && _view.IsDead(slot);
        }

        /// <summary>本地玩家是否处于瞄准态（右键 ADS）——喂 <see cref="ICameraService.SetAiming"/> 的语义源。</summary>
        private bool IsLocalAiming()
        {
            return _view != null && _view.LocalEntityId != 0
                && _view.TryGetSlot(_view.LocalEntityId, out int slot)
                && _view.IsAiming(slot);
        }

        /// <summary>玩家灵敏度倍率（未接入偏好 = 1.0，即纯机制常量）。</summary>
        private float SensitivityMultiplier() => _lookPreferences != null ? _lookPreferences.SensitivityMultiplier : 1f;

        /// <summary>玩家 ADS 倍率（未接入偏好 = 1.0，覆盖在机制收敛比之上）。</summary>
        private float AdsMultiplier() => _lookPreferences != null ? _lookPreferences.AdsScaleMultiplier : 1f;

        /// <summary>垂直视角是否反转（未接入偏好 = 不反转）。</summary>
        private bool InvertY() => _lookPreferences != null && _lookPreferences.InvertY;

        private int LocalSlot()
        {
            if (_view == null || _view.LocalEntityId == 0L) return -1;
            return _view.TryGetSlot(_view.LocalEntityId, out int slot) ? slot : -1;
        }
    }
}
