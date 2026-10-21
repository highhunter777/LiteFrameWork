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

        private ulong _seed;
        private int _commandCount;
        private int _stepCount;
        private int _testRuleEventCount;
        private int _lastStepFrame = -1;
        private bool _truncated;
        private bool _sealed;
        private RoomRunRecord _sealedRecord;

        public RoomRunRecorder(string roomId, string matchId, int expectedPlayers,
            int maxCommands = DefaultMaxCommands, int maxSteps = DefaultMaxSteps)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("roomId 必填", nameof(roomId));
            if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("matchId 必填", nameof(matchId));
            if (expectedPlayers <= 0) throw new ArgumentOutOfRangeException(nameof(expectedPlayers));
            if (maxCommands <= 0) throw new ArgumentOutOfRangeException(nameof(maxCommands));
            if (maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));

            _roomId = roomId;
            _matchId = matchId;
            _expectedPlayers = expectedPlayers;
            _maxCommands = maxCommands;
            _maxSteps = maxSteps;
        }

        public bool Truncated => _truncated;
        public int CommandCount => _commandCount;
        public int StepCount => _stepCount;

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

        public void RecordStep(int frame, uint checksum)
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

        /// <summary>封印并返回记录；重复调用返回同一份。封印后录制调用全部忽略（房间静默后取走）。</summary>
        public RoomRunRecord Seal()
        {
            if (_sealed) return _sealedRecord;
            _sealed = true;
            _sealedRecord = new RoomRunRecord(RoomRunRecord.CurrentFormatVersion,
                _roomId, _matchId, _expectedPlayers, _seed, _truncated, _events.ToArray());
            return _sealedRecord;
        }

        private bool CanRecord() => !_sealed && !_truncated;
    }
}
