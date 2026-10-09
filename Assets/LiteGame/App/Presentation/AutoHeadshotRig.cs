using LiteClient;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
    /// <summary>
    /// **爆头链路自动验证件**（测试模式诊断，release 整段剥离；GM 面板现场开关）。
    ///
    /// **做什么**：把设备源换成自动瞄准源——每帧锁定<b>最近的非自身活体</b>，把 <c>AimPoint</c> 压到它
    /// <b>头部带中心</b>（[HeadHitLine, HitscanHeight] 的中点），并<b>持续按住开火</b>
    /// （限速由权威侧的开火驻留窗自然处理，<b>不</b>绕过它）。于是每次开火都应判为爆头——用来在
    /// <b>真实对局</b>里端到端验证「采集侧上报 AimPoint → 协议 → InputGate 校验 → 回溯 →
    /// 爆头判定 → Crit 事件 → 飘字」整条链。
    ///
    /// **为什么需要**：L1 只覆盖 Sim 与协议层；采集侧上报、InputGate 距离闸、回溯补判这三段
    /// 只有真跑起来才验得到。长时间不出现爆头即说明某一环没把 AimPoint 送到位。
    ///
    /// **不改任何判定**：只替换<b>输入源</b>（<see cref="IIntentSource"/>），等价于"玩家把准心压在头上"。
    /// 无可选目标时退化为空意图，不会乱打。
    /// </summary>
    public sealed class AutoHeadshotRig : IHitFeedbackConsumer
    {
        /// <summary>目标选择与瞄准点解算的输入源（<b>替换</b>真实键鼠源，不是叠加）。</summary>
        private sealed class AutoAimHeadshotSource : IIntentSource
        {
            private readonly SimWorldState _world;
            private readonly long _selfId;

            public string Name => "auto-headshot";

            /// <summary>最近一帧是否锁定到目标（诊断）。</summary>
            public bool HasTarget { get; private set; }

            /// <summary>当前目标实体 Id（无目标 = 0）。</summary>
            public long TargetId { get; private set; }

            /// <summary>最近一帧本地玩家位置（诊断读数：与目标距离过近时能一眼看出）。</summary>
            public SimVector3 LocalPos { get; private set; }

            public AutoAimHeadshotSource(SimWorldState world, long selfId)
            {
                _world = world;
                _selfId = selfId;
            }

            public IntentSample Sample(in SimVector3 localPos)
            {
                var frame = new SimInputFrame { EntityId = _selfId };
                HasTarget = false;
                TargetId = 0;
                LocalPos = localPos;

                _world.TryResolve(_selfId, out int selfSlot);
                int best = -1;
                float bestD2 = float.MaxValue;
                for (int i = 0; i < SimConfig.MaxEntities; i++)
                {
                    if ((_world.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                    if (i == selfSlot) continue;                              // 不打自己
                    ref EntitySlot e = ref _world.Entities[i];
                    if (e.Hp <= 0) continue;                                  // 尸体非目标
                    float dx = e.Pos.X - localPos.X;
                    float dz = e.Pos.Z - localPos.Z;
                    float d2 = dx * dx + dz * dz;
                    // **近身目标跳过**：贴到几乎同格时，枪口→头顶的方向会退化成"正上方"，
                    // 射线朝天打空（实测 Aim=(0,1,0)、零命中）。玩家贴脸时应选远处目标；
                    // 无合格目标时宁可不打，也不朝天放空枪。
                    if (d2 < MinTargetRange * MinTargetRange) continue;
                    if (d2 < bestD2) { bestD2 = d2; best = i; }
                }
                if (best < 0) return new IntentSample(frame);                 // 无目标 ⇒ 空意图

                ref EntitySlot t = ref _world.Entities[best];
                TargetId = t.Id;

                // 瞄准点 = 目标正上方**头部带中心**（带内正中 ⇒ 不贴边界，容错最大）。
                // **AimPoint 单口径**（《固定斜视角射击方案专项设计》§3）：朝向（Yaw）与弹道方向
                // 都由服务端/预测两端自本点派生——本件只产点，不再构造方向（方向字段已退役）。
                float headY = CombatConfig.HeadHitLine
                    + (CombatConfig.HitscanHeight - CombatConfig.HeadHitLine) * 0.5f;
                float px = t.Pos.X;
                float py = t.Pos.Y + headY;
                float pz = t.Pos.Z;

                // 与本体/枪口重合时点退化（无方向可用）——让出本帧（不打）
                float ax = px - localPos.X;
                float ay = py - (localPos.Y + CombatConfig.MuzzleOffsetHeight);
                float az = pz - localPos.Z;
                if (ax * ax + ay * ay + az * az <= 1e-6f) return new IntentSample(frame);

                // **AimPoint = 爆头判定的直接输入**（服务端从枪口指向它求交，并按它的 Y 判爆头）
                frame.AimPointX = px;
                frame.AimPointY = py;
                frame.AimPointZ = pz;

                // 持续开火：限速/驻留窗由权威侧判定（InputSystem 读 ButtonFire 置窗），本件不绕过
                frame.Buttons = SimInputFrame.ButtonFire;

                HasTarget = true;
                return new IntentSample(frame);
            }
        }

private readonly AutoAimHeadshotSource _source;
            /// <summary>目标最小水平距离（m）：**近于此距离的目标直接跳过**。</summary>
            private const float MinTargetRange = 2.5f;
            private IInputService _input;
        private IIntentSource _original;
        private int _critCount;
        private int _hitCount;
        private float _nextReport;

        /// <summary>是否正在驱动（已替换输入源）。</summary>
        public bool Active { get; private set; }

        /// <summary>累计爆头数（诊断）。</summary>
        public int CritCount => _critCount;

        public AutoHeadshotRig(SimWorldState world, long selfEntityId)
        {
            _source = new AutoAimHeadshotSource(world, selfEntityId);
        }

        /// <summary>启用（替换设备源；重复调用幂等）。</summary>
        public void Enable(IInputService input)
        {
            if (input == null || Active) return;
            _input = input;
            _original = input.Source;
            input.SetSource(_source);                     // 换源（键鼠源原样留存在 _original）
            Active = true;
            _nextReport = Time.unscaledTime;
            Debug.Log("[AutoHeadshot] 已启用：自动锁定最近敌人 + 瞄准头部带中心 + 持续开火（GM 面板关闭）");
        }

        /// <summary>关闭（还原原设备源；幂等）。</summary>
        public void Disable()
        {
            if (!Active) return;
            // 还原键鼠源（可能已随对局易主为 null —— 此时只落回"无源"形态，不越权改流程）
            if (_input != null && _input.Source == _source) _input.SetSource(_original);
            Active = false;
            Debug.Log($"[AutoHeadshot] 已关闭（累计爆头 {_critCount} 次 / 命中 {_hitCount} 次）");
        }

        /// <summary>每渲染帧：节流播报锁定状态（1s 一次，避免刷屏）。
        /// **带输入层读数**——爆头链路的断点只可能在这几处，逐项打出来比猜快：
        /// 被上下文门拦下 / Buttons 没带 Fire 位 / AimPoint 没写上。</summary>
        public void Tick()
        {
            if (!Active) return;
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 1f;

            if (!_source.HasTarget)
            {
                Debug.Log("[AutoHeadshot] 场上无可选目标（等补位 bot）");
                return;
            }

            SimInputFrame p = _input != null ? _input.Pending : default;
            Debug.Log($"[AutoHeadshot] 锁定中｜爆头 {_critCount}｜命中 {_hitCount}"
                + $"｜Buttons={p.Buttons}(Fire={(p.Buttons & SimInputFrame.ButtonFire) != 0u})"
                + $"｜拦={_input != null && _input.IsBlocked}({(_input != null ? _input.BlockedByName : "-")})"
                + $"｜本地={_source.LocalPos.X:0.0},{_source.LocalPos.Z:0.0}"
                + $"｜目标={_source.TargetId}"
                + $"｜AimPoint=({p.AimPointX:0.0},{p.AimPointY:0.00},{p.AimPointZ:0.0})");
        }

        /// <summary>IHitFeedbackConsumer：统计爆头/命中（只读，不改表现）。</summary>
        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (!Active) return;
            if (ctx.Kind == FrameEventKind.Crit) _critCount++;
            else if (ctx.Kind == FrameEventKind.Hit) _hitCount++;
        }
    }
#endif
}