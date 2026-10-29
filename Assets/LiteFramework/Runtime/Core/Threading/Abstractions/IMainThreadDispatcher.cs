using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteFramework
{
    public interface IMainThreadDispatcher : ITickable
    {
        void Post(Action action);   // 任意线程可调；主线程帧首执行
    }
}
