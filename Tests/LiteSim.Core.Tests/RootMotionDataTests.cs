using System.Collections.Generic;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 根位移烘焙产物守卫（《联机动画与根位移专项设计》§3 校验面在 L1 的锚）：
    /// 生成物 RootMotionData.g.cs 由编辑器烘焙器产出（服务端不加载 AnimationClip——两端经同一份
    /// 生成数据消费），此处钉住"结构合法 + 摘要可复算 + 手改必红"。
    /// 摘要与结构在两端/构建/回放场景必须逐字节一致——改片段或配置后重烘并重跑 buildHash 生成器。
    /// </summary>
    public class RootMotionDataTests
    {
        private const int MaxDeltaMm = 2000;   // 与烘焙器同上限（单 tick 2m——超此按非法样本）

        [Fact]
        public void 根位移_摘要复算一致_手改生成物必红()
        {
            Assert.Equal(RootMotionCatalog.BakedContentDigest, RootMotionCatalog.ContentDigest);
        }

        [Fact]
        public void 根位移_条目按ActionId升序且唯一()
        {
            var seen = new HashSet<int>();
            int previous = int.MinValue;
            for (int e = 0; e < RootMotionCatalog.Entries.Length; e++)
            {
                int id = RootMotionCatalog.Entries[e].ActionId;
                Assert.True(id > previous, $"条目 {e}（ActionId {id}）破坏升序——生成器排序规则被绕过");
                Assert.True(seen.Add(id), $"ActionId {id} 重复——重复动作绑定是准入隐患");
                previous = id;
            }
        }

        [Fact]
        public void 根位移_每条结构合法_数组长度与量化上限()
        {
            for (int e = 0; e < RootMotionCatalog.Entries.Length; e++)
            {
                RootMotionEntry entry = RootMotionCatalog.Entries[e];
                Assert.True(entry.SampleCount >= 1, $"ActionId {entry.ActionId}：SampleCount ≥ 1（零样本条目无意义）");
                Assert.Equal(entry.SampleCount * 2, entry.DeltaLocalXzMm.Length);
                Assert.Equal(entry.SampleCount, entry.DeltaYawCentiDeg.Length);

                for (int i = 0; i < entry.SampleCount; i++)
                {
                    Assert.True(entry.DeltaLocalXzMm[2 * i] >= -MaxDeltaMm && entry.DeltaLocalXzMm[2 * i] <= MaxDeltaMm,
                        $"ActionId {entry.ActionId} tick {i}：X 增量超上限");
                    Assert.True(entry.DeltaLocalXzMm[2 * i + 1] >= -MaxDeltaMm && entry.DeltaLocalXzMm[2 * i + 1] <= MaxDeltaMm,
                        $"ActionId {entry.ActionId} tick {i}：Z 增量超上限");
                }
            }
        }

        [Fact]
        public void 根位移_采样率锁定60Hz()
        {
            Assert.Equal(SimConfig.TickRate, RootMotionCatalog.TickRate);
            Assert.Equal(60, RootMotionCatalog.TickRate);
        }

        [Fact]
        public void 根位移_摘要文本变化必变_同文本必同()
        {
            // 两份不同样本 → 摘要必不同；同文本 → 必相同（复算口径的确定性锚）
            var a = new RootMotionEntry[]
            {
                new RootMotionEntry { ActionId = 1, MotionVersion = 1, SampleCount = 1, YPeakMm = 0,
                    DeltaLocalXzMm = new short[] { 10, 0 }, DeltaYawCentiDeg = new short[] { 0 } },
            };
            var b = new RootMotionEntry[]
            {
                new RootMotionEntry { ActionId = 1, MotionVersion = 1, SampleCount = 1, YPeakMm = 0,
                    DeltaLocalXzMm = new short[] { 11, 0 }, DeltaYawCentiDeg = new short[] { 0 } },
            };
            string textA = RootMotionDigest.CanonicalText(a);
            Assert.Equal(RootMotionDigest.Compute(textA), RootMotionDigest.Compute(RootMotionDigest.CanonicalText(a)));
            Assert.NotEqual(RootMotionDigest.Compute(textA), RootMotionDigest.Compute(RootMotionDigest.CanonicalText(b)));
        }
    }
}
