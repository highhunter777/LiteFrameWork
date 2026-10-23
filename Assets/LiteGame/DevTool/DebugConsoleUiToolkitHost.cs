#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace LiteGame
{
    /// <summary>
    /// 开发控制台的 UI Toolkit 渲染后端（无资产：运行时创建 PanelSettings，内联样式）。
    /// 只渲染输出行、滚动与输入行；输入仍由 <see cref="DebugConsole"/> 经 DevHotkey 收口，
    /// 因此不依赖 UI Toolkit 焦点/主题（本工程运行期无 ThemeStyleSheet，标准控件无样式）。
    /// 排序 10100 高于 DevHUD（10000）与产品 uGUI 最高层（6000）。
    /// </summary>
    internal sealed class DebugConsoleUiToolkitHost
    {
        private const float SortingOrder = 10100f;
        private const int OutputFontSize = 13;
        private const int HintFontSize = 11;
        private const float ScrollStep = 24f;

        private static readonly Color LineColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color InputColor = new Color(0.75f, 0.95f, 1f, 1f);
        private static readonly Color HintColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        private readonly GameObject _go;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly VisualElement _panel;
        private readonly VisualElement _viewport;
        private readonly VisualElement _content;
        private readonly Label _hint;
        private readonly Label _input;
        private readonly FontAsset _font;
        private readonly List<Label> _lines = new List<Label>(64);

        private float _scrollY;
        private bool _visible;

        public DebugConsoleUiToolkitHost(Transform parent)
        {
            _font = DevUiToolkitFont.CreateSdfFont();
            if (_font == null)
                Log.Warning("开发控制台：未找到可用的 SDF 系统字体，中文可能显示为方块", LogTag.UI);

            _go = new GameObject("[DebugConsole-UI]");
            _go.transform.SetParent(parent, false);

            var document = _go.AddComponent<UIDocument>();

            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "DebugConsole PanelSettings (runtime)";
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;
            _settings.sortingOrder = SortingOrder;
            document.panelSettings = _settings;
            document.sortingOrder = SortingOrder;

            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement { name = "DebugConsole.Panel", pickingMode = PickingMode.Position };
            _panel.style.position = Position.Absolute;
            _panel.style.left = 0f;
            _panel.style.right = 0f;
            _panel.style.bottom = 0f;
            _panel.style.flexDirection = FlexDirection.Column;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);
            _panel.style.paddingLeft = 8f;
            _panel.style.paddingRight = 8f;
            _panel.style.paddingTop = 8f;
            _panel.style.paddingBottom = 6f;
            _panel.style.display = DisplayStyle.None;
            _panel.RegisterCallback<WheelEvent>(OnWheel);

            _viewport = new VisualElement { name = "DebugConsole.Viewport", pickingMode = PickingMode.Ignore };
            _viewport.style.flexGrow = 1f;
            _viewport.style.overflow = Overflow.Hidden;
            _panel.Add(_viewport);

            _content = new VisualElement { name = "DebugConsole.Content", pickingMode = PickingMode.Ignore };
            _content.style.position = Position.Absolute;
            _content.style.left = 0f;
            _content.style.right = 0f;
            _content.style.top = 0f;
            _viewport.Add(_content);

            _hint = CreateLabel(HintFontSize, HintColor);
            _hint.style.marginTop = 4f;
            _panel.Add(_hint);

            _input = CreateLabel(OutputFontSize, InputColor);
            _input.style.unityFontStyleAndWeight = FontStyle.Bold;
            _input.style.marginTop = 2f;
            _panel.Add(_input);

            _root.Add(_panel);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Render(IReadOnlyList<string> lines, string input, string hint, bool textOwned, bool scrollToBottom)
        {
            ApplySize();
            UpdateLines(lines);
            _hint.text = hint ?? string.Empty;
            _input.text = "> " + (input ?? string.Empty) + (textOwned ? "_" : string.Empty);

            if (scrollToBottom) _panel.schedule.Execute(ScrollToBottom).ExecuteLater(0);
            else _panel.schedule.Execute(ClampScroll).ExecuteLater(0);
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
        }

        private Label CreateLabel(int fontSize, Color color)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = fontSize;
            label.style.color = color;
            if (_font != null) label.style.unityFontDefinition = FontDefinition.FromSDFFont(_font);
            return label;
        }

        private void UpdateLines(IReadOnlyList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Label label = i < _lines.Count ? _lines[i] : AddLine();
                label.text = lines[i] ?? string.Empty;
                label.style.display = DisplayStyle.Flex;
            }
            for (int i = lines.Count; i < _lines.Count; i++)
                _lines[i].style.display = DisplayStyle.None;
        }

        private Label AddLine()
        {
            Label label = CreateLabel(OutputFontSize, LineColor);
            label.style.marginBottom = 1f;
            _content.Add(label);
            _lines.Add(label);
            return label;
        }

        private void ApplySize()
        {
            float screenW = Mathf.Max(320f, Screen.width);
            float screenH = Mathf.Max(240f, Screen.height);
            _root.style.width = screenW;
            _root.style.height = screenH;
            _panel.style.width = screenW;
            _panel.style.height = Mathf.Clamp(screenH * 0.45f, 220f, Mathf.Max(220f, screenH - 40f));
            _panel.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnWheel(WheelEvent evt)
        {
            // UI Toolkit：delta.y > 0 = 向上滚；内容随之下移（top 增大），故取负
            _scrollY = Mathf.Clamp(_scrollY - evt.delta.y * ScrollStep, 0f, MaxScroll());
            ApplyScroll();
            evt.StopPropagation();
        }

        private float MaxScroll()
            => Mathf.Max(0f, _content.layout.height - _viewport.layout.height);

        private void ScrollToBottom()
        {
            _scrollY = MaxScroll();
            ApplyScroll();
        }

        private void ClampScroll()
        {
            _scrollY = Mathf.Clamp(_scrollY, 0f, MaxScroll());
            ApplyScroll();
        }

        private void ApplyScroll() => _content.style.top = -_scrollY;
    }
}
#endif
