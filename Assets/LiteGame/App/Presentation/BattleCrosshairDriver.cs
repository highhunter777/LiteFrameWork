using System;
using LiteSim;
using LiteView;
using UnityEngine;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 对局准心驱动（战斗 HUD 第一件；纯 View 层——HUD 归 View、禁止回写 Sim，数据源单向流同前）：
    /// - **位置**：鼠标屏幕点 → <c>RectTransformUtility.ScreenPointToLocalPointInRectangle</c>
    ///   （Overlay 画布，camera=null，CanvasScaler 由该 API 一并处理）+ **后坐冲量残余偏移**（上跳）；
    ///   腰射时准心即瞄准落点（瞄准链 = 鼠标点 → 相机射线 → 交点，屏幕点回投仍是本位）。
    /// - **形态**：<see cref="SimView.IsAiming"/> 读本地预测态（连续位进 PredictedButtons，与本地手感
    ///   同帧）切腰射/瞄准两组；按实体 Id 解析槽位（不能把带版本的 Id 当下标）。
    /// - **可见性**：上下文门被拦（模态 UI 开）→ 藏准心还系统光标；本地表现未建 → 整体隐藏。
    ///   对局内 <c>Cursor.visible=false</c>，Dispose 恢复（流程离场必经 DetachView，光标不遗留隐藏态）。
    /// - **散布张开（静态）**：有效散布角（瞄准 = 表值 spread、腰射 ×<see cref="CombatConfig.HipSpreadFactor"/>）
    ///   → 准心整体缩放；**满张倍率按武器行**（tbcrosshair.static_max_scale——按武器表化；出件为单贴图
    ///   准心，"张开"= 整体缩放）。未装备/无散布信息 → 基线 <see cref="MinSpreadScale"/>（如实降级，不猜）。
    /// - **连发临时张开**：本地 Fire 帧事件（<see cref="IHitFeedbackConsumer"/>——<c>HitFeedbackDispatcher</c>
    ///   路由；静默门由上游 EventSink 保证，回滚重放段不重计入）每发 +<c>burst_growth</c>、钳连发上限，
    ///   停火按回正速率指数衰减回静态——半自动点射同逐发张开。
    /// - **后坐（准心冲量）**：每发一次上跳冲量（<c>kick</c> 像素）并入位置写入，按回弹速率指数回正
    ///   ——与连发张开各自独立节拍（连发张"开多大"、后坐管"跳多高"）。
    /// 手感数值全部表驱动（tbcrosshair 按武器一行；缺行/无表装配回落 <see cref="CrosshairFeel.Default"/>）；
    /// GM 面板「准心调参」经 <see cref="CrosshairTuning"/> 实时覆写定档（不落盘）。
    ///
    /// **不做**：命中标记（随帧事件批）、ADS 相机联动（手测项——不接相机时 ADS 准心跟鼠标，
    /// 锁屏心要等瞄准相机同轴后才成立）。
    ///
    /// 测试注：<paramref name="screenPosition"/>/<paramref name="setCursorVisible"/> 是注入位
    /// （EditMode 确定性驱动）。**鼠标位的生产来源是设备源适配器**
    /// （<see cref="NewInputIntentSource.MouseScreenPosition"/>——InputSystem 的 import 只许在
    /// 适配器边界内，R12；本层经注入消费，不直接认识 InputSystem）；注入位为 null 时回退屏幕零点。
    /// </summary>
    public sealed class BattleCrosshairDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>散布→准心张开的下界（有效散布角 ≤ 0 / 未装备 = 基线）。</summary>
        public const float MinSpreadScale = 1f;

        /// <summary>满张开参照角（度）：有效散布角达此值 → 满张（线性钳制）。步枪瞄准 1.2° ⇒ 0.24 满张、
        /// 腰射 4.8° ⇒ 0.96 满张；霰弹瞄准 3° ⇒ 0.6、腰射 12° ⇒ 钳满。**角度→倍率映射的参照常量**
        /// （实机目测定档）；按武器的"开多大"由表行 static_max_scale 承载，不在此再设档。</summary>
        public const float SpreadFullDegrees = 5f;

        /// <summary>连发张开回正速率（1/s，指数衰减）：残余按 e^(−rate·dt) 收敛。推导：600rpm
        /// （10 发/s）连发时稳态张开 ≈ 每发增量×射速/速率（0.05×10/3 ≈ 0.17 倍率），停手 ~0.3s
        /// 收回大半——"连射渐张、停手即收"的观感档（实机定档随调参面可再调）。</summary>
        public const float BurstRecoverPerSecond = 3f;

        /// <summary>后坐回弹速率（1/s，指数衰减）：冲量残余按 e^(−rate·dt) 回正。推导：与步枪射速
        /// 间隔（0.1s/发）同量级——连发时冲量叠成有限"抖动带"（稳态 ≈ 每发冲量×射速/速率）而非飞走。</summary>
        public const float KickRecoverPerSecond = 10f;

        private readonly SimView _view;
        private readonly IInputService _input;              // 上下文门被拦 → 藏准心还光标；null = 无输入形态（不拦）
        private readonly RectTransform _plane;               // Overlay 画布根（坐标换算基准；测试可用任意 RectTransform）
        private readonly RectTransform _reticle;
        private readonly GameObject _hip;
        private readonly GameObject _ads;
        private readonly CrosshairFeelBook _feel;            // null = 无表装配（灰盒/测试）→ 防御回落 Default
        private readonly Func<Vector2> _screenPosition;     // 注入位：默认读 InputSystem 鼠标
        private readonly Action<bool> _setCursorVisible;    // 注入位：默认写 Cursor.visible
        private float _lastScale = MinSpreadScale;          // 准心缩放脏位（值变化才写 Transform）
        private float _burstOpen;                            // 连发临时张开残余（倍率增量，≥0）
        private float _kick;                                 // 后坐冲量残余（画布像素，≥0——上跳偏移）
        private bool _disposed;

        public BattleCrosshairDriver(SimView view, IInputService input, RectTransform plane,
            RectTransform reticle, GameObject hip, GameObject ads,
            Func<Vector2> screenPosition = null, Action<bool> setCursorVisible = null,
            CrosshairFeelBook feel = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _input = input;
            _plane = plane ?? throw new ArgumentNullException(nameof(plane));
            _reticle = reticle ?? throw new ArgumentNullException(nameof(reticle));
            _hip = hip;
            _ads = ads;
            _feel = feel;
            _screenPosition = screenPosition ?? ZeroScreenPosition;   // 无注入（无设备/触屏形态）→ 屏幕零点
            _setCursorVisible = setCursorVisible ?? (visible => Cursor.visible = visible);
        }

        /// <summary>已计入张开/后坐的本地开火发数（诊断面——"开了几枪就该张几下"的对账读口）。</summary>
        public int ShotPulseCount { get; private set; }

        /// <summary>每渲染帧驱动（<paramref name="elapseSeconds"/> = 真实帧间隔——张开/后坐的回正节拍
        /// 走真实时间；含后坐偏移的位置每帧写——回弹是连续动画面，无脏位可省）。</summary>
        public void Tick(float elapseSeconds)
        {
            if (_disposed) return;

            bool blocked = _input != null && _input.IsBlocked;
            bool localReady = _view.HasLocalDisplay && _view.LocalEntityId != 0;
            bool show = localReady && !blocked;

            _reticle.gameObject.SetActive(show);
            _setCursorVisible(!show);                        // 准心替代光标；被拦/未建局时还光标（模态要点按钮）

            if (!show) return;

            int slot = -1;
            bool hasSlot = _view.LocalEntityId != 0 && _view.TryGetSlot(_view.LocalEntityId, out slot);
            bool aiming = hasSlot && _view.IsAiming(slot);
            if (_hip != null) _hip.SetActive(!aiming);       // 形态组缺失 = 单形态退化（构造侧容忍 null）
            if (_ads != null) _ads.SetActive(aiming);

            // 回正（dt 钳 ≥0：暂停/首帧不倒扣；指数衰减连续无突变；小残归零收口免常驻脏写）
            float dt = elapseSeconds > 0f ? elapseSeconds : 0f;
            if (dt > 0f)
            {
                _burstOpen *= Mathf.Exp(-CrosshairTuning.BurstRecoverOf(BurstRecoverPerSecond) * dt);
                _kick *= Mathf.Exp(-CrosshairTuning.KickRecoverOf(KickRecoverPerSecond) * dt);
                if (_burstOpen < 1e-4f) _burstOpen = 0f;
                if (_kick < 0.05f) _kick = 0f;
            }

            // 位置：鼠标屏幕点（画布中心系）+ 后坐上跳残余
            Vector2 screen = _screenPosition();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_plane, screen, null, out Vector2 local))
                _reticle.localPosition = new Vector3(local.x, local.y + _kick, 0f);

            // 缩放：静态散布张开（按武器行满张倍率）+ 连发增量，钳连发总上限（值变化才写 Transform）
            CrosshairFeel feel = hasSlot ? FeelOf(slot) : CrosshairFeel.Default;
            float scale = StaticScaleOf(in feel, hasSlot, slot, aiming) + _burstOpen;
            float cap = CrosshairTuning.BurstCapOf(feel.BurstCap);
            if (scale > cap) scale = cap;
            if (Mathf.Abs(scale - _lastScale) > 1e-4f)
            {
                _lastScale = scale;
                _reticle.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>本地 Fire 帧事件 → 连发张开增量 + 后坐冲量（其余事件/他人开火不吃——
        /// <c>HitFeedbackDispatcher</c> 已把"我开枪"解析为 <see cref="HitLocalRole.Caused"/>；
        /// 回滚重放段的静默门在其上游 EventSink 保证，重放不重计入）。</summary>
        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;
            if (ctx.Kind != FrameEventKind.Fire) return;
            if (ctx.LocalRole != HitLocalRole.Caused) return;
            if (ctx.Slot < 0) return;

            CrosshairFeel feel = FeelOf(ctx.Slot);
            // 增量累加，钳"以基线为参照"的增量上限（静态分量最小是基线 ⇒ 总值 ≤ 连发上限恒成立；
            // Tick 处对总值二次钳——瞄准/腰射切换静态变低时同样不越 cap）
            float maxOpen = Mathf.Max(0f, CrosshairTuning.BurstCapOf(feel.BurstCap) - MinSpreadScale);
            _burstOpen = Mathf.Min(_burstOpen + CrosshairTuning.BurstGrowthOf(feel.BurstGrowth), maxOpen);
            _kick += CrosshairTuning.KickOf(feel.Kick);
            ShotPulseCount++;
        }

        /// <summary>槽位当前武器行的手感（未装备/无表装配/表缺行 → <see cref="CrosshairFeel.Default"/>，不猜）。</summary>
        private CrosshairFeel FeelOf(int slot)
        {
            if (!_view.TryGetWeaponDefId(slot, out int defId)) return CrosshairFeel.Default;
            return _feel != null && _feel.TryGet(defId, out CrosshairFeel feel) ? feel : CrosshairFeel.Default;
        }

        /// <summary>静态散布张开：有效散布角 → 准心缩放（线性钳制；满张倍率 = 当前武器行
        /// static_max_scale——腰射倍率单源 <see cref="CombatConfig.HipSpreadFactor"/>）。</summary>
        private float StaticScaleOf(in CrosshairFeel feel, bool hasSlot, int slot, bool aiming)
        {
            if (!hasSlot || !_view.TryGetSpreadDegrees(slot, out float degrees)) return MinSpreadScale;
            float effective = aiming ? degrees : degrees * CombatConfig.HipSpreadFactor;
            float k = Mathf.Clamp01(effective / SpreadFullDegrees);
            float max = CrosshairTuning.StaticMaxOf(feel.StaticMaxScale);
            return MinSpreadScale + (max - MinSpreadScale) * k;
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
