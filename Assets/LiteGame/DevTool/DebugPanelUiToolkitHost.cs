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
    /// 开发面板的 UI Toolkit 渲染后端（无资产：运行时 PanelSettings + 内联样式）。
    /// 只渲染常用动作与 <see cref="TestModeOptions"/> 的即时开关；动作/读写全部由 <see cref="DebugPanel"/> 经 DebugKernel 执行。
    /// 运行期无 ThemeStyleSheet，故用 Button/Label 自绘，不依赖标准控件主题。
    /// </summary>
    internal sealed class DebugPanelUiToolkitHost
    {
        private const float SortingOrder = 10200f;
        private const int FontSize = 13;
        private const int HintFontSize = 11;

        private static readonly Color NormalColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.72f, 0.75f, 0.78f, 1f);
        private static readonly Color OnColor = new Color(0.55f, 0.95f, 0.62f, 1f);
        private static readonly Color AccentColor = new Color(0.55f, 0.82f, 1f, 1f);

        private readonly GameObject _go;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly VisualElement _panel;
        private readonly Label _status;
        private readonly FontAsset _font;
        private readonly IReadOnlyList<TestModeOptions.Info> _options;
        private readonly Dictionary<TestModeOptions.Id, Button> _toggleButtons =
            new Dictionary<TestModeOptions.Id, Button>();
        private readonly Dictionary<string, TunableRow> _tunableRows =
            new Dictionary<string, TunableRow>();
        private readonly VisualElement _tunablesSection;
        private readonly VisualElement _tunablesBody;
        private readonly Button _tunablesHeader;
        private Action<DebugTunable, float> _onTunableSet;
        private Action<DebugTunable> _onTunableReset;
        private bool _tunablesExpanded = true;

        private sealed class TunableRow
        {
            public DebugTunable Descriptor;
            public Label Value;
            public VisualElement Fill;
            public Button Toggle;
            public bool Dragging;
        }

        public DebugPanelUiToolkitHost(Transform parent, IReadOnlyList<TestModeOptions.Info> options,
            Action<TestModeOptions.Id> onToggle, Action onEnter, Action onExit, Action onTeleport,
            IReadOnlyList<DebugTunable> tunables = null,
            Action<DebugTunable, float> onTunableSet = null,
            Action<DebugTunable> onTunableReset = null)
        {
            _font = DevUiToolkitFont.CreateSdfFont();
            if (_font == null)
                Log.Warning("开发面板：未找到可用的 SDF 系统字体，中文可能显示为方块", LogTag.UI);
            _options = options;

            _go = new GameObject("[DebugPanel-UI]");
            _go.transform.SetParent(parent, false);

            var document = _go.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "DebugPanel PanelSettings (runtime)";
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;
            _settings.sortingOrder = SortingOrder;
            document.panelSettings = _settings;
            document.sortingOrder = SortingOrder;

            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement { pickingMode = PickingMode.Position };
            _panel.style.position = Position.Absolute;
            _panel.style.top = 8f;
            _panel.style.right = 8f;
            _panel.style.width = 360f;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.86f);
            _panel.style.paddingLeft = 10f;
            _panel.style.paddingRight = 10f;
            _panel.style.paddingTop = 8f;
            _panel.style.paddingBottom = 8f;
            _panel.style.borderLeftWidth = 1f;
            _panel.style.borderRightWidth = 1f;
            _panel.style.borderTopWidth = 1f;
            _panel.style.borderBottomWidth = 1f;
            _panel.style.borderLeftColor = new Color(0.35f, 0.42f, 0.5f, 1f);
            _panel.style.borderRightColor = new Color(0.35f, 0.42f, 0.5f, 1f);
            _panel.style.borderTopColor = new Color(0.35f, 0.42f, 0.5f, 1f);
            _panel.style.borderBottomColor = new Color(0.35f, 0.42f, 0.5f, 1f);
            _panel.style.display = DisplayStyle.None;

            AddLabel(_panel, "开发面板", AccentColor, FontSize + 1, true);
            _status = AddLabel(_panel, "调试内核未装配", MutedColor, FontSize, false);
            _status.style.marginTop = 2f;
            _status.style.marginBottom = 6f;

            AddLabel(_panel, "本地测试模式", MutedColor, HintFontSize, true);
            var actions = new VisualElement { pickingMode = PickingMode.Ignore };
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginBottom = 6f;
            AddActionButton(actions, "进入", onEnter, AccentColor);
            AddActionButton(actions, "退出", onExit, NormalColor);
            AddActionButton(actions, "传送到准心", onTeleport, NormalColor);
            _panel.Add(actions);

            _tunablesSection = new VisualElement { pickingMode = PickingMode.Ignore };
            _tunablesSection.style.marginBottom = 4f;
            _tunablesHeader = CreateButton(ToggleTunables);
            _tunablesHeader.text = "▾ 快捷调参";
            _tunablesHeader.style.marginBottom = 2f;
            _tunablesSection.Add(_tunablesHeader);
            _tunablesBody = new VisualElement { pickingMode = PickingMode.Ignore };
            _tunablesSection.Add(_tunablesBody);
            _panel.Add(_tunablesSection);
            SetTunables(tunables, onTunableSet, onTunableReset);

            AddLabel(_panel, "即时开关", MutedColor, HintFontSize, true);
            for (int i = 0; i < options.Count; i++)
            {
                TestModeOptions.Info option = options[i];
                Button button = CreateButton(onToggle == null ? (Action)null : () => onToggle(option.Id));
                button.text = "☐ " + ShortLabel(option.Label);
                button.tooltip = option.Label;
                button.style.color = NormalColor;
                button.style.marginBottom = 2f;
                _panel.Add(button);
                _toggleButtons[option.Id] = button;
            }

            var hint = AddLabel(_panel, "F11 关闭 · F12 控制台", MutedColor, HintFontSize, false);
            hint.style.marginTop = 6f;

            _root.Add(_panel);
        }

        public void SetVisible(bool visible)
            => _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetStatus(string status)
        {
            _status.text = status ?? string.Empty;
            _status.style.color = MutedColor;
        }

        public void SetStatus(string status, bool error)
        {
            _status.text = status ?? string.Empty;
            _status.style.color = error ? new Color(1f, 0.55f, 0.55f, 1f) : MutedColor;
        }

        public void SetOption(TestModeOptions.Id id, bool value)
        {
            if (!_toggleButtons.TryGetValue(id, out Button button)) return;
            TestModeOptions.Info info = FindInfo(id);
            button.text = (value ? "☑ " : "☐ ") + ShortLabel(info.Label);
            button.style.color = value ? OnColor : NormalColor;
        }

        public void SetTunables(IReadOnlyList<DebugTunable> tunables,
            Action<DebugTunable, float> onSet, Action<DebugTunable> onReset)
        {
            _onTunableSet = onSet;
            _onTunableReset = onReset;
            _tunableRows.Clear();
            _tunablesBody.Clear();

            if (tunables != null)
            {
                for (int i = 0; i < tunables.Count; i++)
                    AddTunableRow(tunables[i]);
            }

            _tunablesSection.style.display = _tunableRows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ToggleTunables(_tunablesExpanded);
        }

        public void SetTunableValue(string id, float value)
        {
            if (!_tunableRows.TryGetValue(id, out TunableRow row)) return;
            row.Value.text = DebugTunableText.Format(row.Descriptor, value);
            if (row.Toggle != null)
            {
                bool on = value >= 0.5f;
                row.Toggle.text = on ? "☑" : "☐";
                row.Toggle.style.color = on ? OnColor : NormalColor;
            }
            if (row.Fill != null)
            {
                float span = row.Descriptor.Max - row.Descriptor.Min;
                float pct = span <= 0f ? 0f : Mathf.Clamp01((value - row.Descriptor.Min) / span);
                row.Fill.style.width = Length.Percent(pct * 100f);
            }
        }

        private void ToggleTunables()
        {
            _tunablesExpanded = !_tunablesExpanded;
            ToggleTunables(_tunablesExpanded);
        }

        private void ToggleTunables(bool expanded)
        {
            _tunablesExpanded = expanded;
            _tunablesBody.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _tunablesHeader.text = expanded ? "▾ 快捷调参" : "▸ 快捷调参";
        }

        private void AddTunableRow(DebugTunable tunable)
        {
            var row = new TunableRow { Descriptor = tunable };
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.flexDirection = FlexDirection.Row;
            line.style.marginBottom = 2f;

            Label name = AddLabel(line, ShortLabel(tunable.Label), NormalColor, HintFontSize, false);
            name.style.width = 96f;
            name.style.flexShrink = 0f;
            name.tooltip = tunable.Label;

            row.Value = AddLabel(line, "-", AccentColor, HintFontSize, false);
            row.Value.style.width = 52f;
            row.Value.style.flexShrink = 0f;

            if (tunable.Kind == DebugTunableKind.Bool)
            {
                Button toggle = CreateButton(() =>
                {
                    float next = tunable.Get() >= 0.5f ? 0f : 1f;
                    _onTunableSet?.Invoke(tunable, next);
                    SetTunableValue(tunable.Id, next);
                });
                toggle.text = "☐";
                toggle.style.width = 28f;
                toggle.style.height = 20f;
                toggle.style.flexShrink = 0f;
                line.Add(toggle);
                row.Toggle = toggle;
            }
            else
            {
                var track = new VisualElement { pickingMode = PickingMode.Position };
                track.style.flexGrow = 1f;
                track.style.height = 14f;
                track.style.marginLeft = 4f;
                track.style.marginRight = 4f;
                track.style.backgroundColor = new Color(0.1f, 0.13f, 0.16f, 1f);
                track.style.borderLeftWidth = 1f;
                track.style.borderRightWidth = 1f;
                track.style.borderTopWidth = 1f;
                track.style.borderBottomWidth = 1f;
                track.style.borderLeftColor = new Color(0.25f, 0.3f, 0.36f, 1f);
                track.style.borderRightColor = new Color(0.25f, 0.3f, 0.36f, 1f);
                track.style.borderTopColor = new Color(0.25f, 0.3f, 0.36f, 1f);
                track.style.borderBottomColor = new Color(0.25f, 0.3f, 0.36f, 1f);
                row.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
                row.Fill.style.position = Position.Absolute;
                row.Fill.style.left = 0f;
                row.Fill.style.top = 0f;
                row.Fill.style.bottom = 0f;
                row.Fill.style.width = Length.Percent(0f);
                row.Fill.style.backgroundColor = AccentColor;
                track.Add(row.Fill);
                track.RegisterCallback<PointerDownEvent>(evt =>
                {
                    row.Dragging = true;
                    track.CapturePointer(evt.pointerId);
                    ApplyTunablePointer(row, track, evt.position.x);
                    evt.StopPropagation();
                });
                track.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!row.Dragging) return;
                    ApplyTunablePointer(row, track, evt.position.x);
                    evt.StopPropagation();
                });
                track.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (!row.Dragging) return;
                    row.Dragging = false;
                    track.ReleasePointer(evt.pointerId);
                    ApplyTunablePointer(row, track, evt.position.x);
                    evt.StopPropagation();
                });
                line.Add(track);
            }

            Button reset = CreateButton(() => _onTunableReset?.Invoke(tunable));
            reset.text = "↺";
            reset.style.width = 24f;
            reset.style.height = 20f;
            reset.style.flexShrink = 0f;
            line.Add(reset);

            _tunablesBody.Add(line);
            _tunableRows[tunable.Id] = row;
            try { SetTunableValue(tunable.Id, tunable.Get()); }
            catch (Exception ex) { Log.Warning("调参项读取失败 " + tunable.Id + "：" + ex.Message, LogTag.UI); }
        }

        private void ApplyTunablePointer(TunableRow row, VisualElement track, float screenX)
        {
            float width = track.worldBound.width;
            if (width <= 0f) return;
            float pct = Mathf.Clamp01((screenX - track.worldBound.xMin) / width);
            float value = row.Descriptor.Min + pct * (row.Descriptor.Max - row.Descriptor.Min);
            _onTunableSet?.Invoke(row.Descriptor, value);
            SetTunableValue(row.Descriptor.Id, value);
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
        }

        private Label AddLabel(VisualElement parent, string text, Color color, int fontSize, bool bold)
        {
            var label = new Label { text = text, pickingMode = PickingMode.Ignore };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = fontSize;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (_font != null) label.style.unityFontDefinition = FontDefinition.FromSDFFont(_font);
            parent.Add(label);
            return label;
        }

        private void AddActionButton(VisualElement parent, string text, Action onClick, Color color)
        {
            Button button = CreateButton(onClick);
            button.text = text;
            button.style.flexGrow = 1f;
            button.style.marginRight = 4f;
            button.style.color = color;
            parent.Add(button);
        }

        private Button CreateButton(Action onClick)
        {
            var button = new Button(onClick) { pickingMode = PickingMode.Position };
            button.style.height = 24f;
            button.style.fontSize = FontSize;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.overflow = Overflow.Hidden;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.backgroundColor = new Color(0.12f, 0.15f, 0.19f, 0.95f);
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftColor = new Color(0.25f, 0.3f, 0.36f, 1f);
            button.style.borderRightColor = new Color(0.25f, 0.3f, 0.36f, 1f);
            button.style.borderTopColor = new Color(0.25f, 0.3f, 0.36f, 1f);
            button.style.borderBottomColor = new Color(0.25f, 0.3f, 0.36f, 1f);
            if (_font != null) button.style.unityFontDefinition = FontDefinition.FromSDFFont(_font);
            return button;
        }

        private TestModeOptions.Info FindInfo(TestModeOptions.Id id)
        {
            for (int i = 0; _options != null && i < _options.Count; i++)
                if (_options[i].Id == id) return _options[i];
            return default;
        }

        /// <summary>面板行用短名（去掉括号说明，完整说明留 tooltip）；不是第二份清单。</summary>
        private static string ShortLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            int cut = label.IndexOf('（');
            if (cut < 0) cut = label.IndexOf('(');
            return cut > 0 ? label.Substring(0, cut) : label;
        }
    }
}
#endif
