using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 时间轴执行器。时间轴由注入时钟的 ScaledDelta 推进；运行列表用 HashSet 做去重判断，
    /// 列表只负责稳定迭代，停止/完成的时间轴在 Tick 末尾惰性清理。
    /// </summary>
    public sealed class GameTimelineRunner : ITimelineRunner
    {
        private readonly IGameClock _clock;
        private readonly List<GameTimeline> _running = new List<GameTimeline>(16);
        private readonly HashSet<GameTimeline> _runningSet = new HashSet<GameTimeline>();
        private readonly HashSet<GameTimeline> _listedSet = new HashSet<GameTimeline>();
        private int _maxStepsPerTick = 100;

        public GameTimelineRunner(IGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>单次 Tick 允许触发的动作总数，防止超大步长造成卡帧。</summary>
        public int MaxStepsPerTick
        {
            get => _maxStepsPerTick;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "MaxStepsPerTick 必须大于 0");
                _maxStepsPerTick = value;
            }
        }

        public ITimeline CreateTimeline() => new GameTimeline(this);

        public void Tick(float realDelta)
        {
            float dt = _clock.ScaledDelta;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f)
                throw new ArgumentOutOfRangeException("ScaledDelta", "时钟 ScaledDelta 必须是非负有限数值");

            int remainingSteps = MaxStepsPerTick;
            int countAtStart = _running.Count;
            for (int i = 0; i < countAtStart; i++)
            {
                GameTimeline timeline = _running[i];
                if (!_runningSet.Contains(timeline)) continue;
                timeline.Advance(dt, ref remainingSteps);
            }

            CleanupStoppedTimelines();
        }

        public string StatsName => "TimelineRunner";

        public void Snapshot(Dictionary<string, string> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            into["运行中"] = _runningSet.Count.ToString();
            into["本帧上限"] = MaxStepsPerTick.ToString();
        }

        internal void Track(GameTimeline timeline)
        {
            if (!_runningSet.Add(timeline)) return;
            if (_listedSet.Add(timeline)) _running.Add(timeline);
        }

        internal void Untrack(GameTimeline timeline)
        {
            _runningSet.Remove(timeline);
        }

        private void CleanupStoppedTimelines()
        {
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                GameTimeline timeline = _running[i];
                if (_runningSet.Contains(timeline)) continue;
                _running.RemoveAt(i);
                _listedSet.Remove(timeline);
            }
        }
    }

    /// <summary>
    /// 轻量时间轴：动作点按时间排序执行，支持有限/无限循环以及不回放动作的 Seek。
    /// </summary>
    public sealed class GameTimeline : ITimeline
    {
        private sealed class Step
        {
            public float Seconds;
            public Action Action;
            public int Order;
        }

        private const float TimeEpsilon = 1e-5f;

        private readonly GameTimelineRunner _runner;
        private readonly List<Step> _steps = new List<Step>(8);
        private int _nextOrder;
        private int _loopCount = 1;
        private float _initialSeek;
        private float _duration;
        private float _position;
        private long _loopIndex;
        private int _nextStepIndex;
        private bool _atEnd;
        private bool _hasStarted;
        private bool _running;
        private bool _finished;

        internal GameTimeline(GameTimelineRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public ITimeline At(float seconds, Action action)
        {
            if (_hasStarted) throw new InvalidOperationException("时间轴启动后不能追加动作点");
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds), "动作点时间必须是非负有限数值");

            _steps.Add(new Step { Seconds = seconds, Action = action, Order = _nextOrder++ });
            return this;
        }

        public ITimeline Loop(int loopCount)
        {
            if (_hasStarted) throw new InvalidOperationException("时间轴启动后不能修改循环次数");
            if (loopCount == 0 || loopCount < -1)
                throw new ArgumentOutOfRangeException(nameof(loopCount), "循环次数必须为正数或 -1（无限循环）");
            _loopCount = loopCount;
            return this;
        }

        public ITimeline Seek(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds), "播放位置必须是非负有限数值");

            if (!_hasStarted)
            {
                _initialSeek = seconds;
                return this;
            }

            // 向后跳只改变播放头，不把 nextStepIndex 倒退，避免已经执行过的动作再次触发。
            if (seconds >= _position)
            {
                _position = seconds;
                SkipPendingThrough(seconds);
            }
            else
            {
                _position = seconds;
            }
            return this;
        }

        public void Start()
        {
            if (_steps.Count == 0) throw new InvalidOperationException("空时间轴不能启动");
            if (_running) throw new InvalidOperationException("时间轴已经启动");
            if (_finished) throw new InvalidOperationException("已完成的时间轴不能再次启动");

            if (!_hasStarted)
            {
                _steps.Sort(CompareSteps);
                _duration = _steps[_steps.Count - 1].Seconds;
                _position = _initialSeek;
                _loopIndex = 0;
                _nextStepIndex = 0;
                _atEnd = false;
                _hasStarted = true;
                SkipPendingThrough(_position);
            }

            _running = true;
            _runner.Track(this);
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _runner.Untrack(this);
        }

        public bool Finished => _finished;

        internal void Advance(float dt, ref int remainingSteps)
        {
            if (!_running || dt <= 0f) return;

            float target = _position + dt;
            if (float.IsInfinity(target) || float.IsNaN(target))
                throw new ArgumentOutOfRangeException(nameof(dt), "时间轴目标位置必须是有限数值");

            // 本帧预算已耗尽时仍推进播放头；未触发的动作保留在游标处，下一帧继续补发。
            if (remainingSteps <= 0)
            {
                _position = target;
                return;
            }

            while (_running)
            {
                if (_atEnd)
                {
                    _position = target;
                    FinishNaturally();
                    return;
                }

                float occurrence = NextOccurrenceTime();
                if (occurrence > target + TimeEpsilon)
                {
                    _position = target;
                    return;
                }

                _position = occurrence;
                Step step = _steps[_nextStepIndex];
                MoveToNextStep();
                remainingSteps--;
                SafeCall.Invoke(step.Action, "Timeline", occurrence);

                if (!_running) return; // 回调中 Stop：立即中断本次推进
                if (_position > target) target = _position; // 回调中 Seek 到未来位置时不被本帧目标覆盖
                if (_atEnd)
                {
                    FinishNaturally();
                    return;
                }
                if (remainingSteps <= 0)
                {
                    _position = target;
                    return;
                }
            }
        }

        private void SkipPendingThrough(float seconds)
        {
            if (_atEnd) return;
            if (NextOccurrenceTime() >= seconds - TimeEpsilon) return;

            if (_duration <= TimeEpsilon)
            {
                // 零时长时间轴的所有动作都发生在 0；Seek 到 0 仍保留这些动作，
                // Seek 到更后位置则表示它们已经被跳过。
                _atEnd = true;
                return;
            }

            double total = _loopCount == -1 ? double.PositiveInfinity : _duration * (double)_loopCount;
            if (seconds > total + TimeEpsilon)
            {
                _atEnd = true;
                return;
            }

            double quotient = seconds / (double)_duration;
            long candidateLoop = quotient >= long.MaxValue ? long.MaxValue : (long)Math.Floor(quotient);
            double cycleStart = candidateLoop * (double)_duration;
            bool onBoundary = candidateLoop > 0 && Math.Abs(seconds - cycleStart) <= TimeEpsilon;

            if (onBoundary)
            {
                // 保留边界时刻上一轮的最后动作；执行它后 MoveToNextStep 会进入下一轮。
                _loopIndex = candidateLoop - 1;
                _nextStepIndex = _steps.Count - 1;
                return;
            }

            if (_loopCount != -1 && candidateLoop >= _loopCount)
            {
                _atEnd = true;
                return;
            }

            _loopIndex = candidateLoop;
            float localSeconds = (float)(seconds - cycleStart);
            int next = 0;
            while (next < _steps.Count && _steps[next].Seconds < localSeconds - TimeEpsilon)
                next++;

            if (next == _steps.Count)
            {
                if (_loopCount != -1 && candidateLoop + 1 >= _loopCount)
                {
                    _atEnd = true;
                    return;
                }
                _loopIndex = candidateLoop + 1;
                next = 0;
            }
            _nextStepIndex = next;
        }

        private float NextOccurrenceTime()
        {
            return (float)(_loopIndex * (double)_duration) + _steps[_nextStepIndex].Seconds;
        }

        private void MoveToNextStep()
        {
            if (_nextStepIndex + 1 < _steps.Count)
            {
                _nextStepIndex++;
                return;
            }

            if (_loopCount != -1 && _loopIndex >= _loopCount - 1)
            {
                _atEnd = true;
                return;
            }

            _loopIndex++;
            _nextStepIndex = 0;
        }

        private void FinishNaturally()
        {
            _running = false;
            _finished = true;
            _runner.Untrack(this);
        }

        private static int CompareSteps(Step left, Step right)
        {
            int time = left.Seconds.CompareTo(right.Seconds);
            return time != 0 ? time : left.Order.CompareTo(right.Order);
        }
    }
}
