using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 图深校验（《状态机专项设计》§3.5，Debug 三宏）：Start 时一次性的可达性扫描 + 死底告警。
    /// </summary>
    internal static class StageGraphDiagnostics<TId, TReq> where TId : struct
    {
        /// <summary>平面机：composites/parent 传 null。</summary>
        internal static void Audit(string machineName,
            Dictionary<TId, IStage<TId, TReq>> stages,
            Dictionary<TId, CompositeSpec<TId>> composites,
            Dictionary<TId, TId> parent,
            TransitionTable<TId, TReq> transitions,
            TId seed)
        {
            var edgesFrom = new Dictionary<TId, List<TId>>();
            var anyTo = new List<TId>();
            if (transitions != null)
                foreach (var e in transitions.Edges)
                {
                    if (e.IsAny) { anyTo.Add(e.To); continue; }
                    if (edgesFrom.TryGetValue(e.From, out var list)) list.Add(e.To);
                    else edgesFrom[e.From] = new List<TId> { e.To };
                }

            var reachable = new HashSet<TId>();
            var queue = new Queue<TId>();
            reachable.Add(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();

                // 祖先链上探：进入子的事务会展开完整路径（根→目标），祖先必经。
                if (parent != null)
                {
                    var cur = id;
                    while (parent.TryGetValue(cur, out var p) && reachable.Add(p))
                    {
                        queue.Enqueue(p);
                        cur = p;
                    }
                }

                // 复合树下探：父可达 ⇒ 全部子可达。
                if (composites != null && composites.TryGetValue(id, out var spec))
                    for (int i = 0; i < spec.Children.Count; i++)
                        if (reachable.Add(spec.Children[i])) queue.Enqueue(spec.Children[i]);

                // 时长链。
                if (stages.TryGetValue(id, out var stage) && stage is TableStage<TId, TReq> ts
                    && ts.Spec.HasNext && reachable.Add(ts.Spec.NextId))
                    queue.Enqueue(ts.Spec.NextId);

                // 迁移边。
                if (edgesFrom.TryGetValue(id, out var targets))
                    for (int i = 0; i < targets.Count; i++)
                        if (reachable.Add(targets[i])) queue.Enqueue(targets[i]);
                for (int i = 0; i < anyTo.Count; i++)
                    if (reachable.Add(anyTo[i])) queue.Enqueue(anyTo[i]);
            }

            foreach (var kv in stages)
            {
                if (!reachable.Contains(kv.Key))
                    Log.Warning($"[{machineName}] 阶段 {kv.Key} 静态不可达（仅经带守卫的边可达时属误报，允许保留）", LogTag.Fsm);
                if (kv.Value is TableStage<TId, TReq> spec && spec.Spec.DurationFrames > 0
                    && !spec.Spec.HasNext && spec.Spec.TimeoutFrames <= 0)
                    Log.Warning($"[{machineName}] 阶段 {kv.Key} 配置了时长但无出口（NextId/看门狗均未设置）——到点后死底；AutoResumeOnEnd 自 M3-7 起不再生效", LogTag.Fsm);
            }
        }
    }
}
