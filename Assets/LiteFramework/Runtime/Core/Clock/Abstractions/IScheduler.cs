using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IScheduler : ITickable, IModuleStats
    {
        int Schedule(float delaySeconds, Action callback);   // 返回取消 id；delay 用"游戏秒"（受变速影响）
        void Cancel(int id);
    }
}
