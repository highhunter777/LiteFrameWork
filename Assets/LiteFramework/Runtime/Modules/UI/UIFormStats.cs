using System.Collections.Generic;

namespace LiteClient.UI
{
    /// <summary>
    /// UI 运行统计投影（从 <see cref="UIService"/> 外提的"统计"域，M3-6 §4.5）：
    /// 把实例状态分布、待换表数、转场相位/队列摊平成 HUD/诊断读数的键值对。
    /// 只读投影——不改任何状态；契约同 <see cref="LiteFramework.IModuleStats"/>（Clear + 填）。
    /// </summary>
    internal static class UIFormStats
    {
        public static void Snapshot(UIFormStore store, UITransitionRunner transitions, int staleCount,
            Dictionary<string, string> into)
        {
            into.Clear();                                    // 契约：实现 Clear + 填（HUD 不清，全实现统一）
            int active = 0, covered = 0, paused = 0, recycled = 0, loading = 0;
            foreach (var form in store.All)
            {
                switch (form.State)
                {
                    case UIFormState.Active: active++; break;
                    case UIFormState.Covered: covered++; break;
                    case UIFormState.Paused: paused++; break;
                    case UIFormState.Recycled: recycled++; break;
                    case UIFormState.Loading: loading++; break;
                }
            }
            into["打开"] = active.ToString();
            into["遮盖"] = covered.ToString();
            into["暂停"] = paused.ToString();
            into["池中"] = recycled.ToString();
            into["加载中"] = loading.ToString();
            into["待更新"] = staleCount.ToString();
            into["转场"] = transitions.Phase.ToString();
            into["转场队列"] = transitions.QueueLength.ToString();
        }
    }
}
