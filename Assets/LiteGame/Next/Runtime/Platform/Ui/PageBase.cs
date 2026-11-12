using System;
using System.Collections.Generic;
using System.Threading;
using LiteFrameworkNext;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>页面基类：创建期扫描自身子树 BindName 建控件表；UiRuntime 先注入句柄/依赖再激活，页面只经 Handle 取生命周期。</summary>
    public abstract class PageBase : MonoBehaviour
    {
        private readonly Dictionary<string, Component> _binds = new Dictionary<string, Component>(StringComparer.Ordinal);
        private readonly List<ClickBinding> _clicks = new List<ClickBinding>();
        private PageHandle _handle;
        private bool _created;
        private bool _disposed;

        /// <summary>本页句柄（Lifetime/展示代次）；由 UiRuntime 在激活前注入。</summary>
        public PageHandle Handle => _handle;

        /// <summary>页面元数据；未打开时为 null。</summary>
        /// <summary>当前展示的页面元数据（未打开为 null）；页面类另持静态 `Spec` 定义，二者不重名。</summary>
        public PageSpec CurrentSpec => _handle?.Spec;

        /// <summary>页面级生命周期；未打开时为 null，关闭后已释放。</summary>
        public Lifetime Lifetime => _handle?.Lifetime;

        /// <summary>页面取消令牌；未打开或已关闭时为 None（不抛异常）。</summary>
        public CancellationToken CancellationToken =>
            _handle == null || _handle.IsClosed ? CancellationToken.None : _handle.Lifetime.Token;

        /// <summary>创建期钩子：控件表已建好后调用一次；子类重写做一次性初始化（不必调 base）。</summary>
        protected virtual void OnCreate()
        {
        }

        /// <summary>每次展示调用（展示代次已递增）；依赖已注入、对象已激活。</summary>
        protected virtual void OnShow()
        {
        }

        /// <summary>每次隐藏调用；已登记的按钮监听在 OnHide 之前解绑。</summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>页面销毁前调用一次（关闭销毁 / 缓存淘汰 / 运行时关闭）。</summary>
        protected virtual void OnDispose()
        {
        }

        /// <summary>展示期间的逐帧驱动（由 UiRuntime.Tick 转发）。</summary>
        protected virtual void OnTick(float deltaTime)
        {
        }

        // Unity 生命周期仅作兜底：页面由 UiRuntime 显式 Prepare/DisposePage；子类覆写 Awake/OnDestroy 时须调 base。
        private void Awake() => EnsureBindings();

        private void OnDestroy() => DisposePage();

        /// <summary>按 BindName 名取控件；缺名或类型不符抛 InvalidOperationException（程序员错误 fail-fast）。</summary>
        protected T Bind<T>(string name) where T : Component
        {
            Component owner = Require(name);
            T component = owner.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException("页面 " + GetType().Name + " 的控件“" + name + "”不是 "
                    + typeof(T).Name + "（实际对象：" + owner.gameObject.name + "）");
            return component;
        }

        /// <summary>为按钮登记点击回调；本次展示结束时（OnHide 前）自动解绑。</summary>
        protected void OnClick(string name, Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Button button = Bind<Button>(name);
            UnityAction listener = () => handler();
            button.onClick.AddListener(listener);
            _clicks.Add(new ClickBinding(button, listener));
        }

        /// <summary>设置文本控件内容（TMP 优先，兼容 uGUI Text）。</summary>
        protected void SetText(string name, string value)
        {
            Component owner = Require(name);
            if (owner.TryGetComponent(out TMP_Text tmpText))
            {
                tmpText.text = value;
                return;
            }

            if (owner.TryGetComponent(out Text uiText))
            {
                uiText.text = value;
                return;
            }

            throw new InvalidOperationException("页面 " + GetType().Name + " 的控件“" + name + "”没有文本组件（TMP_Text/Text）");
        }

        /// <summary>显示/隐藏控件对象本身。</summary>
        protected void SetVisible(string name, bool visible) => Require(name).gameObject.SetActive(visible);

        /// <summary>设置可交互控件的 interactable 位（Selectable 家族：Button/Toggle/InputField 等）。</summary>
        protected void SetInteractable(string name, bool interactable) => Bind<Selectable>(name).interactable = interactable;

        internal void Attach(PageHandle handle) => _handle = handle;

        internal void Prepare()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            EnsureBindings();
        }

        internal void Show() => OnShow();

        internal void Hide()
        {
            ClearClicks();
            OnHide();
        }

        internal void Tick(float deltaTime)
        {
            if (_disposed) return;
            OnTick(deltaTime);
        }

        internal void DisposePage()
        {
            if (_disposed) return;
            _disposed = true;
            ClearClicks();
            try
            {
                OnDispose();
            }
            finally
            {
                _binds.Clear();
                _handle = null;
            }
        }

        private void EnsureBindings()
        {
            if (_created) return;
            _created = true;

            BindName[] markers = GetComponentsInChildren<BindName>(true);
            for (int index = 0; index < markers.Length; index++)
            {
                BindName marker = markers[index];
                string name = marker.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (_binds.ContainsKey(name))
                    throw new InvalidOperationException("页面 " + GetType().Name + " 的 BindName 重名：“"
                        + name + "”（同一页面内控件名必须唯一）");
                _binds.Add(name, marker);
            }

            OnCreate();
        }

        /// <summary>控件是否已绑定（可选控件先探测再取，避免缺件抛异常）。</summary>
        protected bool HasBinding(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            EnsureBindings();
            return _binds.ContainsKey(name);
        }

        private Component Require(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("控件名不能为空", nameof(name));
            EnsureBindings();
            if (!_binds.TryGetValue(name, out Component owner))
                throw new InvalidOperationException("页面 " + GetType().Name + " 缺少绑定控件：“"
                    + name + "”（检查 BindName 是否挂在控件对象上）");
            return owner;
        }

        private void ClearClicks()
        {
            for (int index = 0; index < _clicks.Count; index++)
            {
                Button button = _clicks[index].Button;
                if (button != null) button.onClick.RemoveListener(_clicks[index].Listener);
            }

            _clicks.Clear();
        }

        private readonly struct ClickBinding
        {
            public ClickBinding(Button button, UnityAction listener)
            {
                Button = button;
                Listener = listener;
            }

            public Button Button { get; }

            public UnityAction Listener { get; }
        }
    }
}
