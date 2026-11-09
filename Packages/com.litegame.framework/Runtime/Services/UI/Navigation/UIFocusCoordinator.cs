using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 焦点协调者（《UI框架总设计》§6.2"焦点保存/恢复、键盘/手柄 Submit/Cancel 和平台返回统一处理"； U2-⑦）。
    /// </summary>
    public sealed class UIFocusCoordinator
    {
        /// <summary>被遮/暂停页保存的焦点（formId → 焦点控件；控件随页销毁即失效——Unity null 判定）。</summary>
        private readonly Dictionary<int, GameObject> _saved = new Dictionary<int, GameObject>(4);

        /// <summary>EventSystem 解析缝（默认 <c>EventSystem.current</c>——Play 模式由主循环置位；
        /// EditMode 用例直注实例——<c>current</c> 在编辑器不更新（实测），实例方法可用）。</summary>
        private readonly Func<EventSystem> _resolve;

        /// <summary>平台返回入口（宿主装配点接线——UI 段 <c>UISegment</c> 挂 <c>UINavigationController.BackAsync</c>）。
        /// 未接线时 RequestBack 返回 false（框架先行形态：无导航者即无返回）。</summary>
        public Func<UniTask<bool>> PlatformBack { get; set; }

        public UIFocusCoordinator(Func<EventSystem> resolveEventSystem = null)
        {
            _resolve = resolveEventSystem ?? (() => EventSystem.current);
        }

        private EventSystem Es => _resolve();

        /// <summary>打开接管（打开/复用管线尾段调用）：聚焦首个可交互 Selectable（无则不动）。</summary>
        public void OnFormActivated(UIForm form)
        {
            var es = Es;
            if (es == null) return;                        // 无底座（EditMode/未保障）——簿记跳过不抛

            Selectable first = null;
            foreach (var sel in form.Root.GetComponentsInChildren<Selectable>(false))
            {
                if (sel == null || !sel.interactable) continue;
                first = sel;
                break;                                    // 深度优先首个可交互件——层级声明序即焦点序
            }
            if (first != null) es.SetSelectedGameObject(first.gameObject);
        }

        /// <summary>遮盖/暂停：焦点在本人页内 → 保存并清选择（键盘不驱动被遮/暂停页）。</summary>
        public void OnFormSuspended(UIForm form)
        {
            var es = Es;
            var selected = es == null ? null : es.currentSelectedGameObject;
            if (es == null || selected == null || !IsWithin(selected, form.Root)) return;

            _saved[form.Id] = selected;
            es.SetSelectedGameObject(null);
        }

        /// <summary>揭示/续跑：不抢占恢复（当前选择为空或在本人页内才还原保存的焦点）。</summary>
        public void OnFormRevealed(UIForm form)
        {
            var es = Es;
            if (es == null) return;
            if (!_saved.TryGetValue(form.Id, out var saved) || saved == null) return;

            var selected = es.currentSelectedGameObject;
            if (selected == null || IsWithin(selected, form.Root))
                es.SetSelectedGameObject(saved);
        }

        /// <summary>关闭：摘保存；若当前选择在已关页内 → 清选择（底层页随后的揭示恢复可接管）。</summary>
        public void OnFormClosed(UIForm form)
        {
            _saved.Remove(form.Id);

            var es = Es;
            var selected = es == null ? null : es.currentSelectedGameObject;
            if (es != null && selected != null && IsWithin(selected, form.Root))
                es.SetSelectedGameObject(null);
        }

        /// <summary>平台返回：转发宿主接线（Back 目标推导/拦截链归导航器——不重复实现）。
        /// 异步回执由导航器队列自理（单写者串行）；未接线 = false。</summary>
        public bool RequestBack()
        {
            var handler = PlatformBack;
            if (handler == null) return false;
            handler().ForgetLogged("UIFocusCoordinator");
            return true;
        }

        /// <summary>输入侧轮询（UIService.Tick 驱动）：平台返回键（设备直读经 <see cref="UIInput"/>
        /// 隔离层——桌面先行口径 = ESC；Android 返回键随平台适配批接 Action）。</summary>
        public void Tick()
        {
            if (UIInput.BackPressedThisFrame())
                RequestBack();
        }

        private static bool IsWithin(GameObject selected, GameObject root)
            => selected.transform.IsChildOf(root.transform);
    }
}
