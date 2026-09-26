using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 转场编排层（《UI扩展能力设计》§1.5，Layer 2）：通用状态机 + 待办队列（容量 1）+ 超时兜底 + 交互门 + 统计。
    ///
    /// **不注册 `ITickable`**——由 <see cref="UIService"/> 在自身 Tick 的**帧末**转发：既避免双驱动，
    /// 也让界面 `OnUpdate` 里发起的请求能在同一帧帧末被推进（§1.5.2"帧末 Advance 应用"）。
    ///
    /// 与既有 `_loading`/`_closing` 守卫**正交**：那两个按 formId 防"同一界面并发开/关"，
    /// 本层管"全局表现编排"（同一时刻只有一个事务）。
    ///
    /// 四条硬规则（§1.5.4）：① 同帧 last-wins（首个请求立即开始，后续走排队/丢弃，不重复 Request 给机器）
    /// ② 排队 1 个（目标 = 当前 Incoming 则忽略；再来的丢弃并计数）③ 交互门由壳在阶段钩子里统一管
    /// ④ 超时（默认 2s）强制收尾并广播 <c>Completed=false</c>。
    /// </summary>
    public sealed class UITransitionRunner : IModuleStats
    {
        /// <summary>超时兜底默认时长（§1.5.4 规则④）。</summary>
        public const float DefaultMaxDuration = 2f;

        private readonly StageMachine<TransitionId, TransitionReq> _machine;
        private readonly ITransitionStrategy _strategy;
        private readonly IReplaceTransition _replace;
        private readonly float _maxDuration;

        private TransitionContext _current;
        private TransitionContext _queued;
        private int _dropped;

        /// <summary>事务开始（Lua 侧 begin 事件的来源）。</summary>
        public event Action<TransitionContext> Began;

        /// <summary>事务结束（与 <see cref="Began"/> 成对；超时也发，`Completed=false`）。</summary>
        public event Action<TransitionOutcome> Finished;

        public UITransitionRunner(ITransitionStrategy strategy,
                                  IReplaceTransition replaceTransition = null,
                                  float maxDuration = DefaultMaxDuration,
                                  string name = "UITransition")
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _replace = replaceTransition;
            _maxDuration = maxDuration > 0f ? maxDuration : DefaultMaxDuration;

            _machine = new StageMachine<TransitionId, TransitionReq>(name,
                (TransitionId.Idle, new IdleTransitionStage()),
                (TransitionId.Out, new OutTransitionStage()),
                (TransitionId.In, new InTransitionStage()));
            _machine.Start(TransitionId.Idle);
        }

        // ---- 诊断读数（DevHUD / 验收）----

        /// <summary>当前转场阶段。</summary>
        public TransitionId Phase => _machine.Current;

        /// <summary>是否有事务在执行。</summary>
        public bool Busy => _current != null;

        /// <summary>该界面是否处于当前事务的输入锁内（U1-③：输入协调求解用——接受即锁）。</summary>
        public bool IsLocked(UIForm form)
            => _current != null && (ReferenceEquals(_current.Outgoing, form) || ReferenceEquals(_current.Incoming, form));

        /// <summary>待办队列长度（0 或 1）。</summary>
        public int QueueLength => _queued != null ? 1 : 0;

        /// <summary>因队列满被丢弃的请求数。</summary>
        public int DroppedCount => _dropped;

        /// <summary>累计转场迁移次数。</summary>
        public long TotalTransitions => _machine.TransitionCount;

        /// <summary>
        /// 发起一次转场。空闲 → 立即开始；忙 → 排队 1 个（再来的丢弃并计数）。
        /// 返回的 Task 在事务收尾时完成（超时/被丢/被忽略也会完成，`Completed=false`）。
        /// </summary>
        public UniTask<TransitionOutcome> PlayAsync(TransitionMode mode, UIForm outgoing, UIForm incoming)
        {
            var ctx = new TransitionContext
            {
                Mode = mode,
                Outgoing = outgoing,
                Incoming = incoming,
                MaxDuration = _maxDuration,
                Cts = new System.Threading.CancellationTokenSource(),   // U1-③：本事务取消源（超时/权威取消停策略工作）
            };
            ctx.Playback = new MotionPlayback(ctx.Cts);              // 终态出口与取消源同址（§6.3 + 动画专项 §10）
            ctx.Play = BuildPlay(ctx);

            if (_current == null)
            {
                Start(ctx);
                return ctx.Tcs.Task;
            }

            if (incoming != null && ReferenceEquals(incoming, _current.Incoming))
            {
                Log.Warning($"转场中重复请求同一界面[{incoming.Id}]——忽略", "UI");
                ctx.Skipped = true;
                ctx.Tcs.TrySetResult(OutcomeOf(ctx));
                return ctx.Tcs.Task;
            }

            if (_queued == null)
            {
                _queued = ctx;
                Log.Info($"转场进行中——请求已排队(mode={mode})", "UI");
            }
            else
            {
                _dropped++;
                Log.Warning($"转场队列已满——请求丢弃(mode={mode}，累计 {_dropped})", "UI");
                ctx.Skipped = true;
                ctx.Tcs.TrySetResult(OutcomeOf(ctx));
            }
            return ctx.Tcs.Task;
        }

        /// <summary>
        /// 帧末驱动（<see cref="UIService"/> 的 Tick 末尾调用）。
        /// 顺序：超时/完成判定 → 机器 Tick（推进迁移）→ 收尾 → 取队列。
        ///
        /// **调用方约定**：收尾会同步 `TrySetResult`，从而可能在本方法内同步恢复
        /// `ShowAsync/CloseAsync` 的续体（进而关闭/回收界面）。所以本方法必须位于其余帧逻辑之后——
        /// UIService 已保证（`RaiseUpdate` 循环结束后才转发），届时 `foreach (_forms.Values)` 已结束，重入安全。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_current != null && !_current.Finalized)
            {
                var c = _current;
                bool inTransition = _machine.Current != TransitionId.Idle;

                // 页面在事务进行中被回收/销毁（缓存淘汰、Destroy、低内存清理）：本条策略不再有
                // 可靠的回调源——本仓 DOTween 1.3.030 实测 KillOnDisable 与显式 Kill **都不触发 OnKill**
                // （动画专项 §2 登记的缺陷比记录更严重），靠超时兜底会让任务白悬最多 MaxDuration。
                // 故由壳主动取消：策略复位 → 记 Cancelled → 立即收尾。
                if (inTransition && !c.TimedOut && !c.Done && IsOwnerGone(c))
                {
                    c.OwnerGone = true;
                    Log.Warning($"转场期间界面已回收——取消策略工作并收尾(mode={c.Mode})", "UI");
                    try { c.Cts?.Cancel(); } catch (ObjectDisposedException) { }
                    _machine.Request(TransitionId.Idle);
                }
                else if (inTransition && !c.TimedOut && !c.Done && _machine.StageTime > c.MaxDuration)
                {
                    c.TimedOut = true;
                    Log.Error($"转场超时({c.MaxDuration:0.##}s)——取消策略工作并强制收尾(mode={c.Mode})", "UI");
                    try { c.Cts?.Cancel(); } catch (ObjectDisposedException) { }   // U1-③：先停 Tween/异步工作（§6.3），再收尾
                    _machine.Request(TransitionId.Idle);
                }
                else if (inTransition && !c.TimedOut && c.Done)
                {
                    _machine.Request(TransitionId.Idle);
                }
            }

            _machine.Tick(realDelta);

            if (_current != null && !_current.Finalized
                && _machine.Current == TransitionId.Idle && !_machine.HasPending)
            {
                Finalize(_current);
            }

            // 队列取出放帧末最后一步：保证"一帧最多一变"（§1.5.2）
            if (_current == null && _queued != null)
            {
                var next = _queued;
                _queued = null;
                Start(next);
            }
        }

        // ---- 内部 ----

        /// <summary>事务锁定的页面是否已离开"可播"生命周期（回收/销毁 = 表现宿主已不可靠）。
        /// 只认 <see cref="UIFormState.Recycled"/>/<see cref="UIFormState.Disposed"/>——
        /// Cover/Pause/Closing 仍会收尾回调，不作取消依据（避免把正常流程误判成丢失）。</summary>
        private static bool IsOwnerGone(TransitionContext c)
            => IsGone(c.Outgoing) || IsGone(c.Incoming);

        private static bool IsGone(UIForm form)
            => form != null && (form.State is UIFormState.Recycled or UIFormState.Disposed);

        private void Start(TransitionContext ctx)
        {
            _current = ctx;
            Began?.Invoke(ctx);
            _machine.Request(ctx.Mode == TransitionMode.Pop ? TransitionId.Out : TransitionId.In,
                             new TransitionReq(ctx));
        }

        private void Finalize(TransitionContext ctx)
        {
            ctx.Finalized = true;
            _current = null;                                // 先清当前事务：恢复按"无转场锁"的协调值计算（U1-③）
            ApplyComputedInput(ctx.Outgoing);
            ApplyComputedInput(ctx.Incoming);
            try { ctx.Cts?.Dispose(); } catch (ObjectDisposedException) { }
            ctx.Cts = null;

            var outcome = OutcomeOf(ctx);
            ctx.Tcs.TrySetResult(outcome);
            Finished?.Invoke(outcome);
        }

        /// <summary>按协调者计算值恢复输入（U1-③，§6.3：不无条件写回 true）——
        /// 生命周期未锁定的页面解锁；Paused/Closing/Recycled/Disposed 保持锁定（各自的锁定理由仍在）。
        /// blocksRaycasts 全程不动（打开的界面始终遮挡下层射线——职责分离，§6.2）。</summary>
        private static void ApplyComputedInput(UIForm form)
        {
            if (form == null || form.CanvasGroup == null) return;
            form.CanvasGroup.interactable =
                !(form.State is UIFormState.Paused or UIFormState.Closing or UIFormState.Recycled or UIFormState.Disposed);
        }

        /// <summary>
        /// 结果分类。**分工**：超时/丢弃两种"事务级"事实优先（它们由 Runner 判定）；
        /// 其余交给策略写下的**播放终态**（<see cref="MotionPlayback.Outcome"/>）——
        /// 这样"策略被取消但未超时"会如实报 <see cref="TransitionResultKind.Cancelled"/>，
        /// 而不是落到含糊的兜底值（动画专项 §10"播放终态与 UI 操作结果分层映射"）。
        /// 未实现带终态签名的旧策略：<c>Playback.Finished</c> 恒 false → 按完成处理（保持旧语义）。
        /// </summary>
        private static TransitionOutcome OutcomeOf(TransitionContext ctx)
        {
            TransitionResultKind kind;
            if (ctx.TimedOut)
            {
                kind = TransitionResultKind.TimedOut;
            }
            else if (ctx.Skipped)
            {
                kind = TransitionResultKind.Skipped;
            }
            else if (ctx.Failed)
            {
                kind = TransitionResultKind.Failed;              // StartPlay 捕获到策略异常（优先于播放终态）
            }
            else if (ctx.OwnerGone)
            {
                // 页面被回收：表现未播完但也不是超时——按取消分类（策略已复位并在 Playback 记 Cancelled）
                kind = TransitionResultKind.Cancelled;
            }
            else if (ctx.Playback != null && ctx.Playback.Finished)
            {
                kind = ctx.Playback.Outcome switch
                {
                    MotionOutcome.Cancelled => TransitionResultKind.Cancelled,
                    MotionOutcome.Failed => TransitionResultKind.Failed,
                    _ => TransitionResultKind.Completed,
                };
            }
            else
            {
                kind = ctx.Completed ? TransitionResultKind.Completed : TransitionResultKind.Cancelled;
            }

            return new TransitionOutcome
            {
                Mode = ctx.Mode,
                Outgoing = ctx.Outgoing,
                Incoming = ctx.Incoming,
                Kind = kind,
                // Completed 语义 = "表现正常播完"（§6.3）；超时/取消/异常/丢弃都不算
                Completed = kind == TransitionResultKind.Completed,
                TimedOut = ctx.TimedOut,
            };
        }

        private Func<UniTask> BuildPlay(TransitionContext c) => c.Mode switch
        {
            TransitionMode.Pop => () => _strategy.PlayClose(c.Outgoing, c.Playback),
            TransitionMode.Push => () => _strategy.PlayShow(c.Incoming, c.Playback),
            TransitionMode.Replace => () => PlayReplace(c),
            _ => () => UniTask.CompletedTask,
        };

        /// <summary>Replace 的"两组并发"：策略实现了 <see cref="IReplaceTransition"/> 走定制，否则壳合成。</summary>
        private UniTask PlayReplace(TransitionContext c)
            => _replace != null
                ? _replace.PlayReplace(c.Outgoing, c.Incoming, c.Playback)
                : UniTask.WhenAll(_strategy.PlayClose(c.Outgoing, c.Playback), _strategy.PlayShow(c.Incoming, c.Playback));

        public string StatsName => "UITransition";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["阶段"] = _machine.Current.ToString();
            into["阶段时长"] = _machine.StageTime.ToString("0.0");
            into["忙"] = Busy ? "是" : "否";
            into["队列"] = QueueLength.ToString();
            into["丢弃"] = _dropped.ToString();
            into["累计"] = _machine.TransitionCount.ToString();
        }
    }
}
