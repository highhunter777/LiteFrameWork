using System;
using LiteFramework;
using LiteSim;
using UnityEngine;
using LiteGame.Modules.Battle.Shared;

namespace LiteGame
{
    /// <summary>
    /// 权威命中/击杀事件 → 表现反馈：PublicCombatFeed 的命中/阵亡队列（含他人命中/击杀） 交给 HitFeedbackDispatcher——命中数字/受击特效/命中音效/阵亡归属不再依赖 "本地预测的帧。
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
