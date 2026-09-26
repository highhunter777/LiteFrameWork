using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>控件基类（M4c）：可交互性 + 按压微动效 hook（走 UiFx 原语——全项目唯一 tween 出口）。
    /// 派生类覆写 Awake 时必须调 base.Awake()；OnClick/OnInteractableChanged 为多态挂点。
    /// 2026-09-14 重建：拆文件时同名覆盖事故后按旧程序集反射签名还原（成员与序列化字段名逐一对齐）。</summary>
    public class UIWidget : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private bool interactable = true;
        [SerializeField] private bool pressFx = true;

        /// <summary>可交互（false = 视觉走禁用态且吞点击；具体视觉由派生类实现 OnInteractableChanged）。</summary>
        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value) return;
                interactable = value;
                OnInteractableChanged(value);
            }
        }

        protected virtual void Awake() { }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (!interactable) return;
            PlayPressFx();
            OnClick();
        }

        /// <summary>按压微动效（默认 UiFx.Pulse；子类可覆写，禁引第二套 tween 路径）。</summary>
        public virtual void PlayPressFx()
        {
            if (!pressFx) return;
            var graphic = GetComponent<Graphic>();
            if (graphic != null) UiFx.Pulse(graphic, 1.2f, 0.08f);
        }

        /// <summary>点击语义（派生类覆写；如 StateButton 覆写为空——按压动效已并入其连点保护路径）。</summary>
        protected virtual void OnClick() { }

        /// <summary>可交互性变更挂点（派生类刷新视觉）。</summary>
        protected virtual void OnInteractableChanged(bool value) { }
    }

    /// <summary>控件数据绑定承载件（设计方案 §4.7 绑定模式）：值 → 控件单向刷。
    /// 内置去重（值未变不刷——结构上防"每帧全量刷 UI"）；同一数据可建多个 binder（金币文本/图标各自刷）。</summary>
    public sealed class UIDataBinder<T> : IDisposable
    {
        private Action<T> _apply;
        private T _last;
        private bool _has;

        public UIDataBinder(Action<T> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /// <summary>写值：与上次相同则跳过（离散值映射语义，§4.7）。</summary>
        public void Set(T value)
        {
            if (_apply == null) throw new ObjectDisposedException(nameof(UIDataBinder<T>));
            if (_has && EqualityComparer<T>.Default.Equals(_last, value)) return;
            _last = value;
            _has = true;
            _apply(value);
        }

        public void Dispose() => _apply = null;
    }
}
