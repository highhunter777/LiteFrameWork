using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteGame.Feedback.DamageNumbers;
using TMPro;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Feedback;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.View;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Feedback;

namespace LiteGame.Modules.Battle.SubModules.CombatSubModule.Damage
{
    /// <summary>
    /// 伤害数字渲染驱动（《命中反馈与伤害数字专项设计》§4.3）——BattleLaserDriver 同款纪律： sealed、IDisposable、只读 Sim 事实、不改 Sim。
    /// </summary>
    public sealed class BattleDamageNumberDriver : IDisposable, IHitFeedbackConsumer
    {
        /// <summary>同屏上限；超限淘汰最旧并记日志（不静默丢）。</summary>
        public const int Budget = 24;

        // 相机距离缩放（本工程相机自适配——参考距离 22m 持平，近大远小钳制）。
        private const float CameraReferenceDistance = 22f;
        private const float CameraScaleMin = 0.6f;
        private const float CameraScaleMax = 1.4f;

        private readonly SimView _view;
        private readonly Func<TextMeshPro> _factory;
        private readonly IWorldClock _clock;
        private readonly IDamageNumberStyleResolver _style;
        private readonly DamageNumberMotionProfile _motion;   // 轨道参数（表驱动；缺省 = 代码常量档）
        private readonly DamageNumberMaterialSet _materials;   // 逐档材质（null = 不换材质——底件默认档；测试/降级形态）
        private readonly Func<UnityEngine.Camera> _cameraOf;         // **对局渲染相机**（billboard 面——不是 Camera.main，见下）
        private readonly IDevSession _dev;               // 开发会话端口（飘字开关；null = 测试装配 = 恒显）
        private readonly ObjectPool<GameObject> _pool;
        private readonly DamageNumberAggregator _aggregator;   // 合并窗/寿命随表驱动轨道参数
        private readonly Dictionary<(long TargetId, HitLocalRole Role, bool Crit), Active> _active =
            new Dictionary<(long, HitLocalRole, bool), Active>(16);
        private readonly List<(long TargetId, HitLocalRole Role, bool Crit)> _expired = new List<(long, HitLocalRole, bool)>(8);
        private readonly List<(long TargetId, HitLocalRole Role, bool Crit)> _tickKeys = new List<(long, HitLocalRole, bool)>(16);
        private int _seedCounter;
        private double _lastTickNow;   // 上帧世界钟读数（锚点追随步长 dt 的来源）
        private bool _disposed;

        /// <summary>在册飘字数（诊断/预算断言面）。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>在册条目的 TMP 实例（诊断/测试读取面；无条目 = null——只读，不构成运行时依赖口）。
        /// 爆头档进键：爆头与普通各成一条，读取时按档查询。</summary>
        public TextMeshPro TryGetActive(long targetId, HitLocalRole role, bool crit)
        {
            return _active.TryGetValue((targetId, role, crit), out var a) ? a.Tmp : null;
        }

        private struct Active
        {
            public TextMeshPro Tmp;
            public Vector3 Anchor;         // 受击点锚（当场位置——向 AnchorTarget 恒速靠近；不随实体移动）
            public Vector3 AnchorTarget;   // 锚点目标（起条目 = 该发受击点；合并 = 最新一发受击点）
            public float TopR, TopG, TopB;          // 渐变顶色（alpha 由轨道逐帧给——淡出归轨道数学）
            public float BottomR, BottomG, BottomB; // 渐变底色（上下同值即平色）
            public float FontSize;
            public float ScaleBoost;
            public double SpawnedAt;       // 淘汰"最旧"的判据
        }

        public BattleDamageNumberDriver(SimView view, Func<TextMeshPro> factory, IWorldClock clock,
            IDamageNumberStyleResolver style = null, Func<UnityEngine.Camera> cameraOf = null,
            DamageNumberMaterialSet materials = null, IDevSession dev = null,
            DamageNumberMotionProfile? motion = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _style = style ?? new DamageNumberStyleResolver();
            _motion = motion ?? DamageNumberMotion.DefaultProfile;
            _aggregator = new DamageNumberAggregator(_motion.MergeWindowSeconds, _motion.FadeEndSeconds);
            // **逐档材质调色板**（批C）：装配点整组注入（缺档即不注入——见 ProcedureBattle 取件）；
            // null = 保持底件默认材质（测试装配/降级形态——观感回到批B 平色，行为不失效）。
            _materials = materials;
            // **相机来源可注入**（billboard 面）：优先由装配点给对局渲染相机（Cinemachine brain 的 OutputCamera）。
            _cameraOf = cameraOf;
            _dev = dev;
            _pool = new ObjectPool<GameObject>(
                () => _factory().gameObject,
                onGet: go => go.SetActive(true),
                onRelease: go => go.SetActive(false),
                maxIdle: Budget, statsName: "Pool.BattleDamageNumber",
                diagnoseDoubleRelease: DevBuildFacts.IsDevelopment);
        }

        // ---- IHitFeedbackConsumer（逻辑帧边界——分发器按注册序转发）----。

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;
            if (ctx.Kind != FrameEventKind.Hit && ctx.Kind != FrameEventKind.Crit) return;   // 死亡/开火不进飘字
            if (ctx.LocalRole == HitLocalRole.Bystander) return;                             // 口径：只显本地造成/承受
            // 开发形态开关：飘字是正式特性（非开发模式恒显），测试房内可由 GM 面板现场开合。
            if (_dev != null && _dev.Active && !_dev.DamageNumbers) return;

            // **同键旧代际收口（账实一致，必须在 `MergeOrSpawn` 之前）**：`_active` 的键是 (目标, 角色, 爆头档)， **同目标同角色同档的条目共用一键**（爆头与普通各成一条——用户裁决"连击合并区分爆头/普通"）。
            bool crit = ctx.Kind == FrameEventKind.Crit;
            var key = (ctx.EntityId, ctx.LocalRole, crit);
            bool merging = _aggregator.WillMerge(ctx.EntityId, ctx.LocalRole, crit, _clock.Now);
            if (!merging && _active.ContainsKey(key)) Release(key);

            var snap = _aggregator.MergeOrSpawn(ctx.EntityId, ctx.LocalRole, ctx.Value,
                crit, _clock.Now, _seedCounter++);

            if (snap.Merged)
            {
                // 同档连击：更新既有条目（文本累计/按值分带缩放重解析）＋**锚点目标改最新受击点** ——合并时数字挪向最新命中处（平滑追随，不瞬跳）。
                ApplyEntry(key, in snap, ctx.WorldPos);
                return;
            }

            if (_active.Count >= Budget) EvictOldest();                    // 预算满：先淘汰最旧（记日志）

            var style = _style.Resolve(snap.Value, snap.Role, snap.Crit);
            TextMeshPro tmp = _pool.Acquire().GetComponent<TextMeshPro>();

            // 逐档材质（批C）：档位含爆头键 ⇒ 新条目按档取材质（同一条目档恒定，合并无需换）。
            // 池实例上一世代的材质会被此处覆盖（不依赖"归还时还原"——那要求记住底件默认值，多一份状态）。
            if (_materials != null) tmp.fontSharedMaterial = _materials.For(style.Tier);

            // 受击点锚（用户裁决："飘字应该在受击部位飘"）——命中事件世界位（弹道命中部位）。
            _active[key] = new Active
            {
                Tmp = tmp,
                Anchor = ctx.WorldPos,
                AnchorTarget = ctx.WorldPos,
                TopR = style.TopR, TopG = style.TopG, TopB = style.TopB,
                BottomR = style.BottomR, BottomG = style.BottomG, BottomB = style.BottomB,
                FontSize = style.FontSize,
                ScaleBoost = style.ScaleBoost,
                SpawnedAt = snap.SpawnedAt,
            };
            tmp.text = DamageNumberFormatter.Format(snap.Value);
        }

        /// <summary>同档连击合并落既有实例：累计文本重写、样式重解析（爆头档恒定；按值分带缩放随累计值变）、
        /// **锚点目标改最新受击点**（<paramref name="anchorTarget"/>——Tick 里以恒速靠近，数字平滑滑向最新命中处）。</summary>
        private void ApplyEntry((long, HitLocalRole, bool) key, in DamageNumberSnapshot snap, Vector3 anchorTarget)
        {
            if (!_active.TryGetValue(key, out var a))
            {
                _aggregator.Remove(key.Item1, key.Item2, key.Item3);   // 账实对齐：实例已不在（理论不达——防御收账）
                return;
            }

            var style = _style.Resolve(snap.Value, snap.Role, snap.Crit);
            a.TopR = style.TopR; a.TopG = style.TopG; a.TopB = style.TopB;
            a.BottomR = style.BottomR; a.BottomG = style.BottomG; a.BottomB = style.BottomB;
            a.FontSize = style.FontSize;
            a.ScaleBoost = style.ScaleBoost;
            a.AnchorTarget = anchorTarget;
            _active[key] = a;
            a.Tmp.text = DamageNumberFormatter.Format(snap.Value);
        }

        private void EvictOldest()
        {
            (long, HitLocalRole, bool) oldest = default;
            double oldestAt = double.MaxValue;
            foreach (var kv in _active)
            {
                if (kv.Value.SpawnedAt < oldestAt)
                {
                    oldestAt = kv.Value.SpawnedAt;
                    oldest = kv.Key;
                }
            }

            Log.Warning($"伤害数字超预算（{Budget}）——淘汰最旧并释放其实例（不静默丢）：目标 {oldest.Item1}", LogTag.Battle);
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

            // 渲染帧步长（锚点追随用——世界钟读数差；夹 [0, 0.1s]：首帧/时钟回拨不吃负步长，
            // 大间隔只按 0.1s 追一步，不瞬跳）。追踪的是**锚点位置**，升降浮等归轨道数学。
            float dt = Mathf.Clamp((float)(now - _lastTickNow), 0f, 0.1f);
            _lastTickNow = now;

            // ① 到期收口：聚合器裁决（淡出钟），实例归还 + 摘账——账实一致。
            _aggregator.CollectExpired(now, _expired);
            for (int i = 0; i < _expired.Count; i++) Release(_expired[i]);

            // ② 视觉推进：轨道数学 → 位形/透明度；跟随槽位视图（消失 → 冻结最后锚点）
            // 相机解析**每帧一次**并复用：billboard 朝向与 camScale 都以它为基准。
            UnityEngine.Camera cam = _cameraOf != null ? _cameraOf() : UnityEngine.Camera.main;

            _tickKeys.Clear();
            foreach (var key in _active.Keys) _tickKeys.Add(key);   // 收集段：纯 Add，无外部调用——无重入窗口
            for (int i = 0; i < _tickKeys.Count; i++)
            {
                var key = _tickKeys[i];
                if (!_active.TryGetValue(key, out var a)) continue;               // 快照后被移除（到期/淘汰/重入）→ 容错跳过
                if (!_aggregator.TryGet(key.Item1, key.Item2, key.Item3, out var snap)) continue;

                _motion.Evaluate(
                    (float)(now - snap.SpawnedAt), (float)(now - snap.LastMergeAt),
                    snap.Crit, snap.Seed,
                    out float rise, out float push, out float alpha, out float shake, out float pop);

                // 锚点追随（合并时目标=最新受击点）：恒速靠近——数字平滑滑向最新命中处（不瞬跳）。
                // 起条目时 Anchor==AnchorTarget，此步为空转；向量 != 用近似比较，到点即停。
                if (dt > 0f && a.Anchor != a.AnchorTarget)
                    a.Anchor = Vector3.MoveTowards(a.Anchor, a.AnchorTarget, _motion.AnchorFollowSpeed * dt);

                // 受击点锚位置（不随实体移动；升浮/推挤/抖动归轨道数学叠在锚上）。
                Vector3 pos = new Vector3(
                    a.Anchor.x + push + shake,
                    a.Anchor.y + _motion.HoverHeight + rise,
                    a.Anchor.z);

                float camScale = 1f;
                if (cam != null)
                {
                    float d = Vector3.Distance(cam.transform.position, pos);
                    camScale = Mathf.Clamp(d / CameraReferenceDistance, CameraScaleMin, CameraScaleMax);
                }

                a.Tmp.transform.position = pos;

                // **billboard：每帧把飘字转向渲染相机**（此前**从未写过 rotation**——底件 `fx_damage_number.prefab` 只有 Transform+MeshRenderer+TextMeshPro 三个组件、 无 TMP_Billboard。）。
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - pos;
                    if (toCam.sqrMagnitude > 1e-6f)
                        a.Tmp.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
                }

                float scale = pop * a.ScaleBoost * camScale;
                a.Tmp.transform.localScale = new Vector3(scale, scale, scale);
                a.Tmp.fontSize = a.FontSize;

                // 颜色（批C：顶→底顶点渐变——**权威面**）。
                Color top = new Color(a.TopR, a.TopG, a.TopB, alpha);
                Color bottom = new Color(a.BottomR, a.BottomG, a.BottomB, alpha);
                a.Tmp.colorGradient = new VertexGradient(top, top, bottom, bottom);
                a.Tmp.color = top;
                _active[key] = a;                                        // 键经 TryGetValue 刚确认在册——纯值更新
            }
        }

        /// <summary>释放条目：实例归还池（翻转激活态）＋聚合器摘账。</summary>
        private void Release((long TargetId, HitLocalRole Role, bool Crit) key)
        {
            if (!_active.TryGetValue(key, out var a)) return;
            _active.Remove(key);
            _aggregator.Remove(key.TargetId, key.Role, key.Crit);
            if (a.Tmp != null) _pool.Release(a.Tmp.gameObject);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var keys = new List<(long, HitLocalRole, bool)>(_active.Keys);
            for (int i = 0; i < keys.Count; i++) Release(keys[i]);
        }
    }
}
