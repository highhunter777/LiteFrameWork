using System;
using System.Collections.Generic;
using LiteSim;

namespace RoomServer.Runtime.Diagnostics
{
    /// <summary>
    /// 房间命令日志录制器（纯内存，R11 安全——无 IO/proto/LiteNet/系统时钟）。
    ///
    /// 容量有界：命令/步进/规则事件各自封顶；超限置 <see cref="Truncated"/> 并停止追加，
    /// 绝不抛进权威循环。录制器由宿主按配置注入；null = 零开销关闭。
    ///
    /// 世界存档环（容量 &gt; 0 时启用）：RecordStep 时刻深拷一份权威世界（<see cref="SimWorldState.CopyTo"/>
    /// 逐数组 Array.Copy，环满覆盖最旧帧）——重放分歧帧据此产出字段级差异。内存代价 = 容量 × 单世界大小，
    /// 由 diagnostics.world_ring_frames 显式给。
    ///
    /// 即时快照（mismatch flush）：任意线程 <see cref="RequestSnapshot"/> 置位；房间线程在
    /// <see cref="PollSnapshot"/> 里消费——事件列表与世界环深拷成独立记录后回调，回调在房间线程上
    /// 执行且必须无 IO（宿主侧只入队，写盘归后台）。封印（<see cref="Seal"/>）与快照互不干扰：
    /// 快照后录制继续。
    /// </summary>
    public sealed class RoomRunRecorder
    {
        public const int DefaultMaxCommands = 400_000;
        public const int DefaultMaxSteps = 100_000;
        private const int MaxTestRuleEvents = 1_024;

        private readonly string _roomId;
        private readonly string _matchId;
        private readonly int _expectedPlayers;
        private readonly int _maxCommands;
        private readonly int _maxSteps;
        private readonly List<RoomRunEvent> _events = new List<RoomRunEvent>();
        private readonly SimWorldState[] _worldRing;
        private readonly int[] _worldRingFrames;
        private readonly Action<RoomRunRecord> _onSnapshot;
        private int _snapshotRequested;

        private ulong _seed;
        private int _commandCount;
        private int _stepCount;
        private int _testRuleEventCount;
        private int _lastStepFrame = -1;
        private bool _truncated;
        private bool _sealed;
        private RoomRunRecord _sealedRecord;

        public RoomRunRecorder(string roomId, string matchId, int expectedPlayers,
            int maxCommands = DefaultMaxCommands, int maxSteps = DefaultMaxSteps,
            int worldRingCapacity = 0, Action<RoomRunRecord> onSnapshot = null)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("roomId 必填", nameof(roomId));
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("matchId 必填", nameof(matchId));
            if (expectedPlayers <= 0) throw new ArgumentOutOfRangeException(nameof(expectedPlayers));
            if (maxCommands <= 0) throw new ArgumentOutOfRangeException(nameof(maxCommands));
            if (maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));
            if (worldRingCapacity < 0) throw new ArgumentOutOfRangeException(nameof(worldRingCapacity));

            _roomId = roomId;
            _matchId = matchId;
            _expectedPlayers = expectedPlayers;
            _maxCommands = maxCommands;
            _maxSteps = maxSteps;
            _onSnapshot = onSnapshot;
            if (worldRingCapacity > 0)
            {
                _worldRing = new SimWorldState[worldRingCapacity];
                _worldRingFrames = new int[worldRingCapacity];
                for (int i = 0; i < worldRingCapacity; i++)
                {
                    _worldRing[i] = new SimWorldState();   // 预分配：录制期零 new
                    _worldRingFrames[i] = -1;
                }
            }
        }

        public bool Truncated => _truncated;
        public int CommandCount => _commandCount;
        public int StepCount => _stepCount;
        public bool HasWorlds => _worldRing != null;

        public void RecordSeed(ulong seed)
        {
            if (!CanRecord()) return;
            _seed = seed;
        }

        public void RecordCommand(in RoomCommand cmd)
        {
            if (!CanRecord()) return;
            if (_commandCount >= _maxCommands)
            {
                _truncated = true;
                return;
            }

            RoomRunCommand recorded = RoomRunCommand.From(cmd);
            if (cmd.Kind == RoomCommandKind.ClientInput
                && recorded.InputFrames.Length != Math.Max(cmd.Input.Count, 0))
            {
                // 声明的冗余帧数与可拷贝数组不一致：本记录已不能无损复现，显式截断。
                _truncated = true;
            }

            _events.Add(RoomRunEvent.ForCommand(_events.Count, recorded));
            _commandCount++;
        }

        public void RecordStep(int frame, uint checksum, SimWorldState world = null)
        {
            if (!CanRecord()) return;
            if (_stepCount >= _maxSteps)
            {
                _truncated = true;
                return;
            }
            if (frame <= _lastStepFrame) return;   // 同帧重复捕获不重复记账

            _events.Add(RoomRunEvent.ForStep(_events.Count, new RoomRunStep(frame, checksum)));
            _stepCount++;
            _lastStepFrame = frame;

            // 世界存档：录制点与 checksum 同时刻（StepFrame 末尾的权威世界）
            if (_worldRing != null && world != null)
            {
                int slot = frame % _worldRing.Length;
                world.CopyTo(_worldRing[slot]);
                _worldRingFrames[slot] = frame;
            }
        }

        public void RecordTestRules(int frame, MatchTestRules rules)
        {
            if (!CanRecord()) return;
            if (_testRuleEventCount >= MaxTestRuleEvents)
            {
                _truncated = true;
                return;
            }
            _events.Add(RoomRunEvent.ForTestRules(_events.Count, RoomRunTestRules.From(frame, rules)));
            _testRuleEventCount++;
        }

        /// <summary>封印并返回记录；重复调用返回同一份。封印后录制调用全部忽略（房间静默后取走）。
        /// 世界环所有权随封印移交（封印后不再覆写，零拷贝）。</summary>
        public RoomRunRecord Seal()
        {
            if (_sealed) return _sealedRecord;
            _sealed = true;
            _sealedRecord = new RoomRunRecord(RoomRunRecord.CurrentFormatVersion,
                _roomId, _matchId, _expectedPlayers, _seed, _truncated, _events.ToArray(),
                _worldRing, _worldRingFrames);
            return _sealedRecord;
        }

        /// <summary>请求即时快照（任意线程可调；房间线程在 PollSnapshot 消费）。无回调时为无操作。</summary>
        public void RequestSnapshot()
        {
            if (_onSnapshot == null) return;
            System.Threading.Interlocked.Exchange(ref _snapshotRequested, 1);
        }

        /// <summary>
        /// 房间线程消费快照请求（StepFrame 末尾调用）：事件与世界环深拷成独立记录后回调。
        /// 回调在房间线程上执行——只允许入队等轻操作，IO 归宿主后台。异常吞掉并置截断标记：
        /// 诊断设施不得反噬权威循环。
        /// </summary>
        public void PollSnapshot()
        {
            if (_onSnapshot == null) return;
            if (System.Threading.Interlocked.Exchange(ref _snapshotRequested, 0) == 0) return;

            try
            {
                RoomRunRecord snapshot = BuildSnapshot();
                _onSnapshot(snapshot);
            }
            catch
            {
                // 快照失败只损失即时证据，终态记录不受影响
                _truncated = true;
            }
        }

        /// <summary>当前进度的独立拷贝（录制继续，互不影响终态封印）。</summary>
        public RoomRunRecord BuildSnapshot()
        {
            SimWorldState[] ring = null;
            int[] ringFrames = null;
            if (_worldRing != null)
            {
                ring = new SimWorldState[_worldRing.Length];
                ringFrames = new int[_worldRingFrames.Length];
                for (int i = 0; i < _worldRing.Length; i++)
                {
                    ringFrames[i] = _worldRingFrames[i];
                    if (_worldRingFrames[i] < 0) { ring[i] = new SimWorldState(); continue; }
                    ring[i] = new SimWorldState();
                    _worldRing[i].CopyTo(ring[i]);
                }
            }
            return new RoomRunRecord(RoomRunRecord.CurrentFormatVersion,
                _roomId, _matchId, _expectedPlayers, _seed, _truncated, _events.ToArray(),
                ring, ringFrames);
        }

        private bool CanRecord() => !_sealed && !_truncated;
    }
}
