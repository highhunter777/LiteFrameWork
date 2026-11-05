using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface ITimelineRunner : ITickable, IModuleStats
    {
        int MaxStepsPerTick { get; set; }
        ITimeline CreateTimeline();
    }

    public interface ITimeline
    {
        ITimeline At(float seconds, Action action);   // 链式注册动作点
        ITimeline Loop(int loopCount);                // 1=单轮，-1=无限循环
        ITimeline Seek(float seconds);               // 跳转到播放位置，跳过已越过的动作点
        void Start();
        void Stop();                                  // 可中断
        bool Finished { get; }
    }
}
