using System;
using System.Collections.Generic;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 命中分发器（《命中反馈与伤害数字专项设计》§4.1）：帧事件 → 命中反馈消费者的**编排方接线件**。
    ///
    /// **接在哪**：<c>SimView.EventSink</c> 的**并列订阅者**（构造挂上、Dispose 摘下——多播即
    /// SimView 的编排方接线契约："具体表现（VFX/音效/飘字）由编排方接线"）。动画缝驱动
    /// （<c>CharacterLocomotionDriver</c>）保持构造自订阅不动——其"构造即订阅"是既有契约且被
    /// 帧事件开火全链 EditMode 用例锁死，收编无功能增益、纯破坏面（实施修订——见专项设计 §4.1 修订段）。
    ///
    /// **职责**：把 <see cref="FrameEvent"/> 解析成 <see cref="HitFeedbackContext"/>（槽位解析、
    /// 事件位置直映世界、本地角色判定），按注册序转发给全部 <see cref="IHitFeedbackConsumer"/>。
    /// **不裁决**：本地过滤在消费者内做（旁观事件对特效/音效同样可见）；不持业务状态；
    /// 不做消费者清理（消费者自管生命周期，Unregister 归其所有者）。
    ///
    /// **不做**：不改 Sim；VFX/音效本体归各服务；伤害数字归 <c>BattleDamageNumberDriver</c>。
    /// </summary>
    public sealed class HitFeedbackDispatcher : IDisposable
    {
        private readonly SimView _view;
        private readonly List<IHitFeedbackConsumer> _consumers = new List<IHitFeedbackConsumer>(4);
        private bool _disposed;

        /// <summary>已注册消费者数（诊断/测试面）。</summary>
        public int ConsumerCount => _consumers.Count;

        public HitFeedbackDispatcher(SimView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _view.EventSink += OnFrameEvent;          // 并列订阅（§8 接缝之后、静默门之后）
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

        private void OnFrameEvent(in FrameEvent e)
        {
            if (_disposed || _consumers.Count == 0) return;

            HitFeedbackContext ctx = Resolve(in e);
            for (int i = 0; i < _consumers.Count; i++)
                _consumers[i].OnHitFeedback(in ctx);
        }

        /// <summary>
        /// 事件解析（一次性、集中在此——消费者不重复解析）：
        /// - 槽位：<see cref="SimView.TryGetSlot"/>（实体 Id → 稳定槽位；失败 = -1）；
        /// - 位置：Sim 空间直映世界（与 <see cref="SimView.LocalDisplayPosition"/> 同式）；
        /// - 本地角色：**按事件语义分派**（见 <see cref="ResolveLocalRole"/>）。
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
        ///
        /// 事件的 <c>EntityId</c> 语义随 Kind 而变（见 <see cref="FrameEvent"/>）：
        /// <list type="bullet">
        /// <item><b>Hit / Crit</b>：主体 = 命中<b>目标</b> ⇒ 主体是本地 = 我挨打 ⇒ <b>承受</b>
        ///   （自伤优先承受档）；对象 = 射手，对象是本地 ⇒ <b>造成</b>。</item>
        /// <item><b>Fire</b>：主体 = <b>开火者</b>（<c>OtherId</c> 恒 0）⇒ 主体是本地 = <b>我开枪</b>
        ///   ⇒ <b>造成</b>。**这一条必须单独分派**：主体"是本地"在 Fire 里的含义与 Hit 相反，
        ///   一刀切会把开火反馈按"我被击中"口径驱动——消费者（音效/特效）按 role 选表现，
        ///   结果是开火响受击声。Fire 无对象，故不存在"对象是本地"的分支。</item>
        /// <item><b>Death</b>：主体 = 死者 ⇒ 主体是本地 = 我阵亡 ⇒ 承受（击杀归属对象侧）。</item>
        /// </list>
        /// </summary>
        private HitLocalRole ResolveLocalRole(in FrameEvent e)
        {
            HitLocalRole role = HitLocalRole.Bystander;
            long local = _view.LocalEntityId;
            if (local == 0) return role;              // 本地未对齐：如实旁观，不猜

            if (e.Kind == FrameEventKind.Fire)
                return e.EntityId == local ? HitLocalRole.Caused : HitLocalRole.Bystander;

            if (e.EntityId == local) return HitLocalRole.Received;
            if (e.OtherId != 0 && e.OtherId == local) return HitLocalRole.Caused;
            return HitLocalRole.Bystander;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _view.EventSink -= OnFrameEvent;          // 先摘订阅：此后到达的事件一律不再进入本类
            _consumers.Clear();
        }
    }
}
