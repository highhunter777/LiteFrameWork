using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 恢复模式（《状态机专项设计》§3.4）： <see cref="Hooks"/> = 复用迁移事务跑回调（分歧尾深→浅 `OnLeave`、目标尾浅→深 `OnEnter`。
    /// </summary>
    public enum FsmRestoreMode : byte
    {
        Hooks = 0,
        Silent = 1,
    }

    /// <summary>
    /// 状态机结构快照（《状态机专项设计》§3.4）：活动路径（平面机 = 单点）、复合历史（层级机）、驻留秒/帧与迁移/看门狗计数。
    /// </summary>
    public sealed class FsmCapture<TId> where TId : struct
    {
        internal TId[] ActivePath;                     // 根 → 最深活动态（平面机长度 1）
        internal Dictionary<TId, TId[]> History;       // 层级机复合历史（浅 = 1 项 / 深 = 链）；平面机 null
        internal float StageTime;                      // 浮点按位捕获（Restore 原样置回）
        internal int StageFrames;
        internal long TransitionCount;
        internal int TimeoutCount;
    }
}
