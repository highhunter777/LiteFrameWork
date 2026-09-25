using System;

namespace LiteGame
{
    /// <summary>UI 打开操作的失败分类（《UI框架总设计》§4.3——失败抛类型化异常，不返回 null 表示进行中）。</summary>
    public enum UIOpenFailure
    {
        /// <summary>权威取消：Close 对在途打开 / 全部等待者退出 / Scope 退出（§4.3）。</summary>
        Canceled = 0,

        /// <summary>prefab 加载失败（含根因，已含 location）。</summary>
        LoadFailed = 1,

        /// <summary>OnInit/OnShow 失败——已回滚（半成品不入池、不进字典，§4.1）。</summary>
        InitFailed = 2,

        /// <summary>同界面打开在途且数据不同——首请求持有数据（§4.3 数据冲突不静默覆盖）。</summary>
        Busy = 3,

        /// <summary>请求被拒绝：Shutdown 后不再接入 / 层级组容量不足等（拒绝发生在创建/入栈/OnShow 之前）。</summary>
        Rejected = 4,

        /// <summary>导航排队等待超时（§4.3"队列有上限、等待超时和可观测拒绝结果"——等待超时未被调度即失败）。</summary>
        Timeout = 5,
    }

    /// <summary>
    /// UI 打开操作的类型化失败（§4.3：携带 formId 与失败分类；调用方按分类决定重试/放弃/上报，不再猜 null）。
    /// 继承 <see cref="InvalidOperationException"/>：与既有 fail-fast 断言/捕获点兼容（打开失败本就是非法状态操作的一种）。
    /// </summary>
    public sealed class UIOpenException : InvalidOperationException
    {
        /// <summary>失败分类。</summary>
        public UIOpenFailure Reason { get; }

        /// <summary>目标界面 id。</summary>
        public int FormId { get; }

        public UIOpenException(UIOpenFailure reason, int formId, string message, Exception inner = null)
            : base(message, inner)
        {
            Reason = reason;
            FormId = formId;
        }
    }
}
