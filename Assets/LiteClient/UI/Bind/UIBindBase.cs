using System;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// UI 绑定基类：C# 界面逻辑的受控 API 承载件（设计方案 §4.6——不裸抛 GameObject）。
    /// **MonoBehaviour 形态**：页面组件挂 prefab 根，UIService 实例化 prefab 时经"自带 IUIFormLogic 组件"
    /// 解析缝取用（挂件即被驱动）。索引由 <see cref="BindIndexBuilder"/> 读 BindNode 标记构建。
    /// 七回调拆成 protected virtual（子类覆写业务，本类承接 IUIFormLogic 转发）；
    /// OnButton 返回注销委托（事件语义 §7.3）。
    /// </summary>
    public abstract class UIBindBase : MonoBehaviour, IUIFormLogic, ILocaleBound
    {
        private UIBindIndex _index = UIBindIndex.Empty;

        protected UIForm Form { get; private set; }

        void IUIFormLogic.OnInit(UIForm form, IUIData data)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            _index = BindIndexBuilder.Build(form.Root);
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
