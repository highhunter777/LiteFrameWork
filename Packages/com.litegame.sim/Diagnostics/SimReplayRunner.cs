using System;
using System.Collections.Generic;

namespace LiteSim
{
    /// <summary>
    /// 离线回放器：把 <see cref="SimRunRecord"/> 在**当前运行时**上重跑一遍， 找出**首个分歧帧**并给出该帧的字段级差异。
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
            /// 人读结论。
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
        public static Result Replay(SimRunRecord record, in CombatValues values, WeaponTable weapons)
        {
            return ReplayCore(record, null, values, weapons);
        }

        /// <summary>
        /// 回放并**一键产出字段级报告**：对比源取自记录自身的世界存档 （要求记录以 <see cref="SimRunRecord.BeginWithWorlds"/> 开启，且分歧帧仍在环窗口内）。
        /// </summary>
        public static Result ReplayWithRecordedWorlds(SimRunRecord record, in CombatValues values,
            WeaponTable weapons)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return ReplayCore(record, frame =>
                record.TryGetWorld(frame, out SimWorldState world) ? world : null, values, weapons);
        }

        /// <summary>
        /// 回放并逐帧比对 checksum，且在第 <paramref name="divergentFrame"/> 帧（1 基） 提供记录方的世界快照——**用于产出字段级差异报告**。
        /// </summary>
        public static Result ReplayAndCompare(SimRunRecord record, Func<int, SimWorldState> expectedWorldAt,
            in CombatValues values, WeaponTable weapons)
        {
            return ReplayCore(record, expectedWorldAt, values, weapons);
        }

        private static Result ReplayCore(SimRunRecord record, Func<int, SimWorldState> expectedWorldAt,
            in CombatValues values, WeaponTable weapons)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.FrameCount == 0) throw new ArgumentException("记录为空——没有可回放的帧", nameof(record));

            // 复现起点：初始世界深拷 + 地图深拷（记录期已各存一份，此处再拷避免回放污染记录）
            var state = new SimWorldState();
            record.InitialState.CopyTo(state);
            var map = new SimMapData();
            record.Map.CopyTo(map);

            // 测试规则随装载值回放（进房初值进 CombatValues——重放对账同口径；中局开合不可重放，与既有口径一致）
            var rules = new MatchTestRules { NoDeath = values.NoDeath, InfiniteAmmo = values.InfiniteAmmo };

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

                SimStep.Step(state, map, buffer, values, weapons, rules: rules);
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
        public static IReadOnlyList<uint> ReplayAllChecksums(SimRunRecord record, out int firstMismatchFrame,
            in CombatValues values, WeaponTable weapons)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            firstMismatchFrame = -1;

            var state = new SimWorldState();
            record.InitialState.CopyTo(state);
            var map = new SimMapData();
            record.Map.CopyTo(map);
            var rules = new MatchTestRules { NoDeath = values.NoDeath, InfiniteAmmo = values.InfiniteAmmo };   // 随装载值回放（同 ReplayCore 口径）

            var actual = new List<uint>(record.FrameCount);
            var buffer = new SimInputFrame[record.PlayerCount];

            for (int i = 0; i < record.FrameCount; i++)
            {
                record.TryGetFrame(i, out SimInputFrame[] inputs, out uint expected);
                Array.Copy(inputs, buffer, record.PlayerCount);
                SimStep.Step(state, map, buffer, values, weapons, rules: rules);

                uint c = SimChecksum.ComputeChecksum(state);
                actual.Add(c);
                if (firstMismatchFrame < 0 && c != expected) firstMismatchFrame = i + 1;   // lint-allow R3
            }
            return actual;
        }
    }
}
