using System;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 绑定基类：C# 界面逻辑的受控 API 承载件（设计方案 §4.6——不裸抛 GameObject）。
    /// </summary>
    public abstract partial class UIBindBase : MonoBehaviour, IUIFormLogic, ILocaleBound
    {
        private UIBindIndex _index = UIBindIndex.Empty;

        protected UIForm Form { get; private set; }

        /// <summary>绑定索引（列表/下拉/页签/动效等受控 API；生命周期回调里使用）。</summary>
        protected UIBindIndex Index => _index;

        void IUIFormLogic.OnInit(UIForm form, IUIData data)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            _index = BindIndexBuilder.Build(form.Root);
            _index.BindToast(form.ShowToast);
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

        // 受控 API（GetControl/HasControl/OnButtonClick/SetText/SetTextKey*/SetVisible/SetInteractable/SetAnchoredPosition）
        // 由 UIApiSpec 生成到 UIBindBase.Api.g.cs；本文件只留生命周期与索引装配。

        /// <summary>语言服务注入（<see cref="ILocaleBound"/> 可选能力——UIForm 在 OnInit 后调用）。
        /// OnInit 前索引为全局 Empty = no-op（防 Empty 被串订阅）；幂等语义同 UIBindIndex.BindLocale。</summary>
        public void BindLocale(ILocalizationService localization)
        {
            if (ReferenceEquals(_index, UIBindIndex.Empty)) return;
            _index.BindLocale(localization);
            _locale = localization;
        }

        private ILocalizationService _locale;

        /// <summary>当前语言服务（未注入 = null；页面取 Toast/动态文案用）。</summary>
        protected ILocalizationService Locale => _locale;
    }
}
