using System;
using System.Collections.Generic;
using LiteView;
using LiteView.DamageNumbers;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 伤害数字纯逻辑件验收（《命中反馈与伤害数字专项设计》批B §4.2/§8）：聚合器（合并窗口/爆头档分离/
    /// 双钟分离/账实分离）、轨道数学（升浮单调、淡出线性、抖动包络、出场缩放、推挤确定性）、
    /// 样式常量表（承受染红/爆头红与深红/按值分带）、K 简写格式化。纯规则、零引擎（源链接）。
    /// 断言口径：数值一律 Assert.True + 显式容差（xunit 的 Equal(float,float,tolerance) 语义是
    /// 容差不是精度位，易误读——统一手写差值判据，消息即判据文档）。
    /// </summary>
    public sealed class DamageNumberTests
    {
        private static void Near(double expected, double actual, string because)
            => Assert.True(Math.Abs(expected - actual) <= 1e-6, $"{because}（期望 {expected}，实得 {actual}）");

        private static void Near(float expected, float actual, string because)
            => Assert.True(MathF.Abs(expected - actual) <= 1e-5f, $"{because}（期望 {expected}，实得 {actual}）");

        // ---- 聚合器 ----

        [Fact]
        public void 聚合_首次命中_新条目_值暴击种子直落()
        {
            var agg = new DamageNumberAggregator();
            var snap = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 30, crit: false, now: 1.0, seed: 11);

            Assert.False(snap.Merged, "首次命中必是新条目");
            Assert.Equal(30, snap.Value);
            Assert.False(snap.Crit);
            Assert.Equal(11, snap.Seed);
            Near(1.0, snap.SpawnedAt, "出场钟 = now");
            Near(1.0, snap.LastMergeAt, "首条目最后活动钟 = now");
            Assert.Equal(1, agg.ActiveCount);
        }

        [Fact]
        public void 聚合_窗口内同档二连击_并进既有_累计且双钟各司其职()
        {
            var agg = new DamageNumberAggregator(mergeWindowSeconds: 0.45f);
            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 30, crit: false, now: 1.0, seed: 1);

            var snap = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 25, crit: false, now: 1.2, seed: 99);

            Assert.True(snap.Merged, "窗口内同档命中并进既有条目");
            Assert.Equal(55, snap.Value);
            Assert.False(snap.Crit, "普通档合并后仍是普通（无粘滞升级）");
            Near(1.2, snap.LastMergeAt, "淡出钟重置（连击驻留）");
            Near(1.0, snap.SpawnedAt, "出场钟不重置（数字不跳回起点）");
            Assert.Equal(1, snap.Seed);
            Assert.Equal(1, agg.ActiveCount);
        }

        [Fact]
        public void 聚合_爆头与普通_分离成条_各并各的()
        {
            // 用户裁决："连击合并区分爆头/普通，爆头合爆头的、普通合普通的"——爆头档进合并键。
            var agg = new DamageNumberAggregator(mergeWindowSeconds: 0.45f);
            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 30, crit: false, now: 1.0, seed: 1);

            var critFirst = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 50, crit: true, now: 1.1, seed: 2);
            Assert.False(critFirst.Merged, "爆头不并普通——独立成条");
            Assert.True(critFirst.Crit);
            Assert.Equal(50, critFirst.Value);
            Assert.Equal(2, agg.ActiveCount);

            var normalAgain = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 20, crit: false, now: 1.2, seed: 3);
            Assert.True(normalAgain.Merged, "普通只并普通");
            Assert.Equal(50, normalAgain.Value);
            Assert.False(normalAgain.Crit, "打头再打身体不得把普通条染红（旧粘滞已废）");

            var critAgain = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, crit: true, now: 1.3, seed: 4);
            Assert.True(critAgain.Merged, "爆头只并爆头");
            Assert.Equal(60, critAgain.Value);
            Assert.True(critAgain.Crit);
            Assert.Equal(2, agg.ActiveCount);
        }

        [Fact]
        public void 聚合_窗口边界_恰满窗并入_刚出窗起新条目()
        {
            var agg = new DamageNumberAggregator(mergeWindowSeconds: 0.5f);
            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, crit: false, now: 0.0, seed: 1);

            var inWindow = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, crit: false, now: 0.5, seed: 2);
            Assert.True(inWindow.Merged, "恰满窗口（≤）→ 并入");
            Assert.Equal(20, inWindow.Value);

            var outWindow = agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, crit: false, now: 0.5 + 0.5001, seed: 3);
            Assert.False(outWindow.Merged, "刚出窗 → 新条目（值直落、新种子）");
            Assert.Equal(10, outWindow.Value);
            Assert.Equal(3, outWindow.Seed);
        }

        [Fact]
        public void 聚合_承受与造成_同目标各记各的()
        {
            var agg = new DamageNumberAggregator();

            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 30, false, now: 1.0, seed: 1);
            agg.MergeOrSpawn(7L, HitLocalRole.Received, 20, false, now: 1.1, seed: 2);

            Assert.Equal(2, agg.ActiveCount);
            Assert.True(agg.TryGet(7L, HitLocalRole.Caused, crit: false, out var caused), "造成账在册");
            Assert.Equal(30, caused.Value);
            Assert.True(agg.TryGet(7L, HitLocalRole.Received, crit: false, out var received), "承受账在册");
            Assert.Equal(20, received.Value);
        }

        [Fact]
        public void 聚合_不同目标_互不合并()
        {
            var agg = new DamageNumberAggregator();
            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 30, false, now: 1.0, seed: 1);
            var snap = agg.MergeOrSpawn(8L, HitLocalRole.Caused, 10, false, now: 1.1, seed: 2);
            Assert.False(snap.Merged);
            Assert.Equal(2, agg.ActiveCount);
        }

        [Fact]
        public void 聚合_到期收集_淡出终点起判_摘账后清空()
        {
            var agg = new DamageNumberAggregator();
            agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, false, now: 0.0, seed: 1);

            var expired = new List<(long TargetId, HitLocalRole Role, bool Crit)>();
            agg.CollectExpired(now: DamageNumberMotion.FadeEndSeconds - 0.01, expired);
            Assert.True(expired.Count == 0, "淡出终点前不到期");

            // 终点判定按 ≥，但 FadeEndSeconds 是 float 常量（二进制不可精确表示）——
            // 边界断言用「终点前 1ms / 终点后 1ms」钉窗语义，不钉浮点表示的相等
            agg.CollectExpired(now: (double)DamageNumberMotion.FadeEndSeconds + 0.01, expired);
            Assert.True(expired.Count == 1, "过淡出终点即到期");
            Assert.Equal((7L, HitLocalRole.Caused, false), expired[0]);

            Assert.True(agg.Remove(7L, HitLocalRole.Caused, crit: false), "摘账成功");
            Assert.Equal(0, agg.ActiveCount);

            agg.CollectExpired(now: 1.11, expired);
            Assert.True(expired.Count == 0, "摘账后不再到期（账实一致）");
        }

        [Fact]
        public void 聚合_连击驻留_窗口内持续合并_期间永不到期()
        {
            var agg = new DamageNumberAggregator(mergeWindowSeconds: 0.45f);
            var expired = new List<(long TargetId, HitLocalRole Role, bool Crit)>();

            double now = 0.0;
            for (int i = 0; i < 5; i++)
            {
                agg.MergeOrSpawn(7L, HitLocalRole.Caused, 10, false, now: now, seed: i);
                agg.CollectExpired(now: now, expired);
                Assert.True(expired.Count == 0, $"第 {i} 连击时未到期（淡出钟被合并重置）");
                now += 0.4;
            }

            Assert.True(agg.TryGet(7L, HitLocalRole.Caused, crit: false, out var snap));
            Assert.Equal(50, snap.Value);
        }

        // ---- 格式化 ----

        [Theory]
        [InlineData(0, "0")]
        [InlineData(9999, "9999")]
        [InlineData(10000, "10K")]
        [InlineData(12345, "12.3K")]
        [InlineData(20000, "20K")]
        public void 格式化_位数与K简写边界(int value, string expected)
        {
            Assert.Equal(expected, DamageNumberFormatter.Format(value));
        }

        // ---- 样式 ----

        [Fact]
        public void 样式档_承受染红_造成白_爆头红与深红且放大()
        {
            var r = new DamageNumberStyleResolver();

            var caused = r.Resolve(10, HitLocalRole.Caused, crit: false);
            Near(1f, caused.R, "造成档红分量 = 1（白）");
            Near(1f, caused.G, "造成档绿分量 = 1（白）");
            Near(1f, caused.B, "造成档蓝分量 = 1（白）");

            var received = r.Resolve(10, HitLocalRole.Received, crit: false);
            Assert.True(received.R > received.G + 0.5f, "承受档染红（红分量显著高于绿）");

            // 爆头 = **亮红**（玩家口径"爆头飘字要红"）：红占绝对主导，且与承受档的暗砖红靠明度区分
            var critCaused = r.Resolve(10, HitLocalRole.Caused, crit: true);
            Assert.True(critCaused.R > critCaused.G + 0.5f, "造成爆头 = 红（红分量显著高于绿）");
            Assert.True(critCaused.G < received.G, "爆头亮红的绿分量低于承受暗砖红（明度分层：爆头更亮）");
            Assert.True(critCaused.FontSize > caused.FontSize, "暴击放大字号");

            var critReceived = r.Resolve(10, HitLocalRole.Received, crit: true);
            Assert.True(critReceived.FontSize > received.FontSize, "承受暴击同放大");
            Assert.True(critReceived.R > critReceived.G + 0.7f, "承受暴击 = 深红");
        }

        [Theory]
        [InlineData(19, 1f)]
        [InlineData(20, 1.05f)]
        [InlineData(49, 1.05f)]
        [InlineData(50, 1.12f)]
        [InlineData(99, 1.12f)]
        [InlineData(100, 1.20f)]
        [InlineData(999, 1.20f)]
        public void 样式_按值分带缩放边界_含左端(int value, float expected)
        {
            Near(expected, DamageNumberStyleResolver.ValueScaleBoost(value), $"值 {value} 的分带缩放");
        }

        [Fact]
        public void 样式_分带缩放与角色档正交_合成进快照()
        {
            var r = new DamageNumberStyleResolver();
            var big = r.Resolve(100, HitLocalRole.Caused, crit: false);
            Near(1.20f, big.ScaleBoost, "大伤害（≥100）带缩放");
            Near(1f, big.R, "色仍白（分带缩放与角色档正交）");
        }

        // ---- 轨道数学 ----

        [Fact]
        public void 轨道_升浮单调不减_峰值后钳制恒定()
        {
            float peakT = DamageNumberMotion.RiseSpeed / DamageNumberMotion.RiseDecel;
            float prev = 0f;
            float afterPeak = -1f;

            for (int i = 0; i <= 80; i++)
            {
                float t = i * 0.05f;                       // 0..4s（覆盖峰值与寿命外）
                DamageNumberMotion.Evaluate(t, t, crit: false, seed: 1,
                    out float rise, out _, out _, out _, out _);
                Assert.True(rise >= prev - 1e-5f, $"升浮不得回落 t={t}");
                prev = rise;
                if (t > peakT) afterPeak = rise;
            }

            Assert.True(afterPeak > 0f, "峰值后仍在悬停高度");
            Near(prev, afterPeak, "峰值后钳制恒定（最后段不再变化）");
        }

        [Fact]
        public void 轨道_淡出_起点前恒一_线性到零_终点后恒零()
        {
            DamageNumberMotion.Evaluate(0.1f, 0.1f, false, 0, out _, out _, out float before, out _, out _);
            Near(1f, before, "淡出起点前恒全显");

            DamageNumberMotion.Evaluate(0.5f, DamageNumberMotion.FadeStartSeconds, false, 0,
                out _, out _, out float atStart, out _, out _);
            Near(1f, atStart, "恰淡出起点仍全显（≤ 含左端）");

            float span = DamageNumberMotion.FadeEndSeconds - DamageNumberMotion.FadeStartSeconds;
            DamageNumberMotion.Evaluate(0.5f, DamageNumberMotion.FadeStartSeconds + span * 0.5f, false, 0,
                out _, out _, out float mid, out _, out _);
            Near(0.5f, mid, "淡出中点线性 0.5");

            DamageNumberMotion.Evaluate(0.5f, DamageNumberMotion.FadeEndSeconds, false, 0,
                out _, out _, out float end, out _, out _);
            Near(0f, end, "终点归零");

            DamageNumberMotion.Evaluate(0.5f, DamageNumberMotion.FadeEndSeconds + 1f, false, 0,
                out _, out _, out float after, out _, out _);
            Near(0f, after, "终点后恒零");
        }

        [Fact]
        public void 轨道_抖动_非暴击恒零_暴击窗内有界_窗外归零()
        {
            DamageNumberMotion.Evaluate(0.05f, 0.05f, crit: false, seed: 3,
                out _, out _, out _, out float noCrit, out _);
            Near(0f, noCrit, "非暴击无抖动");

            DamageNumberMotion.Evaluate(0.05f, 0.05f, crit: true, seed: 3,
                out _, out _, out _, out float inWindow, out _);
            Assert.True(MathF.Abs(inWindow) > 1e-3f, "暴击窗内有位移（相位非零种子）");
            Assert.True(MathF.Abs(inWindow) <= DamageNumberMotion.CritShakeAmplitude, "抖动受幅度约束");

            DamageNumberMotion.Evaluate(DamageNumberMotion.CritShakeSeconds + 0.01f, 0.5f, crit: true, seed: 3,
                out _, out _, out _, out float after, out _);
            Near(0f, after, "抖动窗外归零");
        }

        [Fact]
        public void 轨道_出场缩放_零点起跳_窗终归一_窗后恒一()
        {
            DamageNumberMotion.Evaluate(0f, 0f, false, 0, out _, out _, out _, out _, out float s0);
            Near(0.6f, s0, "出场从 0.6 起跳");

            DamageNumberMotion.Evaluate(DamageNumberMotion.PopSeconds, 0f, false, 0,
                out _, out _, out _, out _, out float s1);
            Near(1f, s1, "出场窗终归一");

            DamageNumberMotion.Evaluate(0.5f, 0f, false, 0, out _, out _, out _, out _, out float after);
            Near(1f, after, "出场窗后恒一");
        }

        [Fact]
        public void 轨道_推挤确定性_同种子同散布_受距离约束与时钟无关()
        {
            DamageNumberMotion.Evaluate(0f, 0f, false, seed: 42, out _, out float p1a, out _, out _, out _);
            DamageNumberMotion.Evaluate(0.7f, 0.3f, false, seed: 42, out _, out float p1b, out _, out _, out _);
            Near(p1b, p1a, "推挤只由种子决定（与时钟无关）");

            DamageNumberMotion.Evaluate(0f, 0f, false, seed: 43, out _, out float p2, out _, out _, out _);
            Assert.True(MathF.Abs(p2) <= DamageNumberMotion.PushDistance, "推挤受距离常量约束");
        }
    }
}
