using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteFramework
{
    public interface IGameClock : ITickable, IModuleStats
    {
        float TimeScale { get; set; }        // 变速（0.2 慢动作 / 2 加速）
        bool Paused { get; set; }            // 暂停：全停（连 UI 时钟也停？否——分层见下）
        float Now { get; }                   // 游戏时间累计（受变速/暂停影响）
        float ScaledDelta { get; }           // 本帧游戏时间增量：供后两件消费，避免各自反算变速

        
    }
    // ---- 标记子接口:身份由接口表达——注入点拿混即编译错误 ----
    public interface IWorldClock : IGameClock { }   // 游戏时间:受暂停/时停/变速
    public interface IUIClock : IGameClock { }     // UI 时间:受暂停、不受时停
}
