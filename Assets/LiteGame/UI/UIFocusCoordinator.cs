using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 焦点协调者（《UI框架总设计》§6.2"焦点保存/恢复、键盘/手柄 Submit/Cancel 和平台返回统一处理"；
    /// U2-⑦）。由 <see cref="UIService"/> 持有并在生命周期迁移处驱动；纯簿记类（无 MonoBehaviour），
    /// EditMode 可直测。
    ///
    /// - **焦点事实单源** = EventSystem 当前选择（<c>currentSelectedGameObject</c>）；
    ///   焦点可见 = Selectable 的 Selected 过渡态（引擎原生渲染，不建第二套焦点视觉）；
    /// - **保存/恢复**：遮盖/暂停时若焦点在被遮页内 → 保存并清选择（键盘不再驱动不可交互页）；
    ///   恢复（揭示/续跑）时**不抢占**——当前选择为空或在本人页内才还原（更高层模态持有焦点时不动）；
    /// - **打开接管**：新顶页无条件聚焦首个可交互 Selectable（层级骨架深度优先序——确定性）；
    /// - 平台返回输入侧经 <see cref="UiInput"/> 隔离层（R12 边界——设备直读收敛在
    ///   <c>Assets/LiteGame/UI/Input/</c>），语义侧（Back 目标推导/拦截链）归
    ///   <see cref="UINavigationController"/>——协调者经 <see cref="PlatformBack"/> 转发，不重复实现 Back 判据；
    /// - 键盘/手柄导航与 Submit/Cancel 由 <c>InputSystemUIInputModule</c> 默认动作集驱动
    ///   （引擎半部，见 <see cref="UiInput"/>）；输入锁（转场/模态/暂停）语义由壳层既有
    ///   协调链承担——协调者只在生命周期迁移点写选择。
    /// </summary>
    public sealed class UIFocusCoordinator
    {
        /// <summary>被遮/暂停页保存的焦点（formId → 焦点控件；控件随页销毁即失效——Unity null 判定）。</summary>
        private readonly Dictionary<int, GameObject> _saved = new Dictionary<int, GameObject>(4);

        /// <summary>EventSystem 解析缝（默认 <c>EventSystem.current</c>——Play 模式由主循环置位；
        /// EditMode 用例直注实例——<c>current</c> 在编辑器不更新（实测），实例方法可用）。</summary>
        private readonly Func<EventSystem> _resolve;

        /// <summary>平台返回入口（宿主装配点接线——UiShellModule 挂 <c>UINavigationController.BackAsync</c>）。
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
            handler().Forget();
            return true;
        }

        /// <summary>输入侧轮询（UIService.Tick 驱动）：平台返回键（设备直读经 <see cref="UiInput"/>
        /// 隔离层——桌面先行口径 = ESC；Android 返回键随平台适配批接 Action）。</summary>
        public void Tick()
        {
            if (UiInput.BackPressedThisFrame())
                RequestBack();
        }

        private static bool IsWithin(GameObject selected, GameObject root)
            => selected.transform.IsChildOf(root.transform);
    }
}
