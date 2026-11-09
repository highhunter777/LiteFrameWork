using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>通道非泛型标记（存储与统计用）——批2 追加诊断面成员。</summary>
    internal interface IEventChannel
    {
        int SubscriberCount { get; }
        Type EventType { get; }
        long PublishedCount { get; }
        long DispatchErrors { get; }

        /// <summary>清空全部订阅（总线 Dispose 用；句柄之后 Dispose 为 no-op）。</summary>
        void Clear();
    }

}
