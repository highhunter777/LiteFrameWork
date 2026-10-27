using System;
using LiteClient;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 权威命中/击杀事件 → 表现反馈：<see cref="PublicCombatFeed"/> 的命中/阵亡队列（含他人命中/击杀）
    /// 交给 <see cref="HitFeedbackDispatcher"/>——命中数字/受击特效/命中音效/阵亡归属不再依赖
    /// "本地预测的帧事件"（受害方本地从不模拟敌方开火，旧路径下自己阵亡永远收不到归属/音效；
    /// 预测命中则可能在服务端未命中时误显）。去重已在 <see cref="PublicCombatFeed"/> 完成。
    /// </summary>
    internal sealed class AuthoritativeCombatFeedback : IDisposable
    {
        private readonly PublicCombatFeed _feed;
        private readonly SimView _view;
        private readonly HitFeedbackDispatcher _dispatcher;
        private bool _disposed;

        public AuthoritativeCombatFeedback(PublicCombatFeed feed, SimView view,
            HitFeedbackDispatcher dispatcher)
        {
            _feed = feed;
            _view = view;
            _dispatcher = dispatcher;
        }

        /// <summary>每渲染帧消费权威队列——**队列而非事件**：视图 Attach 前到达的事件
        /// （或一帧内多批事件）不会丢反馈；命中先于死亡（同帧击杀的听感/飘字顺序稳定）。</summary>
        public void Tick()
        {
            if (_disposed || _dispatcher == null) return;

            if (_feed != null)
            {
                while (_feed.TryDequeueHit(out PublicCombatFeed.HitEntry hit))
                {
                    var e = new FrameEvent
                    {
                        Kind = hit.Crit ? FrameEventKind.Crit : FrameEventKind.Hit,
                        EntityId = hit.Target,
                        OtherId = hit.Shooter,
                        Value = hit.Value,
                        Pos = new SimVector3(hit.PosX, hit.PosY, hit.PosZ),
                    };
                    _dispatcher.DispatchAuthoritative(in e);
                }

                while (_feed.TryDequeueKillFeedback(out PublicCombatFeed.KillEntry kill))
                {
                    Vector3 pos = Vector3.zero;
                    if (_view != null && _view.TryGetSlot(kill.Victim, out int slot)
                        && _view.TryGetView(slot, out GameObject go) && go != null)
                        pos = go.transform.position;

                    var e = new FrameEvent
                    {
                        Kind = FrameEventKind.Death,
                        EntityId = kill.Victim,
                        OtherId = kill.Killer,
                        Pos = new SimVector3(pos.x, pos.y, pos.z),
                    };
                    _dispatcher.DispatchAuthoritative(in e);
                }
            }
        }

        public void Dispose() => _disposed = true;
    }
}
