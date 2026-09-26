using System;
using System.Threading;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 界面运行时实例（M4 §2.1）：状态机 + 逻辑持有 + 画布就位。
    /// 状态迁移全部走本类守卫方法（非法迁移当场抛——fail-fast 精神 §3.4）；
    /// 逻辑回调统一经 SafeCall（单个回调抛 = 该界面降级，不炸壳——错误语义同事件桥）。
    /// 画布契约：prefab 根自带 Canvas（overrideSorting）+ CanvasGroup 最佳；缺失则壳补齐，sortingOrder 由层级组分配。
    /// 《UI框架总设计》§4 修订：首次与复用共用 <see cref="PrepareForShow"/> 复位；
    /// Covered/Paused 属"仍打开"，同样可关闭；首次 OnInit/OnShow 失败对外报失败以便壳回滚。
    /// </summary>
    public sealed class UIForm
    {
        public int Id { get; }
        public UIFormInfo Info { get; }
        public UIFormState State { get; private set; }
        public GameObject Root { get; }
        public Canvas Canvas { get; }
        public CanvasGroup CanvasGroup { get; }
        public IUIFormLogic Logic { get; set; } = NullUIFormLogic.Instance;

        /// <summary>位置基线（实例化时刻的 anchoredPosition，含 prefab 作者意图）——复用/重开复位用。</summary>
        private readonly Vector2 _baselinePos;

        // ---- 展示作用域（U1-①：《UI框架总设计》§4.4——"每次打开到关闭"的 CTS 与代次）----

        private int _displayGeneration;

        /// <summary>本次展示的 <see cref="ClientScope"/>（每次打开重建、关闭 Dispose）。
        ///
        /// **为什么是 ClientScope 而不是裸 CTS**（《客户端总设计》§6.2 UI 行"每次展示另有子作用域"）：
        /// 通用壳的展示期资源不止取消令牌一件——订阅袋、展示期任务都要**同一时点、逆序**收尾。
        /// 原先由 <see cref="EnterClosing"/> 手写"先取消再 Dispose 袋"两行，每加一类展示期资源就要
        /// 再加一行、且顺序靠人记；改由作用域托管后，登记什么就按 LIFO 收什么（§4 原则 4：
        /// 所有长生命周期对象必须有唯一 Owner）。
        ///
        /// 所有权仍在<b>实例</b>（§6.2"实例创建至销毁；每次展示另有子作用域"）：本作用域是
        /// <see cref="UIForm"/> 的内部展示状态，**不是**池化实例的租约——租约覆盖缓存实例寿命，
        /// 由 <c>UIService</c> 持到真正销毁。
        ///
        /// 不传父作用域：展示的存续由本实例的状态机决定（开→关），不由父级取消驱动——
        /// 父级驱动的批量清理走 <c>UIService.CloseAllOpen</c>，逐界面正常关闭路径，两条路不混。</summary>
        private ClientScope _displayScope;

        /// <summary>展示代次（每次打开递增）：异步图片/网络结果/动画回调**写入前验证**
        /// （<see cref="IsDisplayCurrent"/>）——复用后的新页面不被旧回调污染（§4.3）。</summary>
        public int DisplayGeneration => _displayGeneration;

        /// <summary>本次展示的取消令牌（展示作用域）：打开时创建、关闭时取消——页面内跨帧异步
        /// （图标加载/网络请求/延时任务）绑定它，界面关闭即级联取消。池化复用安全（每次打开重建）。</summary>
        public CancellationToken DisplayToken => _displayScope?.Token ?? CancellationToken.None;

        /// <summary>代次核验：迟到结果凭旧代次写入 = 污染复用后的新页面——必须丢弃（§4.3）。</summary>
        public bool IsDisplayCurrent(int generation)
            => generation == _displayGeneration && IsOpen;

        public UIForm(UIFormInfo info, GameObject root)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Id = info.Id;
            Root = root ? root : throw new ArgumentNullException(nameof(root));

            // 缺失即 fail-fast（§7 视觉单一来源 / 制作规范 §2"页面根具有 Canvas、CanvasGroup"）：
            // 旧实现在此处 AddComponent 兜底，会把"prefab 装配错误"静默修好——运行时补的 Canvas
            // 绕过统一根缩放与排序配置，缺陷被藏到表现层才发现。装配错误必须在装配期当场暴露。
            Canvas = root.GetComponent<Canvas>()
                ?? throw new InvalidOperationException(
                    $"表单[{info.Id}] prefab 缺少 Canvas——页面根必须自带（制作规范 §2；运行时不得补建，§7）");
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.overrideSorting = true;
            CanvasGroup = root.GetComponent<CanvasGroup>()
                ?? throw new InvalidOperationException(
                    $"表单[{info.Id}] prefab 缺少 CanvasGroup——页面根必须自带（制作规范 §2；运行时不得补建，§7）");
            _baselinePos = root.transform is RectTransform rt ? rt.anchoredPosition : Vector2.zero;
        }

        // ---- 生命周期迁移（UIService 编排调用；守卫 + 逻辑回调定序）----

        /// <summary>
        /// 显示前复位（§4.2，首次与复用共用）：激活对象、复位 alpha 与位置基线（默认离场表现会把
        /// alpha 收成 0，不复位则复用后"页在但看不见"）。
        /// 输入态此处只回到基线；"转场锁 / 模态 / 暂停"的综合求解属 U1 的输入协调者。
        /// </summary>
        internal void PrepareForShow()
        {
            Root.SetActive(true);
            BeginDisplay();                                    // 展示代次递增 + 新展示作用域 CTS（每次打开一份）
            if (CanvasGroup != null)
            {
                CanvasGroup.alpha = 1f;
                CanvasGroup.interactable = true;
                CanvasGroup.blocksRaycasts = true;
            }
            if (Root.transform is RectTransform rt) rt.anchoredPosition = _baselinePos;
        }

        /// <summary>开启一次展示（PrepareForShow 内调用）：代次递增 + 重建展示作用域（旧的已在上次关闭释放）。</summary>
        private void BeginDisplay()
        {
            _displayGeneration++;
            _displayScope?.Dispose();                          // 防御：上次关闭未走 EnterClosing 时兜底
            _displayScope = new ClientScope($"UIForm[{Id}].Display#{_displayGeneration}");
        }

        /// <summary>首次打开：Loading → OnInit → OnShow → Active。
        /// 返回 false = OnInit/OnShow 抛异常（异常已被 SafeCall 记录）——调用方必须回滚清理，
        /// 不得以 Active + 空逻辑伪装成功（§4.1）。</summary>
        internal bool EnterActiveFromLoading(IUIData data)
        {
            Transit(UIFormState.Loading, UIFormState.Active);
            if (!SafeCall.TryInvoke(() => Logic.OnInit(this, data), $"UIForm[{Id}].OnInit")) return false;
            if (!SafeCall.TryInvoke(() => Logic.OnShow(data), $"UIForm[{Id}].OnShow")) return false;
            return true;
        }

        /// <summary>池化复用：Recycled →（SetActive true）→ OnShow → Active。OnInit 不重跑。</summary>
        internal void EnterActiveFromRecycled(IUIData data)
        {
            Transit(UIFormState.Recycled, UIFormState.Active);
            Root.SetActive(true);
            if (NeedsReinit)
            {
                NeedsReinit = false;
                SafeCall.Invoke(() => Logic.OnInit(this, data), $"UIForm[{Id}].OnInit(换表重建)");
            }
            SafeCall.Invoke(() => Logic.OnShow(data), $"UIForm[{Id}].OnShow");
        }

        /// <summary>复用前是否需补跑 OnInit（仅在"逻辑换表"后置真，见 <see cref="UIService.MarkLogicStale"/>）。</summary>
        internal bool NeedsReinit { get; set; }

        /// <summary>是否仍逻辑打开（Active/Covered/Paused）：§4.1 展示状态独立于"可否关闭"——
        /// 遮盖与暂停都不改变关得掉的事实。</summary>
        public bool IsOpen => State is UIFormState.Active or UIFormState.Covered or UIFormState.Paused;

        /// <summary>逻辑落空（§10.2 env 重建 / 开门前置）：解绑并释放旧 Lua 引用、逻辑置空，
        /// 下次显示前按当前注册表重新解析——否则界面会拿已 Dispose 的 LuaFunction 打进死环境。</summary>
        internal void DropLogic()
        {
            // 经通用钩子释放——通用 UI 运行时**不认识任何脚本适配器**（§5.1 逻辑边界）；
            // 需要归还外部引用的逻辑自行覆写 IUIFormLogic.Release。
            Logic?.Release();
            Logic = NullUIFormLogic.Instance;
        }

        /// <summary>打开失败回滚（§4.1）：释放逻辑与 Lua 引用并销毁对象——半成品不入池、不进字典。</summary>
        internal void DisposeFailedOpen()
        {
            DropLogic();
            if (Root == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(Root);    // EditMode 下 Destroy 只记 error 不生效（L2 用例依赖）
                return;
            }
#endif
            UnityEngine.Object.Destroy(Root);
        }

        internal void EnterPaused()
        {
            Transit(UIFormState.Active, UIFormState.Paused);
            if (CanvasGroup != null) CanvasGroup.interactable = false;   // U1-③：输入协调——暂停页锁定交互（§6.2）
            SafeCall.Invoke(() => Logic.OnPause(), $"UIForm[{Id}].OnPause");
        }

        internal void EnterActiveFromPaused()
        {
            Transit(UIFormState.Paused, UIFormState.Active);
            if (CanvasGroup != null) CanvasGroup.interactable = true;    // 恢复为协调计算值（无转场锁时即开）
            SafeCall.Invoke(() => Logic.OnShow(null), $"UIForm[{Id}].OnShow");
        }

        internal void EnterCovered()
        {
            Transit(UIFormState.Active, UIFormState.Covered);
            SafeCall.Invoke(() => Logic.OnCover(), $"UIForm[{Id}].OnCover");
        }

        internal void EnterActiveFromCovered()
        {
            Transit(UIFormState.Covered, UIFormState.Active);
            SafeCall.Invoke(() => Logic.OnReveal(), $"UIForm[{Id}].OnReveal");
        }

        /// <summary>开始关闭（Active/Covered/Paused 三态均可）：OnHide 已调，停在 Closing——
        /// §2.2 转场策略可在此等待动画后再 Recycle。遮盖/暂停不是"关不掉"的理由（§4.1）。</summary>
        internal void EnterClosing()
        {
            if (!IsOpen)
                throw new InvalidOperationException(
                    $"UIForm[{Id}] 非法状态迁移:{State} → Closing（仅 Active/Covered/Paused 可关闭）");
            State = UIFormState.Closing;
            SafeCall.Invoke(() => Logic.OnHide(), $"UIForm[{Id}].OnHide");

            // 展示作用域收尾（§4.4 / §6.2 UI 行）：一次 Dispose 取消本次打开的在途异步
            // （图标/请求/延时任务级联取消）**并**逆序释放登记项（订阅袋等）——
            // 顺序由作用域保证，不再靠本方法手写。幂等，池化复用安全。
            _displayScope?.Dispose();
            _displayScope = null;                      // 置空以支持池化复用：下次显示时重建

            // 界面级订阅清零：OnHide 之后、落池之前（与按钮 UnbindAll 同一时点语义——
            // 池化复用跨环境的安全垫，防"回收期间事件打进已关闭界面"）。
            _subs?.Dispose();
            _subs = null;                              // 置空以支持池化复用：下次显示时按需重建
        }

        /// <summary>
        /// 界面级订阅袋：订阅的事件随界面关闭自动清零（**登记进展示作用域**，随
        /// <see cref="EnterClosing"/> 的作用域 Dispose 一并释放），池化复用安全。
        /// 用法：<c>form.Subscriptions.Add(events.Subscribe&lt;XxxEvent&gt;(OnXxx));</c>
        /// C# 侧界面逻辑订阅事件一律挂这里，不要裸订阅——否则关界面后通道仍持回调（泄漏 +
        /// 复用后回调打进新界面）。Dispose 后误用会当场抛 ObjectDisposedException。
        /// （Lua 侧界面逻辑不适用：走 <c>events.on</c> 并在界面 OnHide 里调用其返回的注销委托。）
        /// **归还期护栏**：界面处于 Closing/Recycled 时取袋子 = 往已关闭界面塞订阅（必然泄漏，因为
        /// 袋子不会再被 Dispose）→ Debug 三宏下当场抛，release 记错误。
        /// </summary>
        public SubscriptionBag Subscriptions
        {
            get
            {
                if (State == UIFormState.Closing || State == UIFormState.Recycled)
                {
                    const string msg = "已关闭/已回收的界面不得再订阅事件（订阅袋不会再被释放 → 必然泄漏）";
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                    throw new InvalidOperationException($"UIForm[{Id}]: {msg}");
#else
                    Log.Error($"UIForm[{Id}]: {msg}", "UI");
#endif
                }
                // 每次展示一枚新袋、登记进本次展示作用域——关闭时随作用域逆序释放，
                // 不跨展示复用（复用即"旧订阅打进新页面"的来源）。同一展示内重复取到同一枚（惰性）。
                return _subs ??= new SubscriptionBag();
            }
        }

        /// <summary>本次展示的订阅袋（惰性；随 <see cref="EnterClosing"/> 的作用域释放置空）。</summary>
        private SubscriptionBag _subs;

        /// <summary>落池：Closing → Recycled（SetActive false）。</summary>
        internal void Recycle()
        {
            Transit(UIFormState.Closing, UIFormState.Recycled);
            Root.SetActive(false);
        }

        /// <summary>实例真正销毁（U1-②/UI-06：缓存淘汰/显式 Destroy/Shutdown 终态）：
        /// 释放逻辑与 Lua 引用、取消展示令牌、标记 Disposed、销毁 GameObject——租约由 UIService 释放。
        /// 之后本 UIForm 对象不可再复用（再次打开 = 全新实例）。</summary>
        internal void DestroyInstance()
        {
            DropLogic();
            _displayScope?.Dispose();                  // 展示作用域收尾（同 EnterClosing；幂等）
            _displayScope = null;
            State = UIFormState.Disposed;
            if (Root == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(Root);    // EditMode 下 Destroy 只记 error 不生效（L2 用例依赖）
                return;
            }
#endif
            UnityEngine.Object.Destroy(Root);
        }

        /// <summary>OnUpdate 派发（UIService.Tick，仅 Active 态会走到这里）。</summary>
        internal void RaiseUpdate(float deltaTime)
        {
            SafeCall.Invoke(() => Logic.OnUpdate(deltaTime), $"UIForm[{Id}].OnUpdate");
        }

        internal void AssignDepth(int sortingOrder)
        {
            if (Canvas != null) Canvas.sortingOrder = sortingOrder;
        }

        private void Transit(UIFormState from, UIFormState to)
        {
            if (State != from)
                throw new InvalidOperationException($"UIForm[{Id}] 非法状态迁移:{State} → {to}（期望自 {from}）");
            State = to;
        }
    }
}
