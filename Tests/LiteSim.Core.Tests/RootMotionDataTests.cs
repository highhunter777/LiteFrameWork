using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteGame.Modules.Battle.Arena;
using Xunit;

namespace LiteGame.Tests
{
    /// <summary>
    /// 根位移数据产物（rootmotion.bin）的 L1 装载契约：格式解析/校验/摘要与编码往返。
    /// </summary>
    public class RootMotionDataTests
    {
        private const int HeaderSize = 4 + 4 + 4 + 4;
        private const int EntryFixedSize = 4 + 4 + 4 + 3 + 4;

        [Fact]
        public void 最小产物_装载字段与整段字节摘要()
        {
            byte[] bytes = Build(new[] { 101 }, sampleCount: 2);
            RootMotionCatalog catalog = RootMotionCatalog.Load(bytes);

            Assert.Equal(LiteSim.SimConfig.TickRate, catalog.TickRate);
            Assert.Equal(1, catalog.Entries.Count);
            RootMotionEntry entry = catalog.Entries[0];
            Assert.Equal(101, entry.ActionId);
            Assert.Equal(1, entry.MotionVersion);
            Assert.Equal(2, entry.SampleCount);
            Assert.Equal(RootMotionDirectionPolicy.StartFacingLocked, entry.DirectionPolicy);
            Assert.Equal(3, entry.YPeakMm);
            Assert.Equal(new short[] { 1, 2, 3, 4 }, entry.DeltaLocalXzMm);
            Assert.Equal(new short[] { 0, 5 }, entry.DeltaYawCentiDeg);

            Assert.Equal(64, catalog.Digest.Length);
            Assert.Equal(Sha256Hex(bytes), catalog.Digest);
            Assert.Equal(catalog.Digest, RootMotionCatalog.Load(bytes).Digest);
        }

        [Fact]
        public void 坏输入_统一抛InvalidDataException()
        {
            AssertBad(bytes => bytes[0] = (byte)'X');
            AssertBad(bytes => BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(4, 4), RootMotionCatalog.FormatVersion + 1));
            AssertBad(bytes => BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(8, 4), LiteSim.SimConfig.TickRate + 1));
            AssertBad(bytes => BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(12, 4), RootMotionCatalog.MaxEntries + 1));
            AssertBad(bytes => BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(HeaderSize + 8, 4),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(HeaderSize + 8, 4)) + 1));   // sampleCount 越界

            byte[] full = Build(new[] { 101 }, 2);
            byte[] truncated = new byte[full.Length - 1];
            Array.Copy(full, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(truncated));
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(Build(new[] { 7, 7 }, 2)));
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(Build(new[] { 9, 3 }, 2)));

            static void AssertBad(Action<byte[]> mutate)
            {
                byte[] bytes = Build(new[] { 101 }, 2);
                mutate(bytes);
                Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
            }
        }

        [Fact]
        public void 编码往返_与独立实现逐字节一致()
        {
            var entries = new[]
            {
                new RootMotionEntry
                {
                    ActionId = 7, MotionVersion = 2, SampleCount = 2,
                    DirectionPolicy = RootMotionDirectionPolicy.FrameByFrame,
                    MovementPolicy = RootMotionMovementPolicy.Limited,
                    CancelPolicy = RootMotionCancelPolicy.Windowed,
                    YPeakMm = -4,
                    DeltaLocalXzMm = new short[] { -1, 2, -3, 4 },
                    DeltaYawCentiDeg = new short[] { 5, -6 },
                },
            };
            byte[] encoded = RootMotionCatalog.Encode(LiteSim.SimConfig.TickRate, entries);
            Assert.Equal(EncodeIndependent(entries), encoded);

            RootMotionCatalog catalog = RootMotionCatalog.Load(encoded);
            Assert.Equal(7, catalog.Entries[0].ActionId);
            Assert.Equal(RootMotionMovementPolicy.Limited, catalog.Entries[0].MovementPolicy);
            Assert.Equal(new short[] { 5, -6 }, catalog.Entries[0].DeltaYawCentiDeg);
        }

        [Fact]
        public void 规范化摘要_同样本同摘要_异样本异摘要()
        {
            var a = new[]
            {
                new RootMotionEntry { ActionId = 1, MotionVersion = 1, SampleCount = 1, YPeakMm = 0,
                    DeltaLocalXzMm = new short[] { 10, 0 }, DeltaYawCentiDeg = new short[] { 0 } },
            };
            var b = new[]
            {
                new RootMotionEntry { ActionId = 1, MotionVersion = 1, SampleCount = 1, YPeakMm = 0,
                    DeltaLocalXzMm = new short[] { 11, 0 }, DeltaYawCentiDeg = new short[] { 0 } },
            };
            string textA = RootMotionDigest.CanonicalText(a, LiteSim.SimConfig.TickRate);
            Assert.Equal(RootMotionDigest.Compute(textA),
                RootMotionDigest.Compute(RootMotionDigest.CanonicalText(a, LiteSim.SimConfig.TickRate)));
            Assert.NotEqual(RootMotionDigest.Compute(textA),
                RootMotionDigest.Compute(RootMotionDigest.CanonicalText(b, LiteSim.SimConfig.TickRate)));
        }

        // ---- 测试侧独立格式实现（格式回归的一道闸）----

        private static byte[] Build(int[] actionIds, int sampleCount)
        {
            var entries = new RootMotionEntry[actionIds.Length];
            for (int e = 0; e < actionIds.Length; e++)
            {
                var dxz = new short[sampleCount * 2];
                var dyaw = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    dxz[2 * i] = (short)(2 * i + 1);
                    dxz[2 * i + 1] = (short)(2 * i + 2);
                    dyaw[i] = sampleCount == 2 && i == 1 ? (short)5 : (short)0;
                }
                entries[e] = new RootMotionEntry
                {
                    ActionId = actionIds[e], MotionVersion = 1, SampleCount = sampleCount, YPeakMm = 3,
                    DeltaLocalXzMm = dxz, DeltaYawCentiDeg = dyaw,
                };
            }
            return EncodeIndependent(entries);
        }

        private static byte[] EncodeIndependent(RootMotionEntry[] entries)
        {
            int size = HeaderSize;
            foreach (RootMotionEntry entry in entries) size += EntryFixedSize + entry.SampleCount * 6;
            var bytes = new byte[size];
            Encoding.ASCII.GetBytes("RMO1").CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), RootMotionCatalog.FormatVersion);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), LiteSim.SimConfig.TickRate);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), entries.Length);
            int offset = HeaderSize;
            foreach (RootMotionEntry entry in entries)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), entry.ActionId); offset += 4;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), entry.MotionVersion); offset += 4;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), entry.SampleCount); offset += 4;
                bytes[offset++] = (byte)entry.DirectionPolicy;
                bytes[offset++] = (byte)entry.MovementPolicy;
                bytes[offset++] = (byte)entry.CancelPolicy;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), entry.YPeakMm); offset += 4;
                for (int i = 0; i < entry.SampleCount; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), entry.DeltaLocalXzMm[2 * i]);
                    offset += 2;
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), entry.DeltaLocalXzMm[2 * i + 1]);
                    offset += 2;
                }
                for (int i = 0; i < entry.SampleCount; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), entry.DeltaYawCentiDeg[i]);
                    offset += 2;
                }
            }
            return bytes;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
