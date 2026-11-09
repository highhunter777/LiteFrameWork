using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 名字 → 控件索引：受控 API 的查询底座。
    /// </summary>
    public sealed partial class UIBindIndex
    {
        public static readonly UIBindIndex Empty = new UIBindIndex(new Dictionary<string, Component>());

        private readonly Dictionary<string, Component> _controls;
        private readonly UIBindLocale _localeBinder;
        private readonly UIBindEvents _eventBinder;
        private readonly UIBindListSource _listBinder;
        private Action<string> _showToast;

        public UIBindIndex(Dictionary<string, Component> controls)
        {
            _controls = controls ?? throw new ArgumentNullException(nameof(controls));
            _localeBinder = new UIBindLocale();
            _eventBinder = new UIBindEvents(this);
            _listBinder = new UIBindListSource(this, _localeBinder);
            _localeBinder.Changed += OnLocaleChanged;
        }

        public partial T Get<T>(string name) where T : Component
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName");
            if (c is T typed) return typed;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 类型不符:期望 {typeof(T).Name} 实得 {c.GetType().Name}");
        }

        public partial bool Has(string name) => _controls.ContainsKey(name);

        public partial void BindButtonClick(string name, Action onClick)
        {
            var btn = Get<Button>(name);
            btn.onClick.RemoveAllListeners();
            UnityEngine.Events.UnityAction h = () => SafeCall.Invoke(onClick, $"OnButtonClick[{name}]");
            btn.onClick.AddListener(h);
        }

        public partial void UnbindButtonClick(string name)
        {
            if (TryGet<Button>(name, out var btn)) btn.onClick.RemoveAllListeners();
        }

        /// <summary>解绑全部按钮监听（界面 OnHide 时调用——池化复用跨环境的安全垫）。</summary>
        public void UnbindAll()
        {
            foreach (var c in _controls.Values)
                if (c is Button btn) btn.onClick.RemoveAllListeners();

            // 本地化绑定与订阅一并解除——池化复用不得留旧订阅（否则旧页继续被语言事件刷新）。
            _localeBinder.Clear();
            _localeBinder.Detach();

            // 输入控件订阅与列表条目一并解除（同上纪律；页面 OnShow 会重推数据与控件值）。
            _eventBinder.ClearAll();
            _listBinder.Clear();
        }

        /// <summary>命令式写文本。
        /// 目标节点挂 <see cref="LTextLabel"/> 时经打字机（Reveal——叙事文本的表现行为，§9；
        /// 关闭/语言变化取消旧任务由组件承担）；否则直接写全量。</summary>
        public partial void SetText(string name, string value)
        {
            if (TryGet<TMPro.TMP_Text>(name, out var tmp)) { RevealOrSetText(tmp.gameObject, value); return; }
            if (TryGet<UnityEngine.UI.Text>(name, out var legacy)) { RevealOrSetText(legacy.gameObject, value); return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Text/TMP_Text 组件");
        }

        /// <summary>命令式路径的打字机路由（节点挂 LTextLabel 时走打字机，否则直写）。</summary>
        internal void RevealOrSetText(GameObject owner, string value)
        {
            var label = owner.GetComponent<LTextLabel>();
            if (label != null) { label.Reveal(value); return; }
            if (owner.GetComponent<TMPro.TMP_Text>() != null) owner.GetComponent<TMPro.TMP_Text>().text = value;
            else owner.GetComponent<UnityEngine.UI.Text>().text = value;
        }

        // ---- LText：按 key 设文本 + 语言变更自动刷新（《UI框架总设计》§9）----。

        /// <summary>
        /// 绑定语言服务（装配点注入）。切换语言时经 <see cref="UIBindLocale.Changed"/> 刷新全部已绑定项。
        /// 重复注入会先退订旧的——避免同实例多份订阅导致重复刷新。
        /// </summary>
        public void BindLocale(ILocalizationService localization) => _localeBinder.Bind(localization);

        /// <summary>语言变更：按已登记 key 重写文本（不触碰未绑定 key 的控件）；列表行文案走整表重绑
        /// （行是复用条目——不做逐行订阅，滚动路径零残留）。</summary>
        private void OnLocaleChanged()
        {
            _localeBinder.RewriteAll(SetText);
            _listBinder.RebindVisible();
        }

        /// <summary>
        /// 按 key 设文本。**未注入语言服务时抛**——静默显示原始 key 会让"忘了注入"
        /// 变成"线上全是 key"的隐蔽故障。
        /// </summary>
        public partial void SetTextKey(string name, string key, params object[] args)
        {
            _localeBinder.RequireBound(name, "SetTextKey");
            SetText(name, _localeBinder.Set(name, key, args));
        }

        /// <summary>按 key 设文本（带复数；§9"英文 one/other 显式选取"）。</summary>
        public partial void SetTextKeyPlural(string name, string key, long count, params object[] args)
        {
            _localeBinder.RequireBound(name, "SetTextKeyPlural");
            SetText(name, _localeBinder.SetPlural(name, key, count, args));
        }

        /// <summary>解除单个控件的本地化绑定（不随语言刷新）。</summary>
        public partial bool UnbindTextKey(string name) => _localeBinder.Unbind(name);

        public partial void SetVisible(string name, bool visible)
        {
            Get<Component>(name).gameObject.SetActive(visible);
        }

        /// <summary>可交互开关：`Selectable` 优先，回退 `UIWidget.Interactable`，再回退 <see cref="IUIInteractable"/>
        /// ——`StateButton` 等自持交互语义的控件不是 Selectable；控件库封装件（下拉/滑条/开关/输入框）
        /// 把 inner Selectable 的可交互性透传出来（页面不必知道封装形态）。</summary>
        public partial void SetInteractable(string name, bool on)
        {
            if (TryGet<Selectable>(name, out var sel)) { sel.interactable = on; return; }
            if (TryGet<UIWidget>(name, out var widget)) { widget.Interactable = on; return; }
            if (_controls.TryGetValue(name, out var control) && control is IUIInteractable custom) { custom.Interactable = on; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Selectable / UIWidget / IUIInteractable 组件（无法设置可交互）");
        }

        // ---- 受控 API 扩展（《UI逻辑API参考》G3/G7/G10）----。

        /// <summary>进度条：归一化值（0~1）。G7。</summary>
        public partial void SetProgress(string name, float value01)
        {
            Get<ProgressBar>(name).Set(value01);
        }

        /// <summary>进度条：当前/上限。G7。</summary>
        public partial void SetProgress(string name, float current, float max)
        {
            Get<ProgressBar>(name).Set(current, max);
        }

        /// <summary>血条（前条瞬时 + 后条延迟滑落）。G7。</summary>
        public partial void SetHp(string name, float current, float max)
        {
            Get<HpBar>(name).Set(current, max);
        }

        /// <summary>倒计时启动。G10。</summary>
        public partial void StartCountdown(string name, float seconds)
        {
            Get<Countdown>(name).StartCountdown(seconds);
        }

        /// <summary>倒计时停止（不触发 OnDone）。G10。</summary>
        public partial void StopCountdown(string name)
        {
            Get<Countdown>(name).Stop();
        }

        internal void BindToast(Action<string> showToast)
            => _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));

        /// <summary>轻提示经所属页面转发到反馈服务；缺少装配即显性失败。</summary>
        public partial void ShowToast(string text)
            => (_showToast ?? throw new InvalidOperationException("页面未绑定 Toast 反馈入口"))(text);

        /// <summary>气泡（挂点旁短提示）。G3。</summary>
        public partial void ShowBubble(string name, string text, float duration)
        {
            Get<UIBubble>(name).Show(text, duration);
        }

        /// <summary>飘字（位置取控件自身 anchoredPosition——界面侧把飘字锚点摆好即可）。G3。</summary>
        public partial void ShowFlyText(string name, string text)
        {
            var pool = Get<FlyTextPool>(name);
            var rt = pool.transform as RectTransform;
            pool.Show(text, rt != null ? rt.anchoredPosition : Vector2.zero);
        }

        public partial void SetAnchoredPosition(string name, Vector2 pos)
        {
            ResolveRect(name).anchoredPosition = pos;      // 走 ResolveRect：BindNode 不产 RectTransform，Get<RectTransform> 必抛
        }

        // ---- 输入控件族（G15～G17，《UI逻辑API参考》§3.4）---- 语义（§5"输入控件"）：**程序 Set 不触发用户变化回调**（Set*WithoutNotify）； 变化通知只在**用户操作**时回调（回调带值）；重复登记替换旧回调。

        /// <summary>滑块设值（0~1，程序 Set——不触发变化回调）。G15。</summary>
        public partial void SetSlider(string name, float value01)
        {
            Get<Slider>(name).SetValueWithoutNotify(Mathf.Clamp01(value01));
        }

        /// <summary>滑块当前值（0~1）。G15。</summary>
        public partial float GetSlider(string name) => Get<Slider>(name).value;

        /// <summary>滑块变化通知（用户拖动；回调带值）。重复登记替换；<c>null</c> = 仅退订。G15。</summary>
        public partial void OnSliderChanged(string name, Action<float> handler) => _eventBinder.OnSliderChanged(name, handler);

        /// <summary>开关设值（程序 Set——不触发变化回调）。G16。</summary>
        public partial void SetToggle(string name, bool on)
        {
            Get<Toggle>(name).SetIsOnWithoutNotify(on);
        }

        /// <summary>开关当前值。G16。</summary>
        public partial bool GetToggle(string name) => Get<Toggle>(name).isOn;

        /// <summary>开关变化通知（用户点击；回调带值）。重复登记替换；<c>null</c> = 仅退订。G16。</summary>
        public partial void OnToggleChanged(string name, Action<bool> handler) => _eventBinder.OnToggleChanged(name, handler);

        /// <summary>下拉件解析：**控件库 `UIDropdownWrap`（TMP 封装）优先**，兼容 UGUI `Dropdown`
        /// （既有页面/用例）——两者都在时以包装件为准（它是项目侧的控件面）。</summary>
        internal Component ResolveDropdown(string name)
        {
            var control = Get<Component>(name);
            if (control is UIDropdownWrap || control is Dropdown) return control;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 不是下拉件:期望 {nameof(UIDropdownWrap)}/{nameof(Dropdown)} 实得 {control.GetType().Name}");
        }

        /// <summary>下拉选项（文本由页面给**已本地化**文本——语言切换时页面重设；**值保持**）。G17。</summary>
        public partial void SetDropdownOptions(string name, IReadOnlyList<string> options)
        {
            var control = ResolveDropdown(name);
            if (control is UIDropdownWrap wrap)
            {
                wrap.SetOptions(options ?? System.Array.Empty<string>(), wrap.Selected);   // 值保持（SetOptions 内部钳位）
                return;
            }

            var dropdown = (Dropdown)control;
            var list = new List<Dropdown.OptionData>(options != null ? options.Count : 0);
            if (options != null)
                for (int i = 0; i < options.Count; i++) list.Add(new Dropdown.OptionData(options[i] ?? string.Empty));
            dropdown.options = list;
            dropdown.RefreshShownValue();                   // 选项变更后刷标题（越界值由 UGUI 自行收敛）
        }

        /// <summary>下拉设值（索引，程序 Set——不触发变化回调）。G17。</summary>
        public partial void SetDropdownValue(string name, int index)
        {
            var control = ResolveDropdown(name);
            if (control is UIDropdownWrap wrap) { wrap.SetValue(index); return; }
            ((Dropdown)control).SetValueWithoutNotify(index);
        }

        /// <summary>下拉当前值（索引）。G17。</summary>
        public partial int GetDropdownValue(string name)
        {
            var control = ResolveDropdown(name);
            return control is UIDropdownWrap wrap ? wrap.Selected : ((Dropdown)control).value;
        }

        /// <summary>下拉变化通知（用户选择；回调带索引）。重复登记替换；<c>null</c> = 仅退订。G17。</summary>
        public partial void OnDropdownChanged(string name, Action<int> handler) => _eventBinder.OnDropdownChanged(name, handler);

        // ---- 列表（G6，《UI逻辑API参考》§3.6"SetList"）----。

        /// <summary>
        /// 装配列表数据源（**整表替换语义**：更新/缩容同一入口——换数据再调一次即可，内核按新 Count 重算窗口）。
        /// </summary>
        public partial void SetList(string name, IUIListRows rows) => _listBinder.Set(name, rows);

        /// <summary>
        /// 行点击通知：回调带 (key, 按钮节点名)——**条目根按钮 = 空串**，条目内其余按钮各回自己的节点名。
        /// 可在 SetList 前后调用（后登记 = 对已实体化条目补挂）。G6。
        /// </summary>
        public partial void OnListClick(string name, Action<string, string> handler) => _listBinder.OnClick(name, handler);

        internal bool TryGetList<T>(string name, out T list) where T : Component
        {
            list = null;
            if (!_controls.TryGetValue(name, out var c) || c == null) return false;
            list = c.GetComponent<T>() ?? c.GetComponentInChildren<T>(true);
            return list != null;
        }

        // ---- 动效口（《动效设计方案》附 A.3）---- 纪律：动效只写表现（透明度/位置），不携带任何判定；原语层的 SetUpdate(true)/SetLink(KillOnDisable) 已写死在 UIFx 里（UIClock 轨 + 界面隐藏即杀）。

        /// <summary>脉冲：透明度快速呼吸两次（图标/红点提醒）。返回 true = tween 已创建。G20。
        /// 缺省参单源＝<see cref="UIFxDefaults"/>（与受控 API 同表）。</summary>
        public partial bool Pulse(string name, float strength, float duration)
        {
            return UIFx.Pulse(ResolveGraphic(name), strength, duration) != null;
        }

        /// <summary>闪烁：一次性高亮回落。G20。</summary>
        public partial bool Flash(string name, float duration)
        {
            return UIFx.Flash(ResolveGraphic(name), duration) != null;
        }

        /// <summary>位移入场：从 offset 相对位滑回原位。G20。</summary>
        public partial bool Slide(string name, Vector2 offset, float duration)
        {
            return UIFx.Slide(ResolveRect(name), offset, duration) != null;
        }

        /// <summary>
        /// 动效目标解析（**不走 <see cref="Get{T}"/>**）：索引里存的是 BindNode 自动检测到的组件。
        /// </summary>
        public Graphic ResolveGraphic(string name)
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName");
            if (c is Graphic g) return g;
            if (c is Button btn && btn.targetGraphic != null) return btn.targetGraphic;
            var found = c.GetComponent<Graphic>() ?? c.GetComponentInChildren<Graphic>(true);
            if (found != null) return found;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 无 Graphic 组件（实得 {c.GetType().Name}）——动效目标必须是 Graphic");
        }

        /// <summary>
        /// RectTransform 解析：UI 层级下任何组件的 transform 都是 RectTransform—— **不要用 Get&lt;RectTransform&gt。
        /// </summary>
        public RectTransform ResolveRect(string name)
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName");
            if (c.transform is RectTransform rt) return rt;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 的 transform 不是 RectTransform（实得 {c.transform.GetType().Name}）");
        }

        internal bool TryGet<T>(string name, out T control) where T : Component
        {
            Component c = _controls.TryGetValue(name, out Component stored) ? stored : null;
            control = c as T;
            if (control == null && c != null) control = c.GetComponent<T>();
            return control != null;
        }
    }
}
