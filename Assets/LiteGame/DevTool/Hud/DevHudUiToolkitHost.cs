#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace LiteGame
{
    /// <summary>
    /// DevHUD 的 UI Toolkit 渲染后端（无资产：运行时创建 `PanelSettings`，内联样式）。
    /// 字体走 TextCore 系统字体文件（微软雅黑/黑体/宋体/PingFang/Noto 候选链），
    /// 无可用 SDF 字体时抛异常，由 <see cref="DevHUD"/> 回退 IMGUI。
    /// 渲染模型：顶部状态行（可拖动面板）+ 可折叠段 + 每行一个 Label（按 <see cref="HudTone"/> 着色）+ 滚轮滚动。
    /// 折叠态由宿主（DevHUD）持有并持久化，本类只做显示与点击上报。
    /// 已知边界：UI Toolkit 面板渲染在 uGUI Screen Space Overlay 之下，`sortingOrder=10000` 高于产品现有最高 6000；
    /// 无 `ThemeStyleSheet` 时标准控件无样式，故折叠/拖拽/滚动都用 Label + PointerEvent 自实现。
    /// </summary>
    internal sealed class DevHudUiToolkitHost
    {
        private sealed class SectionView
        {
            public string Title;
            public VisualElement Root;
            public Label Header;
            public VisualElement Body;
            public readonly List<Label> Lines = new List<Label>(32);
            public bool Collapsed;
        }

        private static readonly Color NormalColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.68f, 1f);
        private static readonly Color WarnColor = new Color(1f, 0.82f, 0.40f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.42f, 0.42f, 1f);
        private static readonly Color HeaderColor = new Color(0.55f, 0.82f, 1f, 1f);

        private readonly GameObject _go;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly VisualElement _panel;
        private readonly VisualElement _content;
        private readonly Label _status;
        private readonly Label _toast;
        private readonly FontAsset _font;
        private readonly Dictionary<string, SectionView> _views = new Dictionary<string, SectionView>();
        private readonly List<SectionView> _ordered = new List<SectionView>(8);

        private IVisualElementScheduledItem _toastHide;
        private Action<string> _onToggle;
        private Action<int, int> _onMoved;
        private bool _dragging;
        private Vector3 _dragStart;
        private float _dragLeft;
        private float _dragTop;
        private float _dragCurrentLeft;
        private float _dragCurrentTop;
        private float _scrollY;
        private int _fontSize = -1;
        private bool _visible = true;

        public DevHudUiToolkitHost(Transform parent)
        {
            _font = CreateSdfFont();
            if (_font == null) throw new InvalidOperationException("没有可用的 SDF 系统字体");

            _go = new GameObject("[DevHUD-UI]");
            _go.transform.SetParent(parent, false);

            var document = _go.AddComponent<UIDocument>();

            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "DevHUD PanelSettings (runtime)";
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;
            _settings.sortingOrder = 10000f;   // 高于产品 uGUI Screen Space Overlay（当前最高 6000）
            document.panelSettings = _settings;
            document.sortingOrder = 10000f;

            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement { pickingMode = PickingMode.Position };
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8f;
            _panel.style.top = 88f;
            _panel.style.width = 480f;
            _panel.style.height = 640f;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _panel.style.paddingLeft = 8f;
            _panel.style.paddingRight = 8f;
            _panel.style.paddingTop = 6f;
            _panel.style.paddingBottom = 6f;
            _panel.style.overflow = Overflow.Hidden;
            _panel.RegisterCallback<WheelEvent>(OnWheel);

            _content = new VisualElement { pickingMode = PickingMode.Ignore };
            _content.style.position = Position.Absolute;
            _content.style.left = 0f;
            _content.style.right = 0f;
            _content.style.top = 0f;

            _status = CreateLabel(_content, MutedColor);
            _status.style.marginBottom = 4f;
            _status.pickingMode = PickingMode.Position;
            _status.RegisterCallback<PointerDownEvent>(OnDragStart);
            _status.RegisterCallback<PointerMoveEvent>(OnDragMove);
            _status.RegisterCallback<PointerUpEvent>(OnDragEnd);
            _panel.Add(_content);

            _toast = CreateLabel(_panel, WarnColor);
            _toast.style.position = Position.Absolute;
            _toast.style.left = 8f;
            _toast.style.right = 8f;
            _toast.style.bottom = 4f;
            _toast.style.display = DisplayStyle.None;

            _root.Add(_panel);
        }

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                _panel.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>一轮采样结果；折叠态/位置由宿主传入（持久化在 DevHUD）。</summary>
        public void Render(string status, IReadOnlyList<DevHudSectionData> sections,
            int width, int fontSize, int left, int top, float scale,
            ISet<string> collapsed, Action<string> onToggle, Action<int, int> onMoved)
        {
            _onToggle = onToggle;
            _onMoved = onMoved;

            ApplySize(width, left, top);
            ApplyFontSize(Mathf.RoundToInt(fontSize * Mathf.Clamp(scale, 0.5f, 3f)));
            _status.text = status ?? string.Empty;

            for (int i = 0; i < sections.Count; i++)
            {
                DevHudSectionData data = sections[i];
                SectionView view = EnsureView(data.Title);
                bool show = data.Visible && data.Lines.Count > 0;
                view.Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;

                view.Collapsed = collapsed != null && collapsed.Contains(data.Title);
                ApplyFold(view);
                UpdateLines(view, data.Lines);
            }

            _panel.schedule.Execute(ClampScroll).ExecuteLater(0);
        }

        public void ShowToast(string text)
        {
            _toast.text = text ?? string.Empty;
            _toast.style.display = DisplayStyle.Flex;
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(HideToast);
            _toastHide.ExecuteLater(2500);
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
        }

        private SectionView EnsureView(string title)
        {
            if (_views.TryGetValue(title, out SectionView view)) return view;

            view = new SectionView { Title = title };

            view.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Root.style.marginTop = 2f;

            view.Header = CreateLabel(view.Root, HeaderColor);
            view.Header.pickingMode = PickingMode.Position;
            view.Header.style.marginTop = 4f;
            // 普通 Label 不会自动收到 ClickEvent（那是 Clickable 的行为），用 PointerDown 做折叠
            view.Header.RegisterCallback<PointerDownEvent>(_ =>
            {
                view.Collapsed = !view.Collapsed;
                ApplyFold(view);
                _onToggle?.Invoke(view.Title);
                _panel.schedule.Execute(ClampScroll).ExecuteLater(0);
            });

            view.Body = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Body.style.paddingLeft = 6f;
            view.Root.Add(view.Body);

            _content.Add(view.Root);
            _views.Add(title, view);
            _ordered.Add(view);
            return view;
        }

        private Label CreateLabel(VisualElement parent, Color color)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = color;
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(_font);
            if (_fontSize > 0) label.style.fontSize = _fontSize;
            parent.Add(label);
            return label;
        }

        private void UpdateLines(SectionView view, IReadOnlyList<HudLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Label label = i < view.Lines.Count ? view.Lines[i] : AddLine(view);
                label.text = lines[i].Text;
                label.style.color = ToneColor(lines[i].Tone);
                label.style.display = DisplayStyle.Flex;
            }
            for (int i = lines.Count; i < view.Lines.Count; i++)
                view.Lines[i].style.display = DisplayStyle.None;
        }

        private Label AddLine(SectionView view)
        {
            Label label = CreateLabel(view.Body, NormalColor);
            label.style.marginBottom = 1f;
            view.Lines.Add(label);
            return label;
        }

        private void ApplyFold(SectionView view)
        {
            view.Header.text = (view.Collapsed ? "[+] " : "[-] ") + view.Title;
            view.Body.style.display = view.Collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ApplyFontSize(int fontSize)
        {
            if (fontSize <= 0 || fontSize == _fontSize) return;
            _fontSize = fontSize;
            _status.style.fontSize = fontSize;
            _toast.style.fontSize = fontSize;
            for (int i = 0; i < _ordered.Count; i++)
            {
                SectionView view = _ordered[i];
                view.Header.style.fontSize = fontSize;
                for (int j = 0; j < view.Lines.Count; j++) view.Lines[j].style.fontSize = fontSize;
            }
        }

        private void ApplySize(int width, int left, int top)
        {
            float screenW = Mathf.Max(320, Screen.width);
            float screenH = Mathf.Max(240, Screen.height);
            _root.style.width = screenW;
            _root.style.height = screenH;

            float w = Mathf.Clamp(width, 240, screenW - 16);
            float clampedTop = Mathf.Clamp(top, 0, screenH - 64);
            if (!_dragging)
            {
                _panel.style.left = Mathf.Clamp(left, 0, screenW - w);
                _panel.style.top = clampedTop;
            }
            float panelTop = _dragging ? _dragCurrentTop : clampedTop;
            _panel.style.width = w;
            _panel.style.height = Mathf.Max(200, screenH - panelTop - 12);
            _panel.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDragStart(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _dragging = true;
            _dragStart = evt.position;
            _dragLeft = _panel.layout.left;
            _dragTop = _panel.layout.top;
            _dragCurrentLeft = _dragLeft;
            _dragCurrentTop = _dragTop;
            _status.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;
            float screenW = Mathf.Max(320, Screen.width);
            float screenH = Mathf.Max(240, Screen.height);
            float w = _panel.layout.width;
            _dragCurrentLeft = Mathf.Clamp(_dragLeft + (evt.position.x - _dragStart.x), 0, screenW - w);
            _dragCurrentTop = Mathf.Clamp(_dragTop + (evt.position.y - _dragStart.y), 0, screenH - 64);
            _panel.style.left = _dragCurrentLeft;
            _panel.style.top = _dragCurrentTop;
            evt.StopPropagation();
        }

        private void OnDragEnd(PointerUpEvent evt)
        {
            if (!_dragging) return;
            _dragging = false;
            _status.ReleasePointer(evt.pointerId);
            _onMoved?.Invoke(Mathf.RoundToInt(_dragCurrentLeft), Mathf.RoundToInt(_dragCurrentTop));
            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            // UI Toolkit：delta.y > 0 = 向上滚；内容随之下移（top 增大），故取负
            _scrollY = Mathf.Clamp(_scrollY - evt.delta.y * 24f, 0f, MaxScroll());
            ApplyScroll();
            evt.StopPropagation();
        }

        private float MaxScroll()
        {
            float view = _panel.layout.height - 12f;
            return Mathf.Max(0f, _content.layout.height - view);
        }

        private void ClampScroll()
        {
            _scrollY = Mathf.Clamp(_scrollY, 0f, MaxScroll());
            ApplyScroll();
        }

        private void ApplyScroll() => _content.style.top = -_scrollY;

        private void HideToast() => _toast.style.display = DisplayStyle.None;

        private static Color ToneColor(HudTone tone)
        {
            switch (tone)
            {
                case HudTone.Muted: return MutedColor;
                case HudTone.Warn: return WarnColor;
                case HudTone.Error: return ErrorColor;
                default: return NormalColor;
            }
        }

        /// <summary>系统字体文件 → TextCore SDF（中文可读；失败返回 null，由调用方回退 IMGUI）。</summary>
        private static FontAsset CreateSdfFont()
        {
            foreach ((string path, int face) in FontCandidates())
            {
                try
                {
                    if (!System.IO.File.Exists(path)) continue;
                    FontAsset asset = FontAsset.CreateFontAsset(path, face, 90, 9,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                    if (asset != null) return asset;
                }
                catch (Exception)
                {
                    // 该字体文件不可读/格式不支持：尝试下一个
                }
            }
            return null;
        }

        private static (string path, int face)[] FontCandidates()
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor)
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                return new[]
                {
                    (System.IO.Path.Combine(dir, "msyh.ttc"), 0),   // 微软雅黑
                    (System.IO.Path.Combine(dir, "simhei.ttf"), 0), // 黑体
                    (System.IO.Path.Combine(dir, "simsun.ttc"), 0), // 宋体
                };
            }
            if (Application.platform == RuntimePlatform.OSXPlayer ||
                Application.platform == RuntimePlatform.OSXEditor)
            {
                return new[] { ("/System/Library/Fonts/PingFang.ttc", 0) };
            }
            return new[]
            {
                ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 0),
                ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 0),
            };
        }
    }
}
#endif
