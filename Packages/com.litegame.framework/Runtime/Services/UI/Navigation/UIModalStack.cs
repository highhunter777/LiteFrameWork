using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// 模态栈（《UI框架总设计》§6.2 视觉/输入单一来源）：**从仍打开的全部页面推导**，不独立记账。
    /// </summary>
    internal sealed class UIModalStack
    {

        private readonly IUIFormQuery _query;
        private readonly HashSet<int> _registered = new HashSet<int>(4);   // 登记为模态的 formId（幂等）
        private readonly List<UIForm> _scratch = new List<UIForm>(8);      // 复用缓冲（推导零分配）

        internal UIModalStack(IUIFormQuery query)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
        }

        /// <summary>取一份"仍打开"快照到复用缓冲。<b>调用方不得跨调用持有该 List</b>——
        /// 缓冲是本类私有的，一次推导内多次取集会互相覆盖。缓冲每次<b>先清空</b>（防跨次累积）。</summary>
        private List<UIForm> OpenSnapshot()
        {
            _scratch.Clear();
            _query.CollectOpen(_scratch);
            return _scratch;
        }

        /// <summary>登记模态（幂等；同 formId 重复登记无副作用）。</summary>
        public void Register(int formId) => _registered.Add(formId);

        /// <summary>取消模态登记（幂等；已打开页面不受影响——只影响后续 Back/遮蔽推导）。</summary>
        public void Unregister(int formId) => _registered.Remove(formId);

        /// <summary>当前是否有打开着的模态（输入协调者的组成输入，§6.2"输入由单一协调者综合模态栈…"）。</summary>
        public bool IsModalOpen => TopModal() != null;

        /// <summary>当前最顶模态（无模态返回 0）——按画布序取最上，**不取登记序**。</summary>
        public int TopModalId => TopModal()?.Id ?? 0;

        /// <summary>
        /// 顶模态实例：从<em>仍打开且已登记</em>的界面按画布序（<c>sortingOrder</c>）取最大者。
        /// 位在前后状态之间无意义——取离散事实，不插值（与 SimView.IsAiming 同口径）。
        /// </summary>
        public UIForm TopModal()
        {
            var open = OpenSnapshot();
            UIForm top = null;
            for (int i = 0; i < open.Count; i++)
            {
                var f = open[i];
                if (f == null || !_registered.Contains(f.Id)) continue;
                if (top == null || f.Canvas.sortingOrder > top.Canvas.sortingOrder) top = f;
            }
            return top;
        }

        /// <summary>
        /// 平台返回目标（§6.2）：**最顶模态优先**，无模态时取最高非空层级组的栈顶。
        /// 被出栈拦截的关闭是合法确定结果——这里只给目标，拦截由关闭方走 Back 语义。无目标返回 false。
        /// </summary>
        public bool TryGetBackTarget(out int formId)
        {
            var modal = TopModal();
            if (modal != null) { formId = modal.Id; return true; }

            // 无模态：从最高层级组往下扫，取首个非空组的**栈顶**（组内最上）。
            // 口径与组栈一致——不看实例字典，避免已落池（Recycled）但仍在字典里的实例被选中。
            for (int layer = _query.HighestLayer; layer >= 0; layer--)
            {
                var open = _query.StackOf(layer);
                if (open == null || open.Count == 0) continue;
                for (int i = open.Count - 1; i >= 0; i--)
                {
                    var f = open[i];
                    if (f != null && f.IsOpen) { formId = f.Id; return true; }
                }
            }
            formId = 0;
            return false;
        }

        /// <summary>
        /// 模态射线遮蔽（§6.2"模态有真实射线遮罩；禁用页面交互不等于停止阻挡下层射线"）： 顶层模态打开期间。
        /// </summary>
        public void RecomputeBlocking()
        {
            // 一次推导只取一次快照：顶模态与逐页面判定必须基于**同一份**打开集合
            // （两次取集会互相覆盖缓冲，导致下方页面判定用错集合）。
            var open = OpenSnapshot();
            UIForm top = null;
            for (int i = 0; i < open.Count; i++)
            {
                var f = open[i];
                if (f == null || !_registered.Contains(f.Id)) continue;
                if (top == null || f.Canvas.sortingOrder > top.Canvas.sortingOrder) top = f;
            }

            for (int i = 0; i < open.Count; i++)
            {
                var f = open[i];
                if (f == null) continue;
                f.CanvasGroup.blocksRaycasts = !(top != null && f != top && IsVisuallyBelow(f, top));
            }
        }

        /// <summary>视觉层级判下方：更低层级组，或同组内更早打开（栈序在前）。</summary>
        private bool IsVisuallyBelow(UIForm f, UIForm top)
        {
            if (f.Info.Layer != top.Info.Layer) return f.Info.Layer < top.Info.Layer;
            var open = _query.StackOf(f.Info.Layer);
            if (open == null) return false;
            int fi = -1, ti = -1;
            for (int i = 0; i < open.Count; i++)
            {
                if (ReferenceEquals(open[i], f)) fi = i;
                if (ReferenceEquals(open[i], top)) ti = i;
            }
            return fi >= 0 && ti >= 0 && fi < ti;
        }
    }
}
