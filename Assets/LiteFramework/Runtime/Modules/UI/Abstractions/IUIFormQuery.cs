using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>窄查询口：模态栈需要的一切界面事实（由壳服务实现）。
    /// 抽这一层是为了让本类**不认识实例池与字典**——可注入替身独立测试。</summary>
    public interface IUIFormQuery
    {
        /// <summary>仍打开的全部界面（Active/Covered/Paused）——枚举契约由实现方保证
        /// （遍历期间不得改集合，见壳服务 Tick 的快照纪律）。</summary>
        void CollectOpen(List<UIForm> into);

        /// <summary>某层级组的已打开栈（判"同组谁更上"用）；无该层 → null。</summary>
        IReadOnlyList<UIForm> StackOf(int layer);

        /// <summary>最高层级号（层级组总数 − 1 恒成立时即为末层；无任何层时 -1）。
        /// Back 目标回退要"从最高层往下扫"，需知道扫描上界。</summary>
        int HighestLayer { get; }
    }
}
