using System;
using System.Collections.Generic;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 确定性回放与分歧定位（<see cref="SimRunRecord"/> / <see cref="SimReplayRunner"/> /
    /// <see cref="SimWorldDiff"/>）用例。
    ///
    /// **这套用例锁定的是"可调试性"本身**：分歧发生时能定到帧号、指到字段。
    /// 此前这类问题只能靠断点跟长运算链（<c>SimMath</c> 里 1-ulp 那条实测即如此）。
    /// 断言分三层：① 同输入必同结论；② 人为注入分歧能被定到**准确的帧**；
    /// ③ 差异报告能指出**准确的字段**（不能只报"某处不同"）。
    /// </summary>
    public class SimReplayDiagnosticsTests
    {
        private static SimMapData Map()
        {
            var m = new SimMapData { GroundY = 0f, HalfWidth = 100f, HalfDepth = 100f };
            m.SpawnPoints[0] = new SimVector3(-10f, 0f, 0f);
            m.SpawnPoints[1] = new SimVector3(10f, 0f, 0f);
            m.SpawnPointCount = 2;
            return m;
        }

        /// <summary>两玩家对峙世界（同种子同初始态——回放一致性的前提）。</summary>
        private static SimWorldState NewWorld(out long id0, out long id1)
        {
            var s = new SimWorldState { RngState = 0xABCDEF01UL };
            id0 = s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-10f, 0f, 0f), Yaw = 0f }, out _);
            id1 = s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 0f), Yaw = SimTrig.Pi }, out _);
            return s;
        }

        /// <summary>造一段固定输入序列（含移动/瞄准/开火——覆盖多系统，避免只动 InputSystem）。</summary>
        private static SimInputFrame[][] ScriptedInputs(long id0, long id1, int frames)
        {
            var all = new SimInputFrame[frames][];
            for (int f = 0; f < frames; f++)
            {
                bool firing = f % 7 == 3;                       // 间歇开火 → 触发伤害/死亡/事件
                all[f] = new SimInputFrame[]
                {
                    new SimInputFrame
                    {
                        EntityId = id0,
                        MoveX = (f % 5 == 0) ? 1f : 0f, MoveZ = 0f,
                        AimPointX = 10f, AimPointY = 1f, AimPointZ = 0f,
                        Buttons = firing ? SimInputFrame.ButtonFire : 0u,
                    },
                    new SimInputFrame
                    {
                        EntityId = id1,
                        MoveX = 0f, MoveZ = (f % 3 == 0) ? -1f : 0f,
                        AimPointX = -10f, AimPointY = 1f, AimPointZ = 0f,
                        Buttons = (f % 11 == 5) ? SimInputFrame.ButtonAim : 0u,
                    },
                };
            }
            return all;
        }

        /// <summary>跑一段并录制（Step 会就地排序输入，故每次喂一份拷贝）。</summary>
        private static SimRunRecord RecordScripted(int frames, out ulong seed)
        {
            var map = Map();
            var world = NewWorld(out long id0, out long id1);
            seed = world.RngState;
            var record = SimRunRecord.Begin(world, map, playerCount: 2, label: "scripted");
            var scripted = ScriptedInputs(id0, id1, frames);
            var buffer = new SimInputFrame[2];

            for (int f = 0; f < frames; f++)
            {
                Array.Copy(scripted[f], buffer, 2);
                SimStep.Step(world, map, buffer);
                record.Capture(world, buffer);
            }
            return record;
        }

        // ---- ① 同输入必同结论 ----

        [Fact]
        public void 记录_同输入重放_逐帧一致()
        {
            var record = RecordScripted(120, out _);
            var result = SimReplayRunner.Replay(record);

            Assert.True(result.Matches, result.ToText());
            Assert.Equal(-1, result.FirstDivergenceFrame);
            Assert.Equal(120, result.ReplayedFrames);
            Assert.Equal(120, result.RecordedFrames);
            Assert.Contains("逐位相符", result.ToText());
        }

        [Fact]
        public void 记录_覆盖开火伤害死亡_非退化序列()
        {
            // 防止"序列没触发任何系统所以必然一致"的假绿：确认 Hp 确实掉过、有事件产生
            var record = RecordScripted(120, out _);
            Assert.True(record.FrameCount == 120);
            Assert.True(record.Checksums.Count == 120);
            // 至少两个不同的 checksum（世界确实在变）
            Assert.True(record.Checksums[0] != record.Checksums[119]);
        }

        [Fact]
        public void 回放_不污染记录_可重复回放()
        {
            // Step 会**就地排序**输入数组（§3.3 规范形）。若回放直接喂记录内部数组，
            // 记录会被改写——第二次回放结论可能不同。这条用例钉死这个坑。
            var record = RecordScripted(60, out _);

            var first = SimReplayRunner.Replay(record);
            var second = SimReplayRunner.Replay(record);

            Assert.True(first.Matches);
            Assert.True(second.Matches);
            Assert.Equal(first.ReplayedFrames, second.ReplayedFrames);
        }

        [Fact]
        public void 全序列复算_与记录逐帧相符()
        {
            var record = RecordScripted(80, out _);
            var actual = SimReplayRunner.ReplayAllChecksums(record, out int firstMismatch);

            Assert.Equal(-1, firstMismatch);
            Assert.Equal(record.FrameCount, actual.Count);
            for (int i = 0; i < actual.Count; i++)
                Assert.Equal(record.Checksums[i], actual[i]);
        }

        // ---- ② 人为注入分歧 → 必须定到**准确帧** ----

        [Fact]
        public void 分歧_篡改某帧输入_定位到该帧且不早不晚()
        {
            var record = RecordScripted(100, out _);
            const int tamperFrame = 42;                     // 0 基 → 分歧应报在第 43 帧（1 基）

            // 篡改记录里的第 42 帧输入（改瞄准点 → 必然改变该帧之后的走向）
            record.TryGetFrame(tamperFrame, out SimInputFrame[] inputs, out _);
            inputs[0].AimPointZ = 0.5f;                     // 原本 0

            var result = SimReplayRunner.Replay(record);

            Assert.False(result.Matches);
            Assert.Equal(tamperFrame + 1, result.FirstDivergenceFrame);
            Assert.Equal(43, result.ReplayedFrames);        // 早停：不继续跑后面的帧
            Assert.NotEqual(result.ExpectedChecksum, result.ActualChecksum);
            Assert.Contains("首个分歧帧 43", result.ToText());
        }

        [Fact]
        public void 分歧_篡改初始世界种子_首帧即分歧()
        {
            var record = RecordScripted(60, out _);
            record.Seed = 0u;                               // 记录声称的种子与实际初始态不符
            record.InitialState.RngState = 0UL;             // 真正改复现起点

            var result = SimReplayRunner.Replay(record);

            Assert.False(result.Matches);
            Assert.Equal(1, result.FirstDivergenceFrame);   // 种子进第 1 帧 checksum（MixUInt32(Frame) 后跟全量）
        }

        [Fact]
        public void 分歧_篡改初始位置_定位到首帧()
        {
            var record = RecordScripted(40, out _);
            // 只改记录的初始世界（不改录制时用的世界）→ 回放起点不同
            int slot = 0;
            Assert.True(record.InitialState.IsAlive(slot));
            record.InitialState.Entities[slot].Pos.X += 0.5f;

            var result = SimReplayRunner.Replay(record);
            Assert.False(result.Matches);
            Assert.Equal(1, result.FirstDivergenceFrame);
        }

        // ---- ③ 差异报告必须指出**准确字段** ----

        [Fact]
        public void 差异报告_指出具体字段而非笼统不同()
        {
            var worldA = NewWorld(out long id0, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);

            Assert.True(worldA.IsAlive(0));
            worldB.Entities[0].Pos.Y = 12.5f;                // 单字段偏差

            var report = SimWorldDiff.Compare(worldA, worldB);

            Assert.False(report.FullEqual);
            var diff = Assert.Single(report.Differences);
            Assert.Equal("Entities[0].Pos.Y", diff.Path);
            Assert.Contains("12.5", diff.Right);
            Assert.Contains("0", diff.Left);
        }

        [Fact]
        public void 差异报告_float按位型判_附十六进制证据()
        {
            var worldA = NewWorld(out _, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);

            // 1-ulp 偏差：字面量看起来一样，位型不同——这正是跨运行时分歧的典型形态
            float tiny = 1.0f;
            int bits = BitConverter.SingleToInt32Bits(tiny);
            float nudged = BitConverter.Int32BitsToSingle(bits + 1);

            worldB.Entities[0].Pos.X = nudged;
            var report = SimWorldDiff.Compare(worldA, worldB);

            var diff = Assert.Single(report.Differences);
            Assert.Equal("Entities[0].Pos.X", diff.Path);
            Assert.Contains("0x", diff.Left);               // 位型证据：字面量相同也能看出差别
            Assert.Contains("0x", diff.Right);
            Assert.NotEqual(diff.Left, diff.Right);
        }

        [Fact]
        public void 差异报告_定性公共面还是私有面()
        {
            // 私有面差异（弹药）——不进公共口径，线上不触发和解
            var worldA = NewWorld(out _, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);
            worldB.Weapons[0 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;

            var priv = SimWorldDiff.Compare(worldA, worldB);
            Assert.False(priv.FullEqual);
            Assert.True(priv.PublicEqual);                  // 公共口径相同
            Assert.Contains("私有面分歧", priv.ToText());

            // 公共面差异（Hp）——线上逐帧和解，必须修
            var worldC = new SimWorldState();
            worldA.CopyTo(worldC);
            worldC.Entities[0].Hp = 42;

            var pub = SimWorldDiff.Compare(worldA, worldC);
            Assert.False(pub.PublicEqual);
            Assert.Contains("公共面分歧", pub.ToText());
            Assert.Contains("必须修", pub.ToText());
        }

        [Fact]
        public void 差异报告_位图差异先报_不刷满字段噪声()
        {
            // 一侧有实体一侧没有：位图差异应先被指出（槽位存在性），
            // 而不是刷满"Entities[i].xxx 不同"（那种报告淹没了真正的根因）
            var worldA = NewWorld(out _, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);
            worldB.Despawn(worldB.Entities[1].Id);

            var report = SimWorldDiff.Compare(worldA, worldB);
            Assert.Contains(report.Differences, d => d.Path.StartsWith("AliveBitmap["));
            // 不比对"只有一侧活"的槽位字段（避免噪声）
            Assert.DoesNotContain(report.Differences, d => d.Path.StartsWith("Entities[1]."));
        }

        [Fact]
        public void 差异报告_上限截断_显式标记不静默()
        {
            var worldA = NewWorld(out _, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);

            // 制造**足够多**的差异（两个槽位各改多个字段——单改一个字段凑不出"超出上限"）
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!worldB.IsAlive(i)) continue;
                worldB.Entities[i].Pos.X += 1f;
                worldB.Entities[i].Pos.Y += 1f;
                worldB.Entities[i].Pos.Z += 1f;
                worldB.Entities[i].Hp = 7;
            }

            var report = SimWorldDiff.Compare(worldA, worldB, maxDifferences: 2);
            Assert.True(report.Truncated);                  // 有差异被丢弃 → 必须显式标记
            Assert.Contains("已达上限", report.ToText());

            // 反向：上限足够大时不标截断（"恰好等于上限"不算截断——没有差异被丢）
            var full = SimWorldDiff.Compare(worldA, worldB, maxDifferences: 64);
            Assert.False(full.Truncated);
        }

        [Fact]
        public void 差异报告_相同世界_单行无差异()
        {
            var worldA = NewWorld(out _, out _);
            var worldB = new SimWorldState();
            worldA.CopyTo(worldB);

            var report = SimWorldDiff.Compare(worldA, worldB);
            Assert.True(report.FullEqual);
            Assert.True(report.PublicEqual);
            Assert.Empty(report.Differences);
            Assert.Equal("无差异（全量口径相同）", report.ToText());
        }

        // ---- 端到端：记录 → 注入分歧 → 定位帧 → 指出字段 ----

        [Fact]
        public void 端到端_注入分歧后_既定位帧又指出字段()
        {
            var map = Map();
            var world = NewWorld(out long id0, out long id1);
            var record = SimRunRecord.Begin(world, map, playerCount: 2);
            var scripted = ScriptedInputs(id0, id1, 60);
            var buffer = new SimInputFrame[2];

            // 录制，同时把第 30 帧的世界留一份（模拟"记录方导出该帧世界"）
            SimWorldState worldAt30 = null;
            for (int f = 0; f < 60; f++)
            {
                Array.Copy(scripted[f], buffer, 2);
                SimStep.Step(world, map, buffer);
                record.Capture(world, buffer);
                if (f + 1 == 30)
                {
                    worldAt30 = new SimWorldState();
                    world.CopyTo(worldAt30);
                }
            }

            // 篡改第 30 帧输入 → 分歧应报在第 30 帧
            record.TryGetFrame(29, out SimInputFrame[] inputs, out _);
            inputs[0].MoveZ = 1f;

            var result = SimReplayRunner.ReplayAndCompare(record, frame =>
                frame == 30 ? worldAt30 : null);

            Assert.False(result.Matches);
            Assert.Equal(30, result.FirstDivergenceFrame);
            Assert.NotNull(result.DivergenceReport);
            // 报告必须能指出"移动导致的位置/速度不同"这类具体字段
            Assert.Contains(result.DivergenceReport.Differences,
                d => d.Path.StartsWith("Entities[0].Pos") || d.Path.StartsWith("Entities[0].Vel"));
        }

        [Fact]
        public void 记录_输入槽数不符_当场拒绝()
        {
            var world = NewWorld(out _, out _);
            var map = Map();
            var record = SimRunRecord.Begin(world, map, playerCount: 2);

            var wrong = new SimInputFrame[3];
            Assert.Throws<ArgumentException>(() => record.Capture(world, wrong));
        }

        // ---- 世界存档（BeginWithWorlds）：一键产出字段级报告 ----

        [Fact]
        public void 世界存档_一键产出一致结论()
        {
            var map = Map();
            var world = NewWorld(out long id0, out long id1);
            var record = SimRunRecord.BeginWithWorlds(world, map, playerCount: 2, worldRingCapacity: 64);
            var scripted = ScriptedInputs(id0, id1, 40);
            var buffer = new SimInputFrame[2];

            for (int f = 0; f < 40; f++)
            {
                Array.Copy(scripted[f], buffer, 2);
                SimStep.Step(world, map, buffer);
                record.Capture(world, buffer);
            }

            Assert.True(record.HasWorlds);
            Assert.Equal(64, record.WorldRingCapacity);

            var result = SimReplayRunner.ReplayWithRecordedWorlds(record);
            Assert.True(result.Matches, result.ToText());
        }

        [Fact]
        public void 世界存档_注入分歧后一键拿到字段级报告()
        {
            var map = Map();
            var world = NewWorld(out long id0, out long id1);
            var record = SimRunRecord.BeginWithWorlds(world, map, playerCount: 2, worldRingCapacity: 128);
            var scripted = ScriptedInputs(id0, id1, 50);
            var buffer = new SimInputFrame[2];

            for (int f = 0; f < 50; f++)
            {
                Array.Copy(scripted[f], buffer, 2);
                SimStep.Step(world, map, buffer);
                record.Capture(world, buffer);
            }

            // 篡改第 20 帧移动输入 → 该帧起世界不同
            record.TryGetFrame(19, out SimInputFrame[] inputs, out _);
            inputs[0].MoveZ = 1f;

            // 一键：不需要调用方另外导出"记录方该帧的世界"
            var result = SimReplayRunner.ReplayWithRecordedWorlds(record);

            Assert.False(result.Matches);
            Assert.Equal(20, result.FirstDivergenceFrame);
            Assert.NotNull(result.DivergenceReport);
            Assert.Contains(result.DivergenceReport.Differences,
                d => d.Path.StartsWith("Entities[0].Pos") || d.Path.StartsWith("Entities[0].Vel"));
            Assert.Contains("首个分歧帧 20", result.ToText());
        }

        [Fact]
        public void 世界存档_环满后旧帧被覆盖_查询返回false()
        {
            var map = Map();
            var world = NewWorld(out long id0, out long id1);
            // 小环（4 帧）跑 10 帧 → 只有最近 4 帧可查
            var record = SimRunRecord.BeginWithWorlds(world, map, playerCount: 2, worldRingCapacity: 4);
            var scripted = ScriptedInputs(id0, id1, 10);
            var buffer = new SimInputFrame[2];

            for (int f = 0; f < 10; f++)
            {
                Array.Copy(scripted[f], buffer, 2);
                SimStep.Step(world, map, buffer);
                record.Capture(world, buffer);
            }

            Assert.False(record.TryGetWorld(1, out _));      // 已被覆盖
            Assert.True(record.TryGetWorld(10, out _));      // 最近一帧在环内
            Assert.True(record.TryGetWorld(7, out _));       // 窗口下界
            Assert.False(record.TryGetWorld(6, out _));      // 刚好被挤出
        }

        [Fact]
        public void 世界存档_未启用时查询返回false_不伪造()
        {
            var world = NewWorld(out _, out _);
            var record = SimRunRecord.Begin(world, Map(), playerCount: 2);   // 普通 Begin（无存档）

            Assert.False(record.HasWorlds);
            Assert.False(record.TryGetWorld(1, out _));
        }

        [Fact]
        public void 世界存档_环容量非法_当场拒绝()
        {
            var world = NewWorld(out _, out _);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimRunRecord.BeginWithWorlds(world, Map(), playerCount: 2, worldRingCapacity: 0));
        }

        [Fact]
        public void 记录_空记录回放_拒绝而非静默通过()
        {
            var world = NewWorld(out _, out _);
            var record = SimRunRecord.Begin(world, Map(), playerCount: 2);
            Assert.Throws<ArgumentException>(() => SimReplayRunner.Replay(record));
        }
    }
}
