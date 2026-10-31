using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteGame.Modules.Battle.Arena;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 根位移数据产物（rootmotion.bin）装载契约：真实产物字段/摘要 + 坏输入校验 + 字节往返。
    /// 真实产物缺失时**显性失败**（不 skip）：数据产物必须由烘焙工具箱「根位移」页导出后入库。
    /// </summary>
    public sealed class RootMotionCatalogEditModeTests
    {
        private const int HeaderSize = 4 + 4 + 4 + 4;
        private const int EntryFixedSize = 4 + 4 + 4 + 3 + 4;

        private static byte[] ReadArtifactOrFail()
        {
            string path = RootMotionCatalog.DefaultAssetPath;
            Assert.IsTrue(File.Exists(path), "缺 rootmotion.bin——先跑烘焙工具箱「根位移」页导出：" + path);
            return File.ReadAllBytes(path);
        }

        [Test]
        public void 真实产物_装载字段与摘要()
        {
            byte[] bytes = ReadArtifactOrFail();
            RootMotionCatalog catalog = RootMotionCatalog.Load(bytes);

            Assert.AreEqual(LiteSim.SimConfig.TickRate, catalog.TickRate, "采样率锁定 Sim 逻辑帧率");
            Assert.Greater(catalog.Entries.Count, 0, "条目表不得为空");

            var ids = new System.Collections.Generic.List<int>();
            for (int e = 0; e < catalog.Entries.Count; e++)
            {
                RootMotionEntry entry = catalog.Entries[e];
                ids.Add(entry.ActionId);
                Assert.GreaterOrEqual(entry.SampleCount, 1, "ActionId " + entry.ActionId + "：SampleCount ≥ 1");
                Assert.AreEqual(entry.SampleCount * 2, entry.DeltaLocalXzMm.Length, "平面增量长度 = 2×SampleCount");
                Assert.AreEqual(entry.SampleCount, entry.DeltaYawCentiDeg.Length, "偏航长度 = SampleCount");
                if (e > 0) Assert.Greater(entry.ActionId, catalog.Entries[e - 1].ActionId, "ActionId 严格升序且唯一");
            }

            CollectionAssert.IsNotEmpty(ids);
            Assert.AreEqual(64, catalog.Digest.Length, "SHA-256 十六进制长度");
            Assert.AreEqual(Sha256Hex(bytes), catalog.Digest, "Digest 对整段输入字节算 SHA-256");
            Assert.AreEqual(catalog.Digest, RootMotionCatalog.Load(bytes).Digest, "同字节摘要稳定（幂等装载）");
        }

        [Test]
        public void 坏magic_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            bytes[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
        }

        [Test]
        public void 坏版本_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), RootMotionCatalog.FormatVersion + 1);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
        }

        [Test]
        public void tickRate不符_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), LiteSim.SimConfig.TickRate + 1);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
        }

        [Test]
        public void 截断_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            byte[] truncated = new byte[bytes.Length - 3];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(truncated));
        }

        [Test]
        public void 样本长度不一致_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            int sampleCountOffset = HeaderSize + 4 + 4;      // actionId + motionVersion 之后
            int sampleCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sampleCountOffset, 4));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(sampleCountOffset, 4), sampleCount + 1);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
        }

        [Test]
        public void entryCount超上限_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), RootMotionCatalog.MaxEntries + 1);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(bytes));
        }

        [Test]
        public void ActionId重复_抛InvalidDataException()
        {
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(Build(new[] { 7, 7 })));
        }

        [Test]
        public void ActionId乱序_抛InvalidDataException()
        {
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(Build(new[] { 9, 3 })));
        }

        [Test]
        public void 尾随字节_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            byte[] padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, padded, bytes.Length);
            Assert.Throws<InvalidDataException>(() => RootMotionCatalog.Load(padded));
        }

        [Test]
        public void 字节往返_Encode与测试构造一致且摘要稳定()
        {
            var entries = new[]
            {
                new RootMotionEntry
                {
                    ActionId = 101, MotionVersion = 1, SampleCount = 2,
                    DirectionPolicy = RootMotionDirectionPolicy.StartFacingLocked,
                    MovementPolicy = RootMotionMovementPolicy.Stack,
                    CancelPolicy = RootMotionCancelPolicy.Windowed,
                    YPeakMm = 12,
                    DeltaLocalXzMm = new short[] { 1, -2, 3, -4 },
                    DeltaYawCentiDeg = new short[] { 0, 5 },
                },
                new RootMotionEntry
                {
                    ActionId = 205, MotionVersion = 3, SampleCount = 1,
                    DirectionPolicy = RootMotionDirectionPolicy.FrameByFrame,
                    MovementPolicy = RootMotionMovementPolicy.Limited,
                    CancelPolicy = RootMotionCancelPolicy.NotCancellable,
                    YPeakMm = -7,
                    DeltaLocalXzMm = new short[] { short.MinValue, short.MaxValue },
                    DeltaYawCentiDeg = new short[] { -1 },
                },
            };

            byte[] encoded = RootMotionCatalog.Encode(LiteSim.SimConfig.TickRate, entries);
            RootMotionCatalog catalog = RootMotionCatalog.Load(encoded);
            Assert.AreEqual(2, catalog.Entries.Count);
            Assert.AreEqual(101, catalog.Entries[0].ActionId);
            Assert.AreEqual(RootMotionMovementPolicy.Stack, catalog.Entries[0].MovementPolicy);
            Assert.AreEqual(RootMotionCancelPolicy.Windowed, catalog.Entries[0].CancelPolicy);
            Assert.AreEqual(12, catalog.Entries[0].YPeakMm);
            CollectionAssert.AreEqual(new short[] { 1, -2, 3, -4 }, catalog.Entries[0].DeltaLocalXzMm);
            CollectionAssert.AreEqual(new short[] { 0, 5 }, catalog.Entries[0].DeltaYawCentiDeg);
            Assert.AreEqual(205, catalog.Entries[1].ActionId);
            Assert.AreEqual(RootMotionDirectionPolicy.FrameByFrame, catalog.Entries[1].DirectionPolicy);
            CollectionAssert.AreEqual(new short[] { short.MinValue, short.MaxValue }, catalog.Entries[1].DeltaLocalXzMm);

            Assert.AreEqual((byte)'R', encoded[0], "magic 首字节");
            CollectionAssert.AreEqual(encoded, EncodeIndependent(entries), "写端与独立实现逐字节一致");
            Assert.AreEqual(Sha256Hex(encoded), catalog.Digest, "Digest 对编码字节算 SHA-256");
            Assert.AreEqual(catalog.Digest, RootMotionCatalog.LoadFromFile(WriteTemp(encoded)).Digest,
                "经文件往返摘要一致");
        }

        // ---- 构造/工具（测试侧独立实现——格式回归的一道闸）----

        private static byte[] Minimal() => Build(new[] { 1 });

        /// <summary>按格式定义构造最小合法字节（同 ActionId 列表，每条第 2 个采样）。</summary>
        private static byte[] Build(int[] actionIds)
        {
            var entries = new RootMotionEntry[actionIds.Length];
            for (int e = 0; e < actionIds.Length; e++)
            {
                entries[e] = new RootMotionEntry
                {
                    ActionId = actionIds[e], MotionVersion = 1, SampleCount = 2,
                    YPeakMm = 0,
                    DeltaLocalXzMm = new short[] { 1, 2, 3, 4 },
                    DeltaYawCentiDeg = new short[] { 0, 0 },
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

        private static string WriteTemp(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(), "rootmotion_catalog_roundtrip.bin");
            File.WriteAllBytes(path, bytes);
            return path;
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
