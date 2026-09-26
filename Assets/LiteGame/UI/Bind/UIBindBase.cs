using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// UI 绑定基类（M4 §2.4）：C# 界面逻辑的受控 API 承载件（设计方案 §4.6——不裸抛 GameObject）。
    /// 双模式索引：A) OnInit 时经 BindIndexBuilder 读 BindNode 标记构建；B) Designer 生成类构造期
    /// RegisterControl 登记（LiteCodeGen 路径 B）。两模式自动选择——登记非空即走 B。
    /// 七回调拆成 protected virtual（子类覆写业务，本类承接 IUIFormLogic 转发）；
    /// OnButton 返回注销委托（事件语义 §7.3）。
    /// </summary>
    public abstract class UIBindBase : IUIFormLogic
    {
        private readonly Dictionary<string, Component> _designer = new Dictionary<string, Component>(16);
        private UIBindIndex _index = UIBindIndex.Empty;

        protected UIForm Form { get; private set; }

        /// <summary>路径 B：Designer 生成类构造期登记字段（LiteCodeGen 生成代码调用）。</summary>
        protected void RegisterControl(string name, Component component)
            => _designer[name] = component ?? throw new ArgumentNullException(nameof(component));

        void IUIFormLogic.OnInit(UIForm form, IUIData data)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            _index = _designer.Count > 0 ? new UIBindIndex(_designer) : BindIndexBuilder.Build(form.Root);
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
        protected void SetVisible(string name, bool visible) => _index.SetVisible(name, visible);
        protected void SetInteractable(string name, bool on) => _index.SetInteractable(name, on);
        protected void SetAnchoredPosition(string name, Vector2 pos) => _index.SetAnchoredPosition(name, pos);
    }
}
