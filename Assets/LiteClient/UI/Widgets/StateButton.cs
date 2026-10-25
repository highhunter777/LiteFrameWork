using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// 多态按钮：normal / pressed / disabled / selected 四态色彩 + 长按 + 连点保护。
    /// 不依赖 uGUI Selectable——色彩与语义全自持（灰盒：色块多态，贴图换皮随美术）。
    /// 事件：OnClick（经连点保护）、OnLongPress、OnSelectedChanged。
    /// 状态色令牌化（P2-3 B2）：各态色旁伴生 token 字段（空＝未令牌化用序列化色；装配期经 <see cref="IThemeBound"/> 注入解析）。
    /// </summary>
    public class StateButton : UIWidget, IPointerDownHandler, IPointerUpHandler, IThemeBound
    {
        [Header("多态色彩")]
        public Color NormalColor = new Color(0.25f, 0.45f, 0.75f);
        public Color PressedColor = new Color(0.15f, 0.3f, 0.55f);
        public Color DisabledColor = new Color(0.35f, 0.35f, 0.35f);
        public Color SelectedColor = new Color(0.9f, 0.7f, 0.2f);

        [Header("多态色彩令牌（空＝未令牌化，用上面序列化色）")]
        [Tooltip("常态色令牌（如 color.state.interact.normal）")]
        public string NormalColorToken;
        [Tooltip("按压色令牌")]
        public string PressedColorToken;
        [Tooltip("禁用色令牌")]
        public string DisabledColorToken;
        [Tooltip("选中色令牌")]
        public string SelectedColorToken;

        [Header("行为")]
        [Tooltip("长按触发时长（秒）")]
        public float LongPressSeconds = 0.6f;
        [Tooltip("连点保护间隔（秒）")]
        public float ClickCooldown = 0.15f;

        private Image _image;
        private bool _selected;
        private bool _pressed;
        private float _downTime;
        private float _lastClickTime;
        private bool _longPressFired;

        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; RefreshVisual(); OnSelectedChanged?.Invoke(value); }
        }

        public event Action OnClickEvent;
        public event Action OnLongPressEvent;
        public event Action<bool> OnSelectedChanged;

        protected override void Awake()
        {
            base.Awake();
            _image = GetComponent<Image>();
            RefreshVisual();
        }

        protected override void OnInteractableChanged(bool value) => RefreshVisual();

        private void RefreshVisual()
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_image == null) return;
            _image.color = !Interactable ? Pick(DisabledColor, DisabledColorToken)
                : _pressed ? Pick(PressedColor, PressedColorToken)
                : _selected ? Pick(SelectedColor, SelectedColorToken)
                : Pick(NormalColor, NormalColorToken);
        }

        /// <summary>取值序（P2-3 B2）：token 已填且可解析 → 令牌现值；否则序列化 fallback（未注入/未令牌化/未知——不猜色）。</summary>
        private Color Pick(Color fallback, string token)
            => _theme != null && !string.IsNullOrEmpty(token) && _theme.TryColor(token, out Color value)
                ? value
                : fallback;

        private UiStyleResolver _theme;

        /// <summary>主题注入（IThemeBound）：持解析器实例并刷新表现；未注入走序列化 fallback，行为不变。</summary>
        public void OnThemeBound(UiStyleResolver resolver)
        {
            _theme = resolver;
            RefreshVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable) return;
            _pressed = true;
            _downTime = Time.unscaledTime;
            _longPressFired = false;
            RefreshVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressed) return;
            _pressed = false;
            RefreshVisual();
            if (_longPressFired) return;                     // 长按已消费，不算点击
            if (Time.unscaledTime - _downTime >= LongPressSeconds) return;
            TryClick();
        }

        private void TryClick()
        {
            if (!Interactable) return;
            if (Time.unscaledTime - _lastClickTime < ClickCooldown) return;   // 连点保护：静默吞
            _lastClickTime = Time.unscaledTime;
            PlayPressFx();
            OnClickEvent?.Invoke();
        }

        private void Update()
        {
            if (!_pressed || _longPressFired || !Interactable) return;
            if (Time.unscaledTime - _downTime >= LongPressSeconds)
            {
                _longPressFired = true;
                OnLongPressEvent?.Invoke();
            }
        }

        protected override void OnClick() { }                // 基类默认微动效已并入 TryClick，避免双触发

    }
}
