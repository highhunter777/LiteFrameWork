using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>通道非泛型标记(存储与统计用)——批2 追加诊断面成员（镜像 ReferencePoolInfo 形态）。</summary>
    internal interface IEventChannel
    {
        int SubscriberCount { get; }
        Type EventType { get; }
        long PublishedCount { get; }
        long DispatchErrors { get; }
    }

}
