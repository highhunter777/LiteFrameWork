using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using UnityEngine;
using LiteGame.Feedback;

namespace LiteGame
{
    /// <summary>
    /// 命中分发器（《命中反馈与伤害数字专项设计》§4.1）：帧事件 → 命中反馈消费者的**编排方接线件**。
    /// </summary>
    public sealed class HitFeedbackDispatcher : IDisposable
    {
        private readonly SimView _view;
        private readonly IEventSubscription _subscription;
        private readonly List<IHitFeedbackConsumer> _consumers = new List<IHitFeedbackConsumer>(4);
        private bool _disposed;

        /// <summary>已注册消费者数（诊断/测试面）。</summary>
        public int ConsumerCount => _consumers.Count;

        public HitFeedbackDispatcher(SimView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _subscription = _view.Events?.SubscribeSignal<FrameEvent>(OnFrameEvent);   // 静默门之后
        }

        /// <summary>注册消费者（转发序 = 注册序）；null 拒收、重复注册幂等。</summary>
        public void Register(IHitFeedbackConsumer consumer)
        {
            if (_disposed || consumer == null || _consumers.Contains(consumer)) return;
            _consumers.Add(consumer);
        }

        /// <summary>摘除消费者（Dispose 后调用为空操作）。</summary>
        public bool Unregister(IHitFeedbackConsumer consumer)
        {
            return !_disposed && _consumers.Remove(consumer);
        }

        /// <summary>
        /// 权威反馈注入点（公共战斗事件面 → Death/Hit/Crit）：与 Sim 帧事件走**同一条**消费者解析/分发， 但不受静默门影响（权威事实不参与预测去重）。
        /// </summary>
        public void DispatchAuthoritative(in FrameEvent e) => OnFrameEvent(in e);

        private void OnFrameEvent(in FrameEvent e)
        {
            if (_disposed || _consumers.Count == 0) return;

            HitFeedbackContext ctx = Resolve(in e);
            for (int i = 0; i < _consumers.Count; i++)
                _consumers[i].OnHitFeedback(in ctx);
        }

        /// <summary>
        /// 事件解析（一次性、集中在此——消费者不重复解析）： - 槽位：SimView.TryGetSlot（实体 Id → 稳定槽位）。
        /// </summary>
        private HitFeedbackContext Resolve(in FrameEvent e)
        {
            int slot = _view.TryGetSlot(e.EntityId, out int s) ? s : -1;
            int otherSlot = e.OtherId != 0 && _view.TryGetSlot(e.OtherId, out int os) ? os : -1;

            HitLocalRole role = ResolveLocalRole(in e);

            return new HitFeedbackContext(e.Kind, e.EntityId, e.OtherId,
                slot, otherSlot, e.Value, new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z), role);
        }

        /// <summary>
        /// 本地角色判定（**按事件语义分派，不按"主体/对象"一刀切**）。
        /// </summary>
        private HitLocalRole ResolveLocalRole(in FrameEvent e)
        {
            HitLocalRole role = HitLocalRole.Bystander;
            long local = _view.LocalEntityId;
            if (local == 0) return role;              // 本地未对齐：如实旁观，不猜

            if (e.Kind == FrameEventKind.Fire || e.Kind == FrameEventKind.ObstacleHit)
                return e.EntityId == local ? HitLocalRole.Caused : HitLocalRole.Bystander;

            if (e.EntityId == local) return HitLocalRole.Received;
            if (e.OtherId != 0 && e.OtherId == local) return HitLocalRole.Caused;
            return HitLocalRole.Bystander;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();                 // 先摘订阅：此后到达的事件一律不再进入本类
            _consumers.Clear();
        }
    }
}
