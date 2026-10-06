using System;
using System.Collections.Generic;

namespace LiteSim
{
    /// <summary>
    /// 离线回放器：把 <see cref="SimRunRecord"/> 在**当前运行时**上重跑一遍，
    /// 找出**首个分歧帧**并给出该帧的字段级差异。
    ///
    /// **用途（跨运行时一致性验收）**：记录在 A 运行时（如 .NET 8 服务器）跑出的 checksum 序列，
    /// 在 B 运行时（如 Unity/Mono 客户端）回放——首个不一致的帧号即分歧点，差异报告指出字段。
    /// 这正是 <see cref="SimMath"/> 里 1-ulp 那类问题**唯一可规模化的排查手段**：
    /// 不必猜、不必断点跟长运算链，跑一遍就有答案。
    ///
    /// **口径**：回放严格按记录顺序 Step（不预测、不回滚、不跳帧）——纯粹是"确定性复算"，
    /// 把 Play 侧的时序噪声全部排除。故本类只认 <see cref="SimRunRecord"/>，不接 RollbackSim。
    /// </summary>
    public static class SimReplayRunner
    {
        /// <summary>回放结论。</summary>
        public sealed class Result
        {
            /// <summary>是否逐帧一致（true = 复现完全成功，本运行时与记录源逐位一致）。</summary>
            public bool Matches => FirstDivergenceFrame < 0;

            /// <summary>首个分歧的**记录帧号**（1 基，= 第几帧执行后；-1 = 无分歧）。</summary>
            public int FirstDivergenceFrame { get; internal set; } = -1;

            /// <summary>分歧帧的两侧 checksum（无分歧时相等）。</summary>
            public uint ExpectedChecksum { get; internal set; }

            public uint ActualChecksum { get; internal set; }

            /// <summary>分歧帧的字段级差异报告（无分歧为 null）。</summary>
            public SimWorldDiff.Report DivergenceReport { get; internal set; }

            /// <summary>实际回放的帧数（记录有 N 帧但中途早停时为分歧帧号）。</summary>
            public int ReplayedFrames { get; internal set; }

            /// <summary>记录总帧数。</summary>
            public int RecordedFrames { get; internal set; }

            /// <summary>
            /// 人读结论。分歧时给出**决策信息**：分歧帧号 + 是该帧的哪个字段开始不同
            /// （字段报告是"与记录源对比"的——注意对比对象是**记录方的世界**，
            /// 但记录只存 checksum 不存世界，故差异报告需由调用方提供记录方的世界；
            /// 见 <see cref="SimReplayRunner.ReplayAndCompare"/> 的 <c>expectedWorldAt</c> 参数）。
            /// </summary>
            public string ToText()
            {
                if (Matches)
                    return $"回放一致：{ReplayedFrames} 帧逐位相符（本运行时与记录源同源）";

                return $"**首个分歧帧 {FirstDivergenceFrame}**（记录 {RecordedFrames} 帧，回放到此为止）\n"
                    + $"  该帧全量 checksum: 期望 {FirstDivergenceFrame} → 0x{ExpectedChecksum:X8}，实得 0x{ActualChecksum:X8}\n"
                    + (DivergenceReport != null
                        ? "  " + DivergenceReport.ToText().Replace("\n", "\n  ")
                        : "  （未提供记录方世界——只能定位帧号，给不出字段差异；"
                          + "回放时用 ReplayAndCompare 并传 expectedWorldAt 可拿到字段级报告）");
            }

            public override string ToString() => ToText();
        }

        /// <summary>
        /// 回放并逐帧比对 checksum（**轻量路径**：只定位首个分歧帧号，不需要记录方世界）。
        /// </summary>
        public static Result Replay(SimRunRecord record)
        {
            return ReplayCore(record, null);
        }

        /// <summary>
        /// 回放并**一键产出字段级报告**：对比源取自记录自身的世界存档
        /// （要求记录以 <see cref="SimRunRecord.BeginWithWorlds"/> 开启，且分歧帧仍在环窗口内）。
        ///
        /// 这是排查的推荐入口——不必由调用方另外导出"记录方该帧的世界"。
        /// 分歧帧已被环覆盖时退化为 <see cref="Replay"/> 的等价形态（报告为 null，只给帧号）。
        /// </summary>
        public static Result ReplayWithRecordedWorlds(SimRunRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return ReplayCore(record, frame =>
                record.TryGetWorld(frame, out SimWorldState world) ? world : null);
        }

        /// <summary>
        /// 回放并逐帧比对 checksum，且在第 <paramref name="divergentFrame"/> 帧（1 基）
        /// 提供记录方的世界快照——**用于产出字段级差异报告**。
        ///
        /// 用法：先跑 <see cref="Replay"/> 拿到分歧帧号，再用记录里保存的该帧世界（若有）重跑一次。
        /// 若记录方只存了 checksum（<see cref="SimRunRecord"/> 的默认形态，省体积），
        /// 则本方法退化为 <see cref="Replay"/> 的等价值（报告为 null）——
        /// **这是刻意的取舍**：存全世界 = 体积 O(帧数 × 世界)，存 checksum = O(帧数)。
        /// 排查时用"记录方在同一输入下重跑并导出该帧世界"补齐对比对象。
        /// </summary>
        public static Result ReplayAndCompare(SimRunRecord record, Func<int, SimWorldState> expectedWorldAt)
        {
            return ReplayCore(record, expectedWorldAt);
        }

        private static Result ReplayCore(SimRunRecord record, Func<int, SimWorldState> expectedWorldAt)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.FrameCount == 0) throw new ArgumentException("记录为空——没有可回放的帧", nameof(record));

            // 复现起点：初始世界深拷 + 地图深拷（记录期已各存一份，此处再拷避免回放污染记录）
            var state = new SimWorldState();
            record.InitialState.CopyTo(state);
            var map = new SimMapData();
            record.Map.CopyTo(map);

            var result = new Result
            {
                RecordedFrames = record.FrameCount,
                ReplayedFrames = 0,
            };

            var buffer = new SimInputFrame[record.PlayerCount];

            for (int i = 0; i < record.FrameCount; i++)
            {
                record.TryGetFrame(i, out SimInputFrame[] inputs, out uint expected);

                // 输入按帧拷贝到复用缓冲：Step 会**就地排序**输入数组（§3.3 规范形），
                // 直接喂记录内部数组会改写记录（回放不可重复）。这是本类最易踩的坑。
                Array.Copy(inputs, buffer, record.PlayerCount);

                SimStep.Step(state, map, buffer);
                result.ReplayedFrames++;

                uint actual = SimChecksum.ComputeChecksum(state);
                if (actual == expected) continue;   // lint-allow R3（uint 位级判等，非浮点精度比较）

                // 首个分歧：记录并（若有对比源）产出字段级报告
                result.FirstDivergenceFrame = i + 1;   // 1 基：第几帧执行后
                result.ExpectedChecksum = expected;
                result.ActualChecksum = actual;
                if (expectedWorldAt != null)   // lint-allow R3（整型判等，非浮点精度比较）
                {
                    var expectedWorld = expectedWorldAt(result.FirstDivergenceFrame);
                    if (expectedWorld != null) result.DivergenceReport = SimWorldDiff.Compare(expectedWorld, state);   // lint-allow R3（整型判等，非浮点精度比较）
                }
                return result;
            }

            return result;   // 全程一致
        }

        /// <summary>
        /// 逐帧 checksum 序列复算（**不做早停**——用于要完整比对的场合，如导出全序列做归档对照）。
        /// 返回序列长度 = 记录帧数；<paramref name="firstMismatchFrame"/> = 首个不符帧号（1 基，-1 = 全符）。
        /// </summary>
        public static IReadOnlyList<uint> ReplayAllChecksums(SimRunRecord record, out int firstMismatchFrame)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            firstMismatchFrame = -1;

            var state = new SimWorldState();
            record.InitialState.CopyTo(state);
            var map = new SimMapData();
            record.Map.CopyTo(map);

            var actual = new List<uint>(record.FrameCount);
            var buffer = new SimInputFrame[record.PlayerCount];

            for (int i = 0; i < record.FrameCount; i++)
            {
                record.TryGetFrame(i, out SimInputFrame[] inputs, out uint expected);
                Array.Copy(inputs, buffer, record.PlayerCount);
                SimStep.Step(state, map, buffer);

                uint c = SimChecksum.ComputeChecksum(state);
                actual.Add(c);
                if (firstMismatchFrame < 0 && c != expected) firstMismatchFrame = i + 1;   // lint-allow R3
            }
            return actual;
        }
    }
}
