using System;
using System.Collections.Generic;

namespace LiteSim
{
    /// <summary>
    /// 一局确定性跑批的**完整可复现记录**：种子 + 初始世界 + 逐帧输入 + 逐帧 checksum。
    ///
    /// **为什么需要它**（本工程原有的调试空白）：<see cref="RollbackSim"/> 能在分歧时和解，
    /// 但和解只告诉你"某一帧的公共口径 checksum 不符"——**不告诉你为什么**。
    /// 此前排查 1-ulp 分歧只能靠断点跟 10k 步运算链（<see cref="SimMath.Sqrt"/> 注释里
    /// ".NET 2896875742 / Unity 3683559206" 那条实测即此路径的产物）。
    /// 本类把"跑一局"变成**可落盘、可离线复算、可逐步比对**的数据。
    ///
    /// **口径**：确定性输入 = 种子（<see cref="SimWorldState.RngState"/> 初值）+ 初始世界 + 每帧全体输入。
    /// 三者相同 ⇒ 逐帧 checksum 必须逐位相同（同运行时内）。故记录 checksum 序列即可定位首个分歧帧，
    /// 无需保存中间世界（体积 = O(帧数 × 玩家数)，非 O(帧数 × 世界大小)）。
    ///
    /// **不参与 Sim 判定**：本类是**诊断侧**只读结构——Sim 不认识它（Core 零依赖纪律），
    /// 由驱动/测试/沙盒在帧边界调用 <see cref="Capture"/> 落记录。
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
        /// 开启记录，**并额外保存最近若干帧的世界存档**（环形）——
        /// 用于让 <see cref="SimReplayRunner.ReplayAndCompare"/> 能**一键产出字段级报告**
        /// （否则只定位帧号，字段差异需调用方自备记录方世界）。
        ///
        /// **体积代价**：每帧一份完整 <see cref="SimWorldState"/> 深拷
        /// （约 <c>MaxEntities × (EntitySlot + 各运行态行)</c> ≈ 数十 KB/帧），
        /// 环容量 N 即 N 倍常驻。故容量按**已知回溯需求**给（如"分歧总在最近 3s 内"→ N = 180），
        /// 不要给整个对局长度。
        ///
        /// **覆盖行为**：环满后覆盖最旧一帧——<see cref="TryGetWorld"/> 对已被覆盖的帧返回 false
        /// （早于窗口的分歧只有帧号、没有字段报告）。这是刻意的有界内存取舍。
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
        /// 取某帧的世界存档（<paramref name="frame"/> = **1 基帧号**，与
        /// <see cref="SimReplayRunner.Result.FirstDivergenceFrame"/> 同口径）。
        /// 未启用存档 / 已被环覆盖 / 尚未记录 → false。
        /// **返回内部对象**（零拷贝）——调用方不得改写（它属于记录）。
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
