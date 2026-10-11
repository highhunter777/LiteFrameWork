using System;

namespace LiteGame
{
    /// <summary>
    /// 遮盖推导（从 <see cref="UIService"/> 外提的纯推演）：以"**仍逻辑打开**的最高全屏界面"所在组为界
    /// （含 Covered/Paused——§6.2：遮盖从仍打开且有遮盖能力的全部页面推导，不能只看 Active），
    /// **严格更低**的组全遮、其余照常；逐界面按目标态做最小迁移（Active↔Covered 配对回调不重复触发、
    /// Paused 不被遮盖改写），并通知焦点协调者（被遮页让出焦点、揭示页不抢占地恢复）。
    ///
    /// 调用时机仍在 <see cref="UIService"/> 的开/关事务序内（本类不决定何时重算，只做推演与迁移）。
    /// </summary>
    internal sealed class UICoverageResolver
    {
        private readonly UILayerGroup[] _groups;
        private readonly UIFocusCoordinator _focus;

        public UICoverageResolver(UILayerGroup[] groups, UIFocusCoordinator focus)
        {
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        }

        /// <summary>遮盖重算（幂等）。</summary>
        public void Recompute()
        {
            int coverThreshold = -1;                    // 严格低于该深度的组全遮
            foreach (var g in _groups)
                foreach (var f in g.Stack.Open)
                    if (f.IsOpen && f.Info.FullScreen)
                        coverThreshold = Math.Max(coverThreshold, g.BaseDepth);

            for (int i = _groups.Length - 1; i >= 0; i--)
            {
                var g = _groups[i];
                bool shouldCover = g.BaseDepth < coverThreshold;
                foreach (var f in g.Stack.Open)
                {
                    if (shouldCover && f.State == UIFormState.Active)
                    {
                        f.EnterCovered();
                        _focus.OnFormSuspended(f);            // 被遮页让出焦点（键盘不驱动不可交互页）
                    }
                    else if (!shouldCover && f.State == UIFormState.Covered)
                    {
                        f.EnterActiveFromCovered();
                        _focus.OnFormRevealed(f);             // 揭示页不抢占地恢复焦点
                    }
                }
            }
        }
    }
}
