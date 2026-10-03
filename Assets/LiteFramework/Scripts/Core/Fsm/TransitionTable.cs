using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 迁移表（《状态机专项设计》§3.1）：状态机上的"自动迁移"配置——三种边共用一份**声明序**评估。
    /// 边 = 配置：Start 前完成注册；机器 Start 封板后加边当场抛；守卫失败 = 边不触发（静默正常路径）。
    /// 平面机/层级机组合挂载同一实例，两机的三种边语义一致；评估由机器转调
    /// （命中 = From 匹配当前态（或 Any）&& guard 通过；声明序首个命中生效）。
    /// </summary>
    public sealed class TransitionTable<TId, TReq>
        where TId : struct
    {
        private sealed class Edge
        {
            public Type EventType;         // null = 条件边（Tick 评估）；否则 = 事件边（Raise 评估）
            public TId From;
            public bool IsAny;             // From 通配（任意活动态）
            public TId To;
            public Delegate Payload;       // Func<TEvt, TReq>——事件边；null = default 载荷
            public Delegate Guard;         // Func<TEvt, bool>（事件边）/ Func<bool>（条件边）；null = 恒真
            public Edge Next;              // 声明序链表（保序插入）
        }

        private Edge _head;
        private Edge _tail;
        private bool _sealed;

        /// <summary>机器 Start 时封板：此后 AddEdge/加边一律抛（边是只读配置——§3.6）。</summary>
        internal void Lock()
        {
            _sealed = true;
        }

        /// <summary>事件边：发 <typeparamref name="TEvt"/> 时从 from 到 to；载荷由 toPayload 由事件构造（null = default）。</summary>
        public void AddEdge<TEvt>(TId from, TId to, Func<TEvt, TReq> toPayload = null, Func<TEvt, bool> guard = null) where TEvt : class
            => Add(new Edge { EventType = typeof(TEvt), From = from, To = to, Payload = toPayload, Guard = guard });

        /// <summary>条件边：Tick 评估（OnUpdate 之后、仅当本帧无挂起请求才入队）。</summary>
        public void AddEdge(TId from, TId to, Func<bool> guard = null)
            => Add(new Edge { EventType = null, From = from, To = to, Guard = guard });

        /// <summary>任意态事件边：From 通配（层级机上以最深活动态参与匹配）。</summary>
        public void AddAnyEdge<TEvt>(TId to, Func<TEvt, TReq> toPayload = null, Func<TEvt, bool> guard = null) where TEvt : class
            => Add(new Edge { EventType = typeof(TEvt), IsAny = true, To = to, Payload = toPayload, Guard = guard });

        /// <summary>任意态条件边。</summary>
        public void AddAnyEdge(TId to, Func<bool> guard = null)
            => Add(new Edge { EventType = null, IsAny = true, To = to, Guard = guard });

        private void Add(Edge e)
        {
            if (_sealed) throw new InvalidOperationException("迁移表已封板（Start 之后边是只读配置）");
            var cmp = EqualityComparer<TId>.Default;
            if (cmp.Equals(e.From, e.To))
                throw new ArgumentException($"迁移边 To == From（重入禁止）:{e.To}", nameof(e));
            if (_tail == null) _head = _tail = e;
            else { _tail.Next = e; _tail = e; }
        }

        /// <summary>构造期校验（机器挂载时转调）：From/To 必须已注册（Any 免 From）；委托 null 合法。</summary>
        internal void Validate(Predicate<TId> isRegistered)
        {
            var cmp = EqualityComparer<TId>.Default;
            for (var edge = _head; edge != null; edge = edge.Next)
            {
                if (!edge.IsAny && !isRegistered(edge.From))
                    throw new InvalidOperationException($"迁移边 From 未注册:{edge.From}");
                if (!isRegistered(edge.To))
                    throw new InvalidOperationException($"迁移边 To 未注册:{edge.To}（边 {edge.From} → {edge.To}）");
            }
        }

        /// <summary>事件边评估：声明序首个（From 匹配 && guard 通过）命中生效。</summary>
        internal bool TryRaiseEdge<TEvt>(TId current, in TEvt e, out TId to, out TReq req)
        {
            to = default;
            req = default;
            var cmp = EqualityComparer<TId>.Default;
            for (var edge = _head; edge != null; edge = edge.Next)
            {
                if (edge.EventType != typeof(TEvt)) continue;
                if (!edge.IsAny && !cmp.Equals(edge.From, current)) continue;
                var guard = (Func<TEvt, bool>)edge.Guard;
                if (guard != null && !guard(e)) continue;   // 守卫失败 = 静默正常路径
                to = edge.To;
                var payload = (Func<TEvt, TReq>)edge.Payload;
                req = payload != null ? payload(e) : default;
                return true;
            }
            return false;
        }

        /// <summary>条件边评估（Tick 时）：声明序首个守卫命中生效。</summary>
        internal bool TryTickEdge(TId current, out TId to, out TReq req)
        {
            to = default;
            req = default;
            var cmp = EqualityComparer<TId>.Default;
            for (var edge = _head; edge != null; edge = edge.Next)
            {
                if (edge.EventType != null) continue;
                if (!edge.IsAny && !cmp.Equals(edge.From, current)) continue;
                var guard = (Func<bool>)edge.Guard;
                if (guard != null && !guard()) continue;
                to = edge.To;
                return true;                                // req = default（无载荷）
            }
            return false;
        }

        /// <summary>图导出（诊断/ExportGraph 用——枚举边）。</summary>
        internal IEnumerable<(Type EventType, TId From, bool IsAny, TId To)> Edges
        {
            get
            {
                for (var edge = _head; edge != null; edge = edge.Next)
                    yield return (edge.EventType, edge.From, edge.IsAny, edge.To);
            }
        }
    }
}
