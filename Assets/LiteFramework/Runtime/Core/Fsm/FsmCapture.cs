using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 恢复模式（《状态机专项设计》§3.4）：
    /// <see cref="Hooks"/> = 复用迁移事务跑回调（分歧尾深→浅 `OnLeave`、目标尾浅→深 `OnEnter`，
    /// default payload）——存档恢复、GM/测试跳态；
    /// <see cref="Silent"/> = 纯结构置换**零回调**——帧同步回滚/确定性重放（重放驱动会重新走回调）。
    /// </summary>
    public enum FsmRestoreMode : byte
    {
        Hooks = 0,
        Silent = 1,
    }

    /// <summary>
    /// 状态机结构快照（《状态机专项设计》§3.4）：活动路径（平面机 = 单点）、复合历史（层级机）、
    /// 恢复栈（抢占型）、驻留秒/帧与迁移/看门狗计数。由 `Capture()` 一次性分配产出
    /// （捕获时机外零分配——§1 稳态零 GC 红线），`Restore` 原样置回（浮点按位，不参与任何计算）。
    ///
    /// "重建后恢复"由调用方保证结构一致：capture 内 id 未注册（机器结构已变）→ `Restore` 抛；
    /// 运行期改图非目标（§3.6）。快照不可变，可跨机器/多次复用（同构机器）。
    /// </summary>
    public sealed class FsmCapture<TId> where TId : struct
    {
        internal TId[] ActivePath;                     // 根 → 最深活动态（平面机长度 1）
        internal Dictionary<TId, TId[]> History;       // 层级机复合历史（浅 = 1 项 / 深 = 链）；平面机 null
        internal TId[] ResumeStack;                    // 恢复栈（底 → 顶）；非抢占型机体 null
        internal int ResumeDropped;
        internal float StageTime;                      // 浮点按位捕获（Restore 原样置回）
        internal int StageFrames;
        internal long TransitionCount;
        internal int TimeoutCount;
    }
}
