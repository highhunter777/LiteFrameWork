using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 阶段契约：状态逻辑写成类，**状态标识是任意结构体** <typeparamref name="TId"/>（推荐 `enum`/`int` 等实现
    /// `IEquatable` 的类型以获免装箱比较）。状态对象是**无状态单例**——禁实例字段（同一实例会被多个机器共用），
    /// 可变数据走 payload 或宿主（驻留帧数读 <see cref="IStageHost{TId,TReq}.StageFrames"/>）。
    /// </summary>
    public interface IStage<TId, TReq> where TId : struct
    {
        void OnInit(IStageHost<TId, TReq> m);
        void OnEnter(IStageHost<TId, TReq> m, in TReq req);
        void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds);
        void OnLeave(IStageHost<TId, TReq> m);
    }
}
