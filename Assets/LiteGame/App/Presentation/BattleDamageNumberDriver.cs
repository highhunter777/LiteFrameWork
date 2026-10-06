using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteSim.View;
using LiteSim.View.DamageNumbers;
using TMPro;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 伤害数字渲染驱动（《命中反馈与伤害数字专项设计》§4.3）——<see cref="BattleLaserDriver"/> 同款纪律：
    /// sealed、IDisposable、只读 Sim 事实、不改 Sim。
    ///
    /// **消费面**：<see cref="IHitFeedbackConsumer"/>（经 <see cref="HitFeedbackDispatcher"/> 注册）——
    /// 只吃 Hit/Crit 且**本地相关性过滤**（只显示本地玩家造成/承受——口径见 <see cref="HitLocalRole"/>）；
    /// 死亡播报/开火不进飘字（各自归属动画/特效批次）。
    ///
    /// **职责分工**：合并窗口与账目归 <see cref="DamageNumberAggregator"/>（纯件，L1 把守）；
    /// 每帧位形归 <see cref="DamageNumberMotion"/>（纯件）；本驱动只做实例池、文本/样式落 TMP、
    /// 跟随与到期收口——**轨道数学/渲染件可替换**（§5 升级路径：TMP 直渲 → 网格提取 → 合批，
    /// 换渲染件不动本类之外的纯件）。
    ///
    /// **生命周期**：实例经 <see cref="ObjectPool{T}"/> 池化（onGet/onRelease 只翻转激活态）；
    /// 条目到期（聚合器裁决）→ 实例归还＋摘账（账实一致）；目标 despawn/视图回收 →
    /// 冻结于最后锚点继续淡出（不悬挂、不瞬移）；同屏超 <see cref="Budget"/> → 淘汰最旧并**记日志**
    /// （不静默丢）。
    ///
    /// **时钟**：合并窗口与寿命走注入的 <see cref="IWorldClock"/>（世界时钟——VfxService 同款纪律），
    /// **不累加渲染帧 delta**——实测编辑器态 `Time.unscaledDeltaTime` 会整体通胀数倍（背景节流态
    /// unscaledTime 跑 2.6~3.5 倍墙钟），累加 delta 会把淡出窗压扁到几百毫秒；世界时钟保持 1:1 且
    /// 变速/暂停语义内建（测试模式 TimeScale 即经它生效）。
    /// </summary>
    public sealed class BattleDamageNumberDriver : IDisposable, IHitFeedbackConsumer
    {
        /// <summary>同屏上限；超限淘汰最旧并记日志（不静默丢）。</summary>
        public const int Budget = 24;

        // 相机距离缩放（本工程相机自适配——参考距离 22m 持平，近大远小钳制）
        private const float CameraReferenceDistance = 22f;
        private const float CameraScaleMin = 0.6f;
        private const float CameraScaleMax = 1.4f;

        private readonly SimView _view;
        private readonly Func<TextMeshPro> _factory;
        private readonly IWorldClock _clock;
        private readonly IDamageNumberStyleResolver _style;
        private readonly ObjectPool<GameObject> _pool;
        private readonly DamageNumberAggregator _aggregator = new DamageNumberAggregator();
        private readonly Dictionary<(long TargetId, HitLocalRole Role), Active> _active =
            new Dictionary<(long, HitLocalRole), Active>(16);
        private readonly List<(long TargetId, HitLocalRole Role)> _expired = new List<(long, HitLocalRole)>(8);
        private readonly List<(long TargetId, HitLocalRole Role)> _tickKeys = new List<(long, HitLocalRole)>(16);
        private int _seedCounter;
        private bool _disposed;
        private bool _ticking;     // 诊断探针：Tick 期间命中到达 = 存在未定位的重入面（见 OnHitFeedback）

        /// <summary>在册飘字数（诊断/预算断言面）。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>在册条目的 TMP 实例（诊断/测试读取面；无条目 = null——只读，不构成运行时依赖口）。</summary>
        public TextMeshPro TryGetActive(long targetId, HitLocalRole role)
        {
            return _active.TryGetValue((targetId, role), out var a) ? a.Tmp : null;
        }

        private struct Active
        {
            public TextMeshPro Tmp;
            public int Slot;               // 跟随槽位（-1 = 无跟随，锚定命中点）
            public Vector3 LastAnchor;     // 最后锚点（槽位视图消失后冻结于此——不悬挂）
            public float R, G, B;          // 档位色（alpha 由轨道逐帧给——淡出归轨道数学）
            public float FontSize;
            public float ScaleBoost;
            public double SpawnedAt;       // 淘汰"最旧"的判据
        }

        public BattleDamageNumberDriver(SimView view, Func<TextMeshPro> factory, IWorldClock clock,
            IDamageNumberStyleResolver style = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _style = style ?? new DamageNumberStyleResolver();
            _pool = new ObjectPool<GameObject>(
                () => _factory().gameObject,
                onGet: go => go.SetActive(true),
                onRelease: go => go.SetActive(false),
                maxIdle: Budget, statsName: "Pool.BattleDamageNumber");
        }

        // ---- IHitFeedbackConsumer（逻辑帧边界——分发器按注册序转发）----

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;
            if (ctx.Kind != FrameEventKind.Hit && ctx.Kind != FrameEventKind.Crit) return;   // 死亡/开火不进飘字
            if (ctx.LocalRole == HitLocalRole.Bystander) return;                             // 口径：只显本地造成/承受

            // 诊断探针（真机实测 Tick 枚举曾被中途修改——InvalidOperation 一次，静态排查枚举体内
            // 全部外部调用均为纯读，重入面未定位）：命中本应只在逻辑帧边界（Tick 外）到达；
            // 若 Tick 期间到达，键快照遍历已结构性容错（本条同帧入账/新键下帧拾取），此日志留证溯源。
            if (_ticking)
                Debug.LogWarning("[Battle] 伤害数字：Tick 期间命中到达（重入面留证）——键快照已容错，请回溯本日志的调用栈");

            var snap = _aggregator.MergeOrSpawn(ctx.EntityId, ctx.LocalRole, ctx.Value,
                ctx.Kind == FrameEventKind.Crit, _clock.Now, _seedCounter++);

            if (snap.Merged)
            {
                ApplyEntry((ctx.EntityId, ctx.LocalRole), in snap);       // 连击：更新既有条目（文本/档位升级）
                return;
            }

            if (_active.Count >= Budget) EvictOldest();                    // 预算满：先淘汰最旧（记日志）

            var style = _style.Resolve(snap.Value, snap.Role, snap.Crit);
            TextMeshPro tmp = _pool.Acquire().GetComponent<TextMeshPro>();

            // 跟随锚：槽位可解析 → 目标视图位；否则锚定命中点（诚实退化——不造第二视觉源）
            Vector3 anchor = ctx.WorldPos;
            if (ctx.Slot >= 0 && _view.TryGetView(ctx.Slot, out var v) && v != null)
                anchor = v.transform.position;

            _active[(ctx.EntityId, ctx.LocalRole)] = new Active
            {
                Tmp = tmp,
                Slot = ctx.Slot,
                LastAnchor = anchor,
                R = style.R, G = style.G, B = style.B,
                FontSize = style.FontSize,
                ScaleBoost = style.ScaleBoost,
                SpawnedAt = snap.SpawnedAt,
            };
            tmp.text = DamageNumberFormatter.Format(snap.Value);
        }

        /// <summary>连击合并落既有实例：累计文本重写、暴击档升级（色/字号/缩放重解析）。</summary>
        private void ApplyEntry((long, HitLocalRole) key, in DamageNumberSnapshot snap)
        {
            if (!_active.TryGetValue(key, out var a))
            {
                _aggregator.Remove(key.Item1, key.Item2);      // 账实对齐：实例已不在（理论不达——防御收账）
                return;
            }

            var style = _style.Resolve(snap.Value, snap.Role, snap.Crit);
            a.R = style.R; a.G = style.G; a.B = style.B;
            a.FontSize = style.FontSize;
            a.ScaleBoost = style.ScaleBoost;
            _active[key] = a;
            a.Tmp.text = DamageNumberFormatter.Format(snap.Value);
        }

        private void EvictOldest()
        {
            (long, HitLocalRole) oldest = default;
            double oldestAt = double.MaxValue;
            foreach (var kv in _active)
            {
                if (kv.Value.SpawnedAt < oldestAt)
                {
                    oldestAt = kv.Value.SpawnedAt;
                    oldest = kv.Key;
                }
            }

            Debug.LogWarning($"[Battle] 伤害数字超预算（{Budget}）——淘汰最旧并释放其实例（不静默丢）：目标 {oldest.Item1}");
            Release(oldest);
        }

        /// <summary>每渲染帧视觉推进（ProcedureBattle.OnUpdate 末段——视图/激光之后的下游消费者）。
        /// 时钟读注入的世界钟（单次快照，同 Tick 内一致）；② 段走**键快照遍历**（收集段零外部调用、
        /// 遍历段缺键容错）——枚举不持有跨外部调用的活枚举器，即使 Tick 中途 _active 被修改
        /// （真机实测出现过一次未定位的重入面）也不炸：缺键跳过、新键下帧拾取。</summary>
        public void Tick()
        {
            if (_disposed) return;
            double now = _clock.Now;
            _ticking = true;
            try
            {
                // ① 到期收口：聚合器裁决（淡出钟），实例归还 + 摘账——账实一致
                _aggregator.CollectExpired(now, _expired);
                for (int i = 0; i < _expired.Count; i++) Release(_expired[i]);

                // ② 视觉推进：轨道数学 → 位形/透明度；跟随槽位视图（消失 → 冻结最后锚点）
                Camera cam = Camera.main;

                _tickKeys.Clear();
                foreach (var key in _active.Keys) _tickKeys.Add(key);   // 收集段：纯 Add，无外部调用——无重入窗口
                for (int i = 0; i < _tickKeys.Count; i++)
                {
                    var key = _tickKeys[i];
                    if (!_active.TryGetValue(key, out var a)) continue;               // 快照后被移除（到期/淘汰/重入）→ 容错跳过
                    if (!_aggregator.TryGet(key.Item1, key.Item2, out var snap)) continue;

                    DamageNumberMotion.Evaluate(
                        (float)(now - snap.SpawnedAt), (float)(now - snap.LastMergeAt),
                        snap.Crit, snap.Seed,
                        out float rise, out float push, out float alpha, out float shake, out float pop);

                    if (a.Slot >= 0 && _view.TryGetView(a.Slot, out var v) && v != null)
                        a.LastAnchor = v.transform.position;                 // 跟随；despawn 后保持最后锚点

                    Vector3 pos = new Vector3(
                        a.LastAnchor.x + push + shake,
                        a.LastAnchor.y + DamageNumberMotion.HoverHeight + rise,
                        a.LastAnchor.z);

                    float camScale = 1f;
                    if (cam != null)
                    {
                        float d = Vector3.Distance(cam.transform.position, pos);
                        camScale = Mathf.Clamp(d / CameraReferenceDistance, CameraScaleMin, CameraScaleMax);
                    }

                    a.Tmp.transform.position = pos;
                    float scale = pop * a.ScaleBoost * camScale;
                    a.Tmp.transform.localScale = new Vector3(scale, scale, scale);
                    a.Tmp.fontSize = a.FontSize;
                    a.Tmp.color = new Color(a.R, a.G, a.B, alpha);
                    _active[key] = a;                                        // 键经 TryGetValue 刚确认在册——纯值更新
                }
            }
            finally
            {
                _ticking = false;
            }
        }

        /// <summary>释放条目：实例归还池（翻转激活态）＋聚合器摘账。</summary>
        private void Release((long TargetId, HitLocalRole Role) key)
        {
            if (!_active.TryGetValue(key, out var a)) return;
            _active.Remove(key);
            _aggregator.Remove(key.TargetId, key.Role);
            if (a.Tmp != null) _pool.Release(a.Tmp.gameObject);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var keys = new List<(long, HitLocalRole)>(_active.Keys);
            for (int i = 0; i < keys.Count; i++) Release(keys[i]);
        }
    }
}
