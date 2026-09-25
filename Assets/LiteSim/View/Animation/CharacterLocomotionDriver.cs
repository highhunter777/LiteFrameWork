using System;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 角色移动动画驱动器（《动画模块专项设计》§7 更新次序中"Driver 解析参数与目标进度 →
    /// 提交播放/取消"的首个消费者）：按视图实例的实际移动速度把移动语义
    /// （<see cref="CharacterAnimationIds"/>）提交到每实体自己的
    /// <see cref="CharacterAnimationPlayer"/>（经 Profile 解析 → <see cref="AnimatorAnimationBackend"/>）。
    ///
    /// **速度来源**：视图 Transform 的帧间位移——本地（预测+和解衰减）与远端（快照插值）
    /// 同一来源，不读 Sim 内部（本类是 View 层消费者，只读视图）；插值/衰减的速度天然平滑。
    /// **时钟**：跟随视图同一时间域（真实帧间隔——与 SimView 插值一致；世界暂停语义与视图移动
    /// 一并归 G2 表现收口，本类不单独缩放）。
    ///
    /// **灰盒降级**：无 Animator 的视图（灰盒胶囊/缺资源克隆）不建播放器——表现为无动画，
    /// 与缺角色资源的既有降级姿势一致。视图回收/复用时旧播放器随视图消失被 Dispose，
    /// 新占用者重建（Owner 代次语义由重建保证）。
    /// </summary>
    public sealed class CharacterLocomotionDriver : IDisposable
    {
        /// <summary>低于该速度（m/s）视为静止。</summary>
        public const float IdleBelowMps = 0.5f;

        /// <summary>高于该速度（m/s）视为奔跑（CombatConfig.MoveSpeed 默认 5）。</summary>
        public const float RunAboveMps = 2.5f;

        private readonly SimView _view;
        private readonly AnimationProfile _profile;
        private readonly SlotAnim[] _slots = new SlotAnim[SimConfig.MaxEntities];
        private bool _disposed;

        public CharacterLocomotionDriver(SimView view, AnimationProfile profile = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _profile = profile ?? CombatGirlsAnimationProfile.Build();
        }

        /// <summary>当前有动画播放器的实体视图数（诊断/测试）。</summary>
        public int AnimatedViews
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++) if (_slots[i]?.Player != null) n++;
                return n;
            }
        }

        /// <summary>查询槽位当前动画语义（诊断/HUD/测试用；无动画视图返回 false）。</summary>
        public bool TryGetCurrent(int slotIndex, out AnimationId id)
        {
            id = default;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            var s = _slots[slotIndex];
            if (s?.Player == null || !s.Current.IsValid) return false;
            id = s.Current;
            return true;
        }

        /// <summary>
        /// 每渲染帧推进（SimView 之后调用——视图位置先更新，本类再解析目标姿态）：
        /// 视图增删检查 → 速度分档 → 语义变化才提交 → 播放器采样推进。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_disposed) return;
            float dt = realDelta > 0f ? realDelta : 0f;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_view.TryGetView(i, out var go))
                {
                    ReleaseSlot(i);                       // 视图已回收（死亡/离场）：播放器随视图消失收口
                    continue;
                }

                var s = _slots[i] ?? (_slots[i] = new SlotAnim());
                if (s.Player == null)
                {
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator == null || animator.runtimeAnimatorController == null)
                    {
                        // 灰盒视图：无动画面——只记位置（保持速度判断的帧间基准），不建播放器
                        RememberPosition(s, go);
                        continue;
                    }

                    s.Player = new CharacterAnimationPlayer(new AnimatorAnimationBackend(animator), _profile);
                    s.Current = CharacterAnimationIds.Idle;
                    s.Player.Play(new AnimationRequest(s.Current, AnimationChannel.Locomotion));   // 初建落 Idle（不开局 T-pose）
                }

                if (s.HasPos && dt > 0f)
                {
                    var target = ResolveTarget(s, go.transform.position, dt);
                    if (!target.Equals(s.Current))
                    {
                        var result = s.Player.Play(new AnimationRequest(target, AnimationChannel.Locomotion));
                        if (result.Accepted) s.Current = target;      // 拒绝不推进状态（下次重试，不丢帧）
                    }
                }

                RememberPosition(s, go);
                s.Player?.Tick(dt);                                    // 唯一驱动入口（§7）
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _slots.Length; i++) ReleaseSlot(i);
        }

        // ---- 内部 ----

        private AnimationId ResolveTarget(SlotAnim s, Vector3 pos, float dt)
        {
            Vector3 d = pos - s.LastPos;
            float speed = Mathf.Sqrt(d.x * d.x + d.z * d.z) / dt;      // 水平速度（m/s）
            if (speed < IdleBelowMps) return CharacterAnimationIds.Idle;
            if (speed < RunAboveMps) return CharacterAnimationIds.Walk;
            return CharacterAnimationIds.Run;
        }

        private static void RememberPosition(SlotAnim s, GameObject go)
        {
            s.LastPos = go.transform.position;
            s.HasPos = true;
        }

        private void ReleaseSlot(int i)
        {
            var s = _slots[i];
            if (s == null) return;
            s.Player?.Dispose();                         // 销毁序：代次失效 → 终态 → 释放后端（Graph.Destroy）
            s.Player = null;
            s.Current = default;
            s.HasPos = false;
            _slots[i] = null;
        }

        private sealed class SlotAnim
        {
            public CharacterAnimationPlayer Player;
            public AnimationId Current;
            public Vector3 LastPos;
            public bool HasPos;
        }
    }
}
