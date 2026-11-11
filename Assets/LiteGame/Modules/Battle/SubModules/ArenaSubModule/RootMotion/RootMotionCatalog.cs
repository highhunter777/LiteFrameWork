using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteSim;

namespace LiteGame.Modules.Battle.SubModules.ArenaSubModule.RootMotion
{
    /// <summary>
    /// 根位移数据产物（`rootmotion.bin`）的只读装载结果：逐动作的烘焙位移增量（契约见 RootMotionEntry）。
    /// </summary>
    public sealed class RootMotionCatalog
    {
        /// <summary>运行时资源路径（内容包内必备件；编辑器烘焙落点 <c>BakeTargets.RootMotionBinOutput</c> 与之同串）。</summary>
        public const string DefaultAssetPath = "Assets/GameData/Config/runtime/rootmotion.bin";

        /// <summary>当前格式版本（装载器只接受本版本）。</summary>
        public const int FormatVersion = 1;

        /// <summary>条目数上限（防炸：正常内容为个位数量级，4096 已远超任何动作表）。</summary>
        public const int MaxEntries = 4096;

        private const string MagicText = "RMO1";
        private const int MagicSize = 4;
        private const int HeaderSize = MagicSize + 4 + 4 + 4;          // magic + version + tickRate + entryCount

        /// <summary>条目定长部分：actionId/motionVersion/sampleCount + 三策略（各 1 字节）+ yPeakMm。</summary>
        private const int EntryFixedSize = 4 + 4 + 4 + 3 + 4;

        /// <summary>每采样步字节数：X/Z 两个 int16 ＋ yaw 一个 int16。</summary>
        private const int BytesPerSample = 2 * 2 + 2;

        private readonly RootMotionEntry[] _entries;
        private readonly int _tickRate;
        private readonly string _digest;

        private RootMotionCatalog(RootMotionEntry[] entries, int tickRate, string digest)
        {
            _entries = entries;
            _tickRate = tickRate;
            _digest = digest;
        }

        /// <summary>条目表（按 ActionId 升序；只读视图——装载结果不再变更）。</summary>
        public IReadOnlyList<RootMotionEntry> Entries => _entries;

        /// <summary>产物声明的采样率（装载期已断言等于 <see cref="SimConfig.TickRate"/>）。</summary>
        public int TickRate => _tickRate;

        /// <summary>整段输入字节的 SHA-256（小写十六进制，64 字符）——对局准入/发布对账用。</summary>
        public string Digest => _digest;

        /// <summary>从原生字节装载并校验；失败抛 <see cref="InvalidDataException"/>。</summary>
        public static RootMotionCatalog Load(ReadOnlySpan<byte> bytes)
        {
            Require(bytes, 0, HeaderSize, "头部");
            for (int i = 0; i < MagicSize; i++)
            {
                if (bytes[i] != (byte)MagicText[i])
                    throw new InvalidDataException("根位移产物 magic 不匹配（期望 " + MagicText + "）");
            }

            int offset = MagicSize;
            int version = ReadInt32(bytes, ref offset, "formatVersion");
            if (version != FormatVersion)
                throw new InvalidDataException("根位移产物格式版本不支持：" + version + "（期望 " + FormatVersion + "）");

            int tickRate = ReadInt32(bytes, ref offset, "tickRate");
            if (tickRate != SimConfig.TickRate)
                throw new InvalidDataException("根位移产物 tickRate=" + tickRate
                    + " 与 Sim 逻辑帧率 " + SimConfig.TickRate + " 不一致");

            int entryCount = ReadInt32(bytes, ref offset, "entryCount");
            if (entryCount <= 0 || entryCount > MaxEntries)
                throw new InvalidDataException("根位移产物 entryCount 非法：" + entryCount
                    + "（要求 1.." + MaxEntries + "）");

            var entries = new RootMotionEntry[entryCount];
            int previousActionId = int.MinValue;
            for (int e = 0; e < entryCount; e++)
            {
                int actionId = ReadInt32(bytes, ref offset, "actionId");
                if (actionId <= previousActionId)
                    throw new InvalidDataException("根位移产物 actionId 必须严格升序且唯一："
                        + actionId + " 出现在 " + previousActionId + " 之后");
                previousActionId = actionId;

                int motionVersion = ReadInt32(bytes, ref offset, "motionVersion");
                int sampleCount = ReadInt32(bytes, ref offset, "sampleCount");
                if (sampleCount <= 0)
                    throw new InvalidDataException("根位移产物 ActionId " + actionId + " 的 sampleCount 非法：" + sampleCount);

                var direction = (RootMotionDirectionPolicy)ReadPolicyByte(bytes, ref offset, "directionPolicy", 2);
                var movement = (RootMotionMovementPolicy)ReadPolicyByte(bytes, ref offset, "movementPolicy", 2);
                var cancel = (RootMotionCancelPolicy)ReadPolicyByte(bytes, ref offset, "cancelPolicy", 1);
                int yPeakMm = ReadInt32(bytes, ref offset, "yPeakMm");

                Require(bytes, offset, (long)sampleCount * BytesPerSample,
                    "ActionId " + actionId + " 的 " + sampleCount + " 个样本");
                var dxz = new short[sampleCount * 2];
                for (int i = 0; i < dxz.Length; i++)
                    dxz[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset + i * 2, 2));
                offset += dxz.Length * 2;
                var dyaw = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                    dyaw[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset + i * 2, 2));
                offset += sampleCount * 2;

                entries[e] = new RootMotionEntry
                {
                    ActionId = actionId,
                    MotionVersion = motionVersion,
                    SampleCount = sampleCount,
                    DirectionPolicy = direction,
                    MovementPolicy = movement,
                    CancelPolicy = cancel,
                    YPeakMm = yPeakMm,
                    DeltaLocalXzMm = dxz,
                    DeltaYawCentiDeg = dyaw,
                };
            }

            if (offset != bytes.Length)
                throw new InvalidDataException("根位移产物存在尾随字节：" + (bytes.Length - offset));

            return new RootMotionCatalog(entries, tickRate, ComputeDigest(bytes));
        }

        /// <summary>编辑器/测试用文件装载（内部 File.ReadAllBytes + <see cref="Load"/>）。</summary>
        public static RootMotionCatalog LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("产物路径不能为空", nameof(path));
            return Load(File.ReadAllBytes(path));
        }

        /// <summary>按同一格式序列化（烘焙器/测试用）：条目须合法（非空、ActionId 严格升序、
        /// SampleCount ≥ 1、增量数组长度与 SampleCount 一致、策略在已定义范围内）。
        /// 参数非法抛 <see cref="ArgumentException"/>（写端错误不是数据损坏，不与装载期
        /// <see cref="InvalidDataException"/> 混用）。</summary>
        public static byte[] Encode(int tickRate, IReadOnlyList<RootMotionEntry> entries)
        {
            if (tickRate != SimConfig.TickRate)
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate,
                    "根位移产物采样率必须等于 Sim 逻辑帧率 " + SimConfig.TickRate);
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Count <= 0 || entries.Count > MaxEntries)
                throw new ArgumentException("根位移条目数须在 1.." + MaxEntries + "：" + entries.Count, nameof(entries));

            long size = HeaderSize;
            int previousActionId = int.MinValue;
            for (int e = 0; e < entries.Count; e++)
            {
                RootMotionEntry entry = entries[e];
                if (entry == null) throw new ArgumentException("条目 " + e + " 为 null", nameof(entries));
                if (entry.ActionId <= previousActionId)
                    throw new ArgumentException("条目 " + e + " 的 ActionId 未严格升序（" + entry.ActionId + "）", nameof(entries));
                previousActionId = entry.ActionId;
                if (entry.SampleCount <= 0)
                    throw new ArgumentException("ActionId " + entry.ActionId + " 的 SampleCount 非法：" + entry.SampleCount, nameof(entries));
                if (entry.DeltaLocalXzMm == null || entry.DeltaLocalXzMm.Length != entry.SampleCount * 2)
                    throw new ArgumentException("ActionId " + entry.ActionId + " 的平面增量长度须为 2×SampleCount", nameof(entries));
                if (entry.DeltaYawCentiDeg == null || entry.DeltaYawCentiDeg.Length != entry.SampleCount)
                    throw new ArgumentException("ActionId " + entry.ActionId + " 的偏航数组长度须为 SampleCount", nameof(entries));
                if ((byte)entry.DirectionPolicy > 2 || (byte)entry.MovementPolicy > 2 || (byte)entry.CancelPolicy > 1)
                    throw new ArgumentException("ActionId " + entry.ActionId + " 的策略值超出已定义范围", nameof(entries));
                size += EntryFixedSize + (long)entry.SampleCount * BytesPerSample;
            }

            if (size > int.MaxValue)
                throw new ArgumentException("根位移产物过大：" + size + " 字节", nameof(entries));

            var result = new byte[(int)size];
            Encoding.ASCII.GetBytes(MagicText, 0, MagicSize, result, 0);
            int offset = MagicSize;
            WriteInt32(result, ref offset, FormatVersion);
            WriteInt32(result, ref offset, tickRate);
            WriteInt32(result, ref offset, entries.Count);

            for (int e = 0; e < entries.Count; e++)
            {
                RootMotionEntry entry = entries[e];
                WriteInt32(result, ref offset, entry.ActionId);
                WriteInt32(result, ref offset, entry.MotionVersion);
                WriteInt32(result, ref offset, entry.SampleCount);
                result[offset++] = (byte)entry.DirectionPolicy;
                result[offset++] = (byte)entry.MovementPolicy;
                result[offset++] = (byte)entry.CancelPolicy;
                WriteInt32(result, ref offset, entry.YPeakMm);
                for (int i = 0; i < entry.DeltaLocalXzMm.Length; i++)
                    WriteInt16(result, ref offset, entry.DeltaLocalXzMm[i]);
                for (int i = 0; i < entry.DeltaYawCentiDeg.Length; i++)
                    WriteInt16(result, ref offset, entry.DeltaYawCentiDeg[i]);
            }

            return result;
        }

        private static void Require(ReadOnlySpan<byte> bytes, int offset, long count, string field)
        {
            if (offset < 0 || count < 0 || count > bytes.Length - offset)
                throw new InvalidDataException("根位移产物截断：缺 " + field
                    + "（偏移 " + offset + "，长度 " + bytes.Length + "）");
        }

        private static int ReadInt32(ReadOnlySpan<byte> bytes, ref int offset, string field)
        {
            Require(bytes, offset, 4, field);
            int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static byte ReadPolicyByte(ReadOnlySpan<byte> bytes, ref int offset, string field, byte maxInclusive)
        {
            Require(bytes, offset, 1, field);
            byte value = bytes[offset++];
            if (value > maxInclusive)
                throw new InvalidDataException("根位移产物 " + field + "=" + value
                    + " 超出已定义范围 0.." + maxInclusive);
            return value;
        }

        private static void WriteInt32(byte[] target, ref int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
            offset += 4;
        }

        private static void WriteInt16(byte[] target, ref int offset, short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(offset, 2), value);
            offset += 2;
        }

        private static string ComputeDigest(ReadOnlySpan<byte> bytes)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes.ToArray());
            }
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
