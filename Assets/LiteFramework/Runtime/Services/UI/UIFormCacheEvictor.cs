using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 缓存预算淘汰（从 <see cref="UIService"/> 外提的"缓存淘汰策略"域，M3-6 §4.5）：
    /// 池中实例数超过预算时按 LRU 升序淘汰（Resident 不参与；只淘汰未打开实例），
    /// 淘汰 = 走 <see cref="UIService.Destroy"/> 同一完整销毁路径（租约释放）。
    ///
    /// **边界**：只做"选谁淘汰 + 触发销毁"；登记/租约/使用序号由 <see cref="UIFormStore"/> 记账，
    /// 预算值由服务持有（<see cref="UIService.CacheBudget"/>）。
    /// </summary>
    internal sealed class UIFormCacheEvictor
    {
        private readonly UIFormStore _store;
        private readonly Action<int> _destroy;
        private readonly List<(int id, long used)> _candidates = new List<(int id, long used)>(16);   // 复用，零分配

        /// <param name="destroy">完整销毁（服务注入为 <c>DestroyFormInternal</c>——摘登记+销毁+放租约）。</param>
        public UIFormCacheEvictor(UIFormStore store, Action<int> destroy)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
        }

        /// <summary>超预算即淘汰（幂等：未超 = 无动作）。</summary>
        public void Evict(int budget)
        {
            int cached = 0;
            foreach (var form in _store.All)
                if (form.State == UIFormState.Recycled) cached++;
            if (cached <= budget) return;

            // 候选 = 池中 LRU 策略实例（Resident 不淘汰），按最近使用序升序（最旧先淘汰）
            _candidates.Clear();
            _store.CollectEvictionCandidates(_candidates);
            _candidates.Sort((a, b) => a.used.CompareTo(b.used));

            int toEvict = cached - budget;
            for (int i = 0; i < toEvict && i < _candidates.Count; i++)
            {
                _destroy(_candidates[i].id);
                Log.Info($"UIForm[{_candidates[i].id}] 缓存超出预算 {budget}——LRU 淘汰（租约释放）", LogTag.UI);
            }
        }
    }
}
