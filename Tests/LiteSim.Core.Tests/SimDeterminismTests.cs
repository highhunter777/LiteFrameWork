using System;
using System.IO;
using System.Text;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 确定性 / 校验基线 / FrameDriver 用例（《M8 实施指导》§3，对账 §9 M8 验收①②③）。
    /// 基线机制复用 M7 同款：.NET 侧记录 Baselines/SimChecksum.txt，逐值比较（非容差）。
    /// </summary>
    public class SimDeterminismTests
    {
        // ---- §9 验收①②：固定输入序列跑 3000 帧结果稳定 / 重跑逐字节一致 ----

        [Fact]
        public void 确定性_3000帧两次运行checksum序列一致()
        {
            uint[] a = SimChecksumBaselineSpec.RunChecksumSequence(SimChecksumBaselineSpec.FrameCount);
            uint[] b = SimChecksumBaselineSpec.RunChecksumSequence(SimChecksumBaselineSpec.FrameCount);
            Assert.Equal(a, b);
        }

        [Fact]
        public void 确定性_3000帧两次运行最终状态逐元素一致()
        {
            var a = SimChecksumBaselineSpec.Run(SimChecksumBaselineSpec.FrameCount, -1);
            var b = SimChecksumBaselineSpec.Run(SimChecksumBaselineSpec.FrameCount, -1);
            AssertWorldsElementWiseEqual(a.Final, b.Final);
        }

        [Fact]
        public void 确定性_中途快照CopyTo逐元素一致且不扰动后续()
        {
            const int snapshotFrame = 1234;

            var a = SimChecksumBaselineSpec.Run(SimChecksumBaselineSpec.FrameCount, snapshotFrame);
            var b = SimChecksumBaselineSpec.Run(SimChecksumBaselineSpec.FrameCount, snapshotFrame);

            // 快照逐元素一致（Array.Copy 深拷语义）
            AssertWorldsElementWiseEqual(a.Snapshot, b.Snapshot);
            Assert.Equal(a.Sequence[snapshotFrame], b.Sequence[snapshotFrame]);

            // 快照后继续运行不受影响（无别名共享——§7 风险 1 对策）
            AssertWorldsElementWiseEqual(a.Final, b.Final);
            Assert.Equal(a.Sequence, b.Sequence);
        }

        // ---- §9 验收②③：校验基线（.NET 侧记录；Unity 侧对账为预测质量保障） ----

        [Fact]
        public void 基线_文件存在()
        {
            string path = BaselinePath();
            Assert.True(File.Exists(path), "缺少 Sim checksum 基线文件：" + path);
        }

        [Fact]
        public void 基线_3000帧序列与基线逐值一致()
        {
            string path = BaselinePath();
            Assert.True(File.Exists(path), "缺少 Sim checksum 基线文件：" + path);

            var expected = new System.Collections.Generic.List<uint>();
            string[] raw = File.ReadAllLines(path);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                expected.Add(uint.Parse(line, System.Globalization.CultureInfo.InvariantCulture));
            }

            uint[] actual = SimChecksumBaselineSpec.RunChecksumSequence(SimChecksumBaselineSpec.FrameCount);
            Assert.Equal(expected.Count, actual.Length);
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// 基线记录（手动运行）：设 SIM_RECORD_BASELINE=1 后 dotnet test --filter 本用例，
        /// 将规格输出写入源码 Baselines/ 目录。CI/常规运行下为空操作——防止静默重写掩盖回归。
        /// </summary>
        [Fact]
        public void 基线_记录文件_手动运行()
        {
            if (Environment.GetEnvironmentVariable("SIM_RECORD_BASELINE") != "1") return;

            string root = FindRepoRoot();
            string path = Path.Combine(root, "Tests", "LiteSim.Core.Tests", "Baselines", "SimChecksum.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, SimChecksumBaselineSpec.BuildText(), new UTF8Encoding(false));
        }

        // ---- FrameDriver（§3 测试组：累加器 + 追帧） ----

        [Fact]
        public void FrameDriver_半帧输入不推进()
        {
            var s = new SimWorldState();
            var driver = new FrameDriver();
            driver.Tick(0.5f * SimConfig.Dt, s, SimChecksumBaselineSpec.BuildMap(), new SimInputFrame[0], CombatValues.Default, WeaponTable.Default);
            Assert.Equal(0, driver.StepsLastTick);
            Assert.Equal(0, s.Frame);
        }

        [Fact]
        public void FrameDriver_两帧半推进两帧()
        {
            var s = new SimWorldState();
            var driver = new FrameDriver();
            driver.Tick(2.5f * SimConfig.Dt, s, SimChecksumBaselineSpec.BuildMap(), new SimInputFrame[0], CombatValues.Default, WeaponTable.Default);
            Assert.Equal(2, driver.StepsLastTick);
            Assert.Equal(2, s.Frame);

            // 余量（~0.5 帧）保留并参与后续累计：再给 2 帧量 → 2.5 帧余量 → 推进 2 帧
            // （用整帧量验证余量，避免半帧累加恰好压在浮点比较边界上）
            driver.Tick(2f * SimConfig.Dt, s, SimChecksumBaselineSpec.BuildMap(), new SimInputFrame[0], CombatValues.Default, WeaponTable.Default);
            Assert.Equal(2, driver.StepsLastTick);
            Assert.Equal(4, s.Frame);
        }

        [Fact]
        public void FrameDriver_积压百帧单次最多追五()
        {
            var s = new SimWorldState();
            var driver = new FrameDriver();
            driver.Tick(100f * SimConfig.Dt, s, SimChecksumBaselineSpec.BuildMap(), new SimInputFrame[0], CombatValues.Default, WeaponTable.Default);
            Assert.Equal(SimConfig.MaxCatchUp, driver.StepsLastTick);
            Assert.Equal(SimConfig.MaxCatchUp, s.Frame);

            // 余量已丢弃（防死亡螺旋）：再来 1 帧量只推进 1 帧，不继续追旧账
            driver.Tick(1f * SimConfig.Dt, s, SimChecksumBaselineSpec.BuildMap(), new SimInputFrame[0], CombatValues.Default, WeaponTable.Default);
            Assert.Equal(1, driver.StepsLastTick);
            Assert.Equal(SimConfig.MaxCatchUp + 1, s.Frame);
        }

        // ---- 辅助 ----

        private static string BaselinePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Baselines", "SimChecksum.txt");
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "Tests", "Tests.slnx"))
                    && Directory.Exists(Path.Combine(d.FullName, "Assets")))
                    return d.FullName;
            }
            throw new DirectoryNotFoundException("无法定位仓库根目录（Tests/Tests.slnx + Assets）");
        }

        /// <summary>逐元素比较两个世界（含全部槽位逻辑字段与两个平面数组——"逐字节一致"的可执行形态）。</summary>
        private static void AssertWorldsElementWiseEqual(SimWorldState a, SimWorldState b)
        {
            Assert.Equal(a.Frame, b.Frame);
            Assert.Equal(a.RngState, b.RngState);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                Assert.Equal(a.Entities[i].Id, b.Entities[i].Id);
                Assert.Equal(a.Entities[i].Pos.X, b.Entities[i].Pos.X);
                Assert.Equal(a.Entities[i].Pos.Y, b.Entities[i].Pos.Y);
                Assert.Equal(a.Entities[i].Pos.Z, b.Entities[i].Pos.Z);
                Assert.Equal(a.Entities[i].Vel.X, b.Entities[i].Vel.X);
                Assert.Equal(a.Entities[i].Vel.Y, b.Entities[i].Vel.Y);
                Assert.Equal(a.Entities[i].Vel.Z, b.Entities[i].Vel.Z);
                Assert.Equal(a.Entities[i].Yaw, b.Entities[i].Yaw);
                Assert.Equal(a.Entities[i].Hp, b.Entities[i].Hp);
                Assert.Equal(a.Entities[i].Flags, b.Entities[i].Flags);
            }

            for (int i = 0; i < a.AliveBitmap.Length; i++) Assert.Equal(a.AliveBitmap[i], b.AliveBitmap[i]);
            for (int i = 0; i < a.Globals.Length; i++) Assert.Equal(a.Globals[i], b.Globals[i]);
            for (int i = 0; i < a.CustomData.Length; i++) Assert.Equal(a.CustomData[i], b.CustomData[i]);
        }
    }
}
