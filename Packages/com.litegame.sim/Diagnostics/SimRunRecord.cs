using System;
using System.Collections.Generic;

namespace LiteSim
{
    /// <summary>
    /// 一局确定性跑批的**完整可复现记录**：种子 + 初始世界 + 逐帧输入 + 逐帧 checksum。
    /// </summary>
    public sealed class SimRunRecord
    {
        /// <summary>每帧的输入槽（全体玩家，playerId 升序规范形——Step 就地排序后的形态）。</summary>
        private readonly List<SimInputFrame[]> _frames = new List<SimInputFrame[]>();

        /// <summary>每帧执行后的**全量口径** checksum（逐步比对的分歧探针）。</summary>
        private readonly List<uint> _checksums = new List<uint>();

        /// <summary>记录标签（落盘/报告用；不影响复现）。</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>初始 RngState（复现起点；<see cref="SimWorldState.RngState"/> 的初值）。</summary>
        public ulong Seed { get; set; }

        /// <summary>玩家数（每帧输入槽数；构造后不变）。</summary>
        public int PlayerCount { get; private set; }

        /// <summary>已记录帧数（= checksum 条数）。</summary>
        public int FrameCount => _frames.Count;

        /// <summary>复现所需的初始世界（构造期深拷一份——调用方此后改动原世界不影响本记录）。</summary>
        public SimWorldState InitialState { get; private set; }

        /// <summary>复现所需的地图（与初始世界同源——两端地图不一致本身就是分歧源，必须一并记录）。</summary>
        public SimMapData Map { get; private set; }

        // ---- 可选：环形世界存档（诊断增强；见 BeginWithWorlds）----

        /// <summary>世界存档环（null = 不存世界，只存 checksum——默认形态）。</summary>
        private SimWorldState[] _worldRing;

        /// <summary>环内每槽对应的帧号（1 基；-1 = 空）。</summary>
        private int[] _worldRingFrames;

        /// <summary>环容量（= 可回溯的帧数窗口）。</summary>
        private int _worldRingCapacity;

        /// <summary>是否保存了世界存档（false = 字段级报告需调用方自备对比源）。</summary>
        public bool HasWorlds => _worldRing != null;

        /// <summary>世界存档环容量（0 = 未启用）。</summary>
        public int WorldRingCapacity => _worldRingCapacity;

        /// <summary>
        /// 开启记录：深拷初始世界与地图，锚定种子与玩家数。
        /// <paramref name="initialState"/> 的当前 <c>RngState</c> 即 <see cref="Seed"/>。
        /// </summary>
        public static SimRunRecord Begin(SimWorldState initialState, SimMapData map, int playerCount,
            string label = null)
        {
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (playerCount <= 0) throw new ArgumentOutOfRangeException(nameof(playerCount));

            var record = new SimRunRecord
            {
                Seed = initialState.RngState,
                PlayerCount = playerCount,
                Label = label ?? string.Empty,
                InitialState = new SimWorldState(),
                Map = new SimMapData(),
            };
            initialState.CopyTo(record.InitialState);
            map.CopyTo(record.Map);
            return record;
        }

        /// <summary>
        /// 开启记录，**并额外保存最近若干帧的世界存档**（环形）—— 用于让 <see cref="SimReplayRunner.ReplayAndCompare"/> 能**一键产出字段级报告** （否则只定位帧号。）。
        /// </summary>
        public static SimRunRecord BeginWithWorlds(SimWorldState initialState, SimMapData map, int playerCount,
            int worldRingCapacity, string label = null)
        {
            if (worldRingCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldRingCapacity), "世界存档环容量必须 > 0（不需要就用具 Begin）");

            var record = Begin(initialState, map, playerCount, label);
            record._worldRingCapacity = worldRingCapacity;
            record._worldRing = new SimWorldState[worldRingCapacity];
            record._worldRingFrames = new int[worldRingCapacity];
            for (int i = 0; i < worldRingCapacity; i++)
            {
                record._worldRing[i] = new SimWorldState();   // 预分配：运行期零 new（与本工程纪律一致）
                record._worldRingFrames[i] = -1;
            }
            return record;
        }

        /// <summary>
        /// 取某帧的世界存档（<paramref name="frame"/> = **1 基帧号**。）。
        /// </summary>
        public bool TryGetWorld(int frame, out SimWorldState world)
        {
            world = null;
            if (_worldRing == null || frame <= 0) return false;

            int slot = (frame - 1) % _worldRingCapacity;
            if (_worldRingFrames[slot] != frame) return false;
            world = _worldRing[slot];
            return true;
        }

        /// <summary>
        /// 记录一帧：**该帧执行后**的世界状态 + 本帧实际消费的输入。
        /// 调用时机必须是 Step 之后（checksum 是对"执行结果"的指纹）。
        /// </summary>
        public void Capture(SimWorldState state, SimInputFrame[] consumed)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (consumed == null) throw new ArgumentNullException(nameof(consumed));
            if (consumed.Length != PlayerCount)   // lint-allow R3（整型判等，非浮点精度比较）
                throw new ArgumentException(
                    $"输入槽数不符：期望 {PlayerCount}，实得 {consumed.Length}（记录期内玩家数不变）", nameof(consumed));

            var copy = new SimInputFrame[PlayerCount];
            Array.Copy(consumed, copy, PlayerCount);
            _frames.Add(copy);
            _checksums.Add(SimChecksum.ComputeChecksum(state));

            // 世界存档（启用时）：帧号 = 本次记录后的条数（1 基，与 FirstDivergenceFrame 同口径）
            if (_worldRing != null)
            {
                int frame = _frames.Count;
                int slot = (frame - 1) % _worldRingCapacity;
                state.CopyTo(_worldRing[slot]);
                _worldRingFrames[slot] = frame;
            }
        }

        /// <summary>取某帧输入（索引 = 记录序，0 基；越界 false）。返回内部数组（零拷贝，调用方不得改写）。</summary>
        public bool TryGetFrame(int index, out SimInputFrame[] inputs, out uint checksum)
        {
            inputs = null;
            checksum = 0u;
            if (index < 0 || index >= _frames.Count) return false;
            inputs = _frames[index];
            checksum = _checksums[index];
            return true;
        }

        /// <summary>取某帧记录的 checksum（越界返回 false）。</summary>
        public bool TryGetChecksum(int index, out uint checksum)
        {
            checksum = 0u;
            if (index < 0 || index >= _checksums.Count) return false;
            checksum = _checksums[index];
            return true;
        }

        /// <summary>全部逐帧 checksum（只读快照；报告用）。</summary>
        public IReadOnlyList<uint> Checksums => _checksums;
    }
}
