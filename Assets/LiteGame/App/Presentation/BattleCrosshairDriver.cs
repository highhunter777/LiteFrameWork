using System;
using LiteSim.View;
using UnityEngine;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 对局准心驱动（战斗 HUD 第一件：仅形态切换、最小可用、场景内对象）：
    /// 纯 View 层——每渲染帧把场景里的准心（训练场 <c>/Battle HUD</c>）摆到鼠标屏幕点，并按本地
    /// 预测态瞄准位（<see cref="SimView.IsAiming"/>，右键 ADS）切换腰射/瞄准两组视觉。
    ///
    /// **数据源单向流**（《角色状态与动作专项设计》§1 层级边界——HUD 归 View，禁止回写 Sim）：
    /// - **位置**：鼠标屏幕点 → <c>RectTransformUtility.ScreenPointToLocalPointInRectangle</c>
    ///   （Overlay 画布，camera=null，CanvasScaler 由该 API 一并处理）。腰射时准心即瞄准落点
    ///   （瞄准链 = 鼠标点 → 相机射线 → 地面交点，屏幕点回投仍是本位——无散布下严格同位）；
    ///   不读插值后的表现 Transform、不把任何值回灌输入（同 <c>ProcedureBattle</c> 采样原点纪律）。
    /// - **形态**：<see cref="SimView.IsAiming"/> 读**本地预测态**（连续位进 PredictedButtons，
    ///   与本地手感同帧）；按实体 Id 解析槽位（不能把带版本的 Id 当下标）。
    /// - **可见性**：上下文门被拦（模态 UI 开 → 本帧输入为空）→ 藏准心还
    ///   系统光标（模态要点按钮）；本地表现未建（StartGame 未达/首快照未对齐）→ 整体隐藏。
    /// - **光标**：准心替代系统光标——对局内 <c>Cursor.visible=false</c>，Dispose 恢复
    ///   （流程离场/宿主关闭必经 <see cref="ProcedureBattle.DetachView"/>，光标不遗留隐藏态）。
    ///
    /// **不做**（登记于《角色状态与动作专项设计》§7 准心行）：散布张开（随武器数值表化）、
    /// 命中标记（随帧事件消费）、ADS 相机联动（手测项——**不接相机时 ADS 准心必须跟鼠标**，
    /// 射线解算以鼠标为源，锁屏心要等瞄准相机同轴后才成立）。
    ///
    /// 测试注：<paramref name="screenPosition"/>/<paramref name="setCursorVisible"/> 是注入位
    /// （EditMode 确定性驱动）。**鼠标位的生产来源是设备源适配器**
    /// （<see cref="NewInputIntentSource.MouseScreenPosition"/>——InputSystem 的 import 只许在
    /// 适配器边界内，R12；本层经注入消费，不直接认识 InputSystem）；注入位为 null 时回退屏幕零点
    /// （无设备/触屏形态——触屏源归输入服务余部）。
    /// </summary>
    public sealed class BattleCrosshairDriver : IDisposable
    {
        private readonly SimView _view;
        private readonly IInputService _input;              // 上下文门被拦 → 藏准心还光标；null = 无输入形态（不拦）
        private readonly RectTransform _plane;               // Overlay 画布根（坐标换算基准；测试可用任意 RectTransform）
        private readonly RectTransform _reticle;
        private readonly GameObject _hip;
        private readonly GameObject _ads;
        private readonly Func<Vector2> _screenPosition;     // 注入位：默认读 InputSystem 鼠标
        private readonly Action<bool> _setCursorVisible;    // 注入位：默认写 Cursor.visible
        private bool _disposed;

        public BattleCrosshairDriver(SimView view, IInputService input, RectTransform plane,
            RectTransform reticle, GameObject hip, GameObject ads,
            Func<Vector2> screenPosition = null, Action<bool> setCursorVisible = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _input = input;
            _plane = plane ?? throw new ArgumentNullException(nameof(plane));
            _reticle = reticle ?? throw new ArgumentNullException(nameof(reticle));
            _hip = hip;
            _ads = ads;
            _screenPosition = screenPosition ?? ZeroScreenPosition;   // 无注入（无设备/触屏形态）→ 屏幕零点
            _setCursorVisible = setCursorVisible ?? (visible => Cursor.visible = visible);
        }

        /// <summary>每渲染帧驱动（ProcedureBattle.OnUpdate 末尾——视图/动画状态已解析之后）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            bool blocked = _input != null && _input.IsBlocked;
            bool localReady = _view.HasLocalDisplay && _view.LocalEntityId != 0;
            bool show = localReady && !blocked;

            _reticle.gameObject.SetActive(show);
            _setCursorVisible(!show);                        // 准心替代光标；被拦/未建局时还光标（模态要点按钮）

            if (!show) return;

            Vector2 screen = _screenPosition();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_plane, screen, null, out Vector2 local))
                _reticle.localPosition = local;              // 锚点=中心：local 即"画布中心系"坐标（驱动写、对象零偏移）

            bool aiming = _view.TryGetSlot(_view.LocalEntityId, out int slot) && _view.IsAiming(slot);
            if (_hip != null) _hip.SetActive(!aiming);       // 形态组缺失 = 单形态退化（构造侧容忍 null）
            if (_ads != null) _ads.SetActive(aiming);
        }

        /// <summary>无注入位回退：屏幕零点（无设备/触屏形态——生产鼠标位经设备源适配器注入）。</summary>
        private static Vector2 ZeroScreenPosition() => Vector2.zero;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _setCursorVisible(true);                          // 流程离场/宿主关闭：还系统光标
        }
    }
}
