using System;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 绑定基类：C# 界面逻辑的受控 API 承载件（设计方案 §4.6——不裸抛 GameObject）。
    /// **MonoBehaviour 形态**：页面组件挂 prefab 根，UIService 实例化 prefab 时经"自带 IUIFormLogic 组件"
    /// 解析缝取用（挂件即被驱动）。索引由 <see cref="BindIndexBuilder"/> 读 BindNode 标记构建。
    /// 七回调拆成 protected virtual（子类覆写业务，本类承接 IUIFormLogic 转发）；
    /// OnButton 返回注销委托（事件语义 §7.3）。
    /// </summary>
    public abstract partial class UIBindBase : MonoBehaviour, IUIFormLogic, ILocaleBound
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

        // 受控 API（GetControl/HasControl/OnButton/SetText/SetTextKey*/SetVisible/SetInteractable/SetAnchoredPosition）
        // 由 UiApiSpec 生成到 UIBindBase.Api.g.cs；本文件只留生命周期与索引装配。

        /// <summary>语言服务注入（<see cref="ILocaleBound"/> 可选能力——UIForm 在 OnInit 后调用）。
        /// OnInit 前索引为全局 Empty = no-op（防 Empty 被串订阅）；幂等语义同 UIBindIndex.BindLocale。</summary>
        public void BindLocale(ILocalizationService localization)
        {
            if (ReferenceEquals(_index, UIBindIndex.Empty)) return;
            _index.BindLocale(localization);
        }
    }
}
