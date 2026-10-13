using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// UI 绑定基类：C# 界面逻辑的受控 API 承载件（设计方案 §4.6——不裸抛 GameObject）。
    /// **MonoBehaviour 形态**：页面组件挂 prefab 根（Designer 拖引用或 BindNode 标记），
    /// UIService 实例化 prefab 时经"自带 IUIFormLogic 组件"解析缝取用（挂件即被驱动——路径 B 接线口）。
    /// 双模式索引：A) 经 BindIndexBuilder 读 BindNode 标记构建；B) 生成类覆写 <see cref="CollectControls"/>
    /// 登记字段（LiteCodeGen EmitRegisterControl 产物调 RegisterControl——OnInit 内先收集再选型，
    /// 与 Unity Awake 派发解耦）。两模式自动选择——登记非空即走 B。
    /// 七回调拆成 protected virtual（子类覆写业务，本类承接 IUIFormLogic 转发）；
    /// OnButton 返回注销委托（事件语义 §7.3）。
    /// </summary>
    public abstract class UIBindBase : MonoBehaviour, IUIFormLogic, ILocaleBound
    {
        // **惰性初始化**（非 readonly）：Unity Instantiate 不跑字段初始化器/构造器——
        // prefab 挂件页被克隆后本字典为 null，登记口与选型口都必须容 null（首个登记即建）。
        private Dictionary<string, Component> _designer;
        private UIBindIndex _index = UIBindIndex.Empty;

        /// <summary>登记字典（惰性——首个 RegisterControl 即建）。</summary>
        private Dictionary<string, Component> Designer => _designer ??= new Dictionary<string, Component>(16);

        protected UIForm Form { get; private set; }

        /// <summary>路径 B：逐控件登记字段（生成类覆写 <see cref="CollectControls"/> 时调用；手写页同形）。</summary>
        protected void RegisterControl(string name, Component component)
            => Designer[name] = component ?? throw new ArgumentNullException(nameof(component));

        /// <summary>路径 B 收集钩子：生成类覆写并逐字段 <see cref="RegisterControl"/>（基类在索引选型之前调用）。
        /// 默认空 = 走路径 A（BindNode 标记索引）。</summary>
        protected virtual void CollectControls() { }

        void IUIFormLogic.OnInit(UIForm form, IUIData data)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            CollectControls();
            _index = _designer != null && _designer.Count > 0
                ? new UIBindIndex(_designer)
                : BindIndexBuilder.Build(form.Root);
            OnInit(data);
        }

        void IUIFormLogic.OnShow(IUIData data) => OnShow(data);
        void IUIFormLogic.OnUpdate(float deltaTime) => OnUpdate(deltaTime);
        void IUIFormLogic.OnPause() => OnPause();
        void IUIFormLogic.OnCover() => OnCover();
        void IUIFormLogic.OnReveal() => OnReveal();
        void IUIFormLogic.OnHide() => OnHide();

        protected virtual void OnInit(IUIData data) { }
        protected virtual void OnShow(IUIData data) { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnPause() { }
        protected virtual void OnCover() { }
        protected virtual void OnReveal() { }
        protected virtual void OnHide() { }

        // ---- 受控 API（转发索引；强类型面，子类专用）----

        protected T GetControl<T>(string name) where T : Component => _index.Get<T>(name);

        protected bool HasControl(string name) => _index.Has(name);

        /// <summary>替换式绑按钮；返回注销委托（事件语义 §7.3）。onClick 抛经 SafeCall 隔离。</summary>
        protected Action OnButton(string name, Action onClick)
        {
            _index.BindButton(name, onClick);
            return () => _index.UnbindButton(name);
        }

        protected void SetText(string name, string value) => _index.SetText(name, value);

        // ---- LText 面（与 Lua shim 同面：《UI框架总设计》§9——文案经 key 写入，语言切换自动刷新）----

        /// <summary>按 key 设文本（带参复数外的常规形态；<see cref="UIBindIndex.SetTextKey"/> 同语义：
        /// 未注入语言服务时抛——静默显示 key 是隐蔽故障）。</summary>
        protected void SetTextKey(string name, string key, params object[] args)
            => _index.SetTextKey(name, key, args);

        /// <summary>按 key 设文本（英文 one/other 显式复数选取）。</summary>
        protected void SetTextKeyPlural(string name, string key, long count, params object[] args)
            => _index.SetTextKeyPlural(name, key, count, args);

        /// <summary>解除单控件本地化绑定（不再随语言刷新）。</summary>
        protected bool UnbindTextKey(string name) => _index.UnbindTextKey(name);

        protected void SetVisible(string name, bool visible) => _index.SetVisible(name, visible);
        protected void SetInteractable(string name, bool on) => _index.SetInteractable(name, on);
        protected void SetAnchoredPosition(string name, Vector2 pos) => _index.SetAnchoredPosition(name, pos);

        /// <summary>语言服务注入（<see cref="ILocaleBound"/> 可选能力——UIForm 在 OnInit 后调用）。
        /// OnInit 前索引为全局 Empty = no-op（防 Empty 被串订阅）；幂等语义同 UIBindIndex.BindLocale。</summary>
        public void BindLocale(ILocalizationService localization)
        {
            if (ReferenceEquals(_index, UIBindIndex.Empty)) return;
            _index.BindLocale(localization);
        }
    }
}
