using System;
using System.Globalization;

namespace LiteFramework
{
    /// <summary>
    /// 设置项的**非泛型面**：只需要"回默认"与"默认值快照"的消费者（恢复默认、设置项清单）
    /// 不应为每个类型写一遍。`Setting&lt;T&gt;` 实现它，`GameSettings.Settings` 以它枚举全部项。
    /// </summary>
    public interface ISetting
    {
        /// <summary>该设置项的默认值（写入同值即回到默认；不删除键——删除语义归 SettingService）。</summary>
        void ResetToDefault();
    }
}
