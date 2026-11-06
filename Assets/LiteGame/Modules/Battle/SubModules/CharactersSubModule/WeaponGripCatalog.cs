using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 持枪偏移数据产物（`grip.bin`）的只读装载结果：武器骨相对持枪手（挂点父级）在参考持枪姿态下的 局部位姿——换弹上半身叠加期姿态覆写的**数据单源**（消费见 WeaponGripConstraint/GripT。）。
    /// </summary>
    public sealed class WeaponGripCatalog
    {
        /// <summary>运行时资源路径（内容包内；编辑器烘焙落点 <c>BakeTargets.GripBinOutput</c> 与之同串）。</summary>
        public const string DefaultAssetPath = "Assets/GameData/Config/runtime/grip.bin";

        /// <summary>当前格式版本（装载器只接受本版本）。</summary>
        public const int FormatVersion = 1;

        private const string MagicText = "GRIP";
        private const int MagicSize = 4;

        private readonly string _followPath;
        private readonly string _weaponPath;
        private readonly Vector3 _localPosition;
        private readonly Vector3 _localEuler;
        private readonly float _torsoWeight;
        private readonly string _digest;

        private WeaponGripCatalog(string followPath, string weaponPath, Vector3 localPosition,
            Vector3 localEuler, float torsoWeight, string digest)
        {
            _followPath = followPath;
            _weaponPath = weaponPath;
            _localPosition = localPosition;
            _localEuler = localEuler;
            _torsoWeight = torsoWeight;
            _digest = digest;
        }

        /// <summary>跟随骨路径（相对 Animator 根）：持枪手。</summary>
        public string FollowPath => _followPath;

        /// <summary>武器骨路径（相对 Animator 根；枪模型是它的子节点）。</summary>
        public string WeaponPath => _weaponPath;

        /// <summary>枪骨相对跟随骨的局部位置（持枪位偏移，参考姿态烘焙）。</summary>
        public Vector3 LocalPosition => _localPosition;

        /// <summary>枪骨相对跟随骨的局部欧拉角（度；消费侧 Quaternion.Euler）。</summary>
        public Vector3 LocalEuler => _localEuler;

        /// <summary>幅度缩放：0 = 不约束（用片段曲线）、1 = 完全跟随持枪手。</summary>
        public float TorsoWeight => _torsoWeight;

        /// <summary>整段输入字节的 SHA-256（小写十六进制，64 字符）——发布/诊断对账用。</summary>
        public string Digest => _digest;

        /// <summary>从原生字节装载并校验；失败抛 <see cref="InvalidDataException"/>。</summary>
        public static WeaponGripCatalog Load(ReadOnlySpan<byte> bytes)
        {
            int offset = 0;
            Require(bytes, offset, MagicSize, "magic");
            byte[] magic = Encoding.ASCII.GetBytes(MagicText);
            for (int i = 0; i < MagicSize; i++)
            {
                if (bytes[offset + i] != magic[i])
                    throw new InvalidDataException("持枪偏移产物 magic 不匹配（期望 " + MagicText + "）");
            }
            offset += MagicSize;

            Require(bytes, offset, 4, "formatVersion");
            int version = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
            offset += 4;
            if (version != FormatVersion)
                throw new InvalidDataException("持枪偏移产物格式版本不支持：" + version + "（期望 " + FormatVersion + "）");

            string followPath = ReadString(bytes, ref offset, "followPath");
            string weaponPath = ReadString(bytes, ref offset, "weaponPath");

            float posX = ReadFloat(bytes, ref offset, "localPos.x");
            float posY = ReadFloat(bytes, ref offset, "localPos.y");
            float posZ = ReadFloat(bytes, ref offset, "localPos.z");
            float eulerX = ReadFloat(bytes, ref offset, "localEuler.x");
            float eulerY = ReadFloat(bytes, ref offset, "localEuler.y");
            float eulerZ = ReadFloat(bytes, ref offset, "localEuler.z");
            float torsoWeight = ReadFloat(bytes, ref offset, "torsoWeight");
            if (torsoWeight < 0f || torsoWeight > 1f)
                throw new InvalidDataException("持枪偏移产物 torsoWeight 越界：" + torsoWeight + "（要求 0..1）");
            if (offset != bytes.Length)
                throw new InvalidDataException("持枪偏移产物存在尾随字节：" + (bytes.Length - offset));

            return new WeaponGripCatalog(followPath, weaponPath,
                new Vector3(posX, posY, posZ), new Vector3(eulerX, eulerY, eulerZ), torsoWeight,
                ComputeDigest(bytes));
        }

        /// <summary>编辑器/测试用文件装载（内部 File.ReadAllBytes + <see cref="Load"/>）。</summary>
        public static WeaponGripCatalog LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("产物路径不能为空", nameof(path));
            return Load(File.ReadAllBytes(path));
        }

        /// <summary>按同一格式序列化（烘焙器/测试用；参数非法抛 <see cref="ArgumentException"/>）。</summary>
        public static byte[] Encode(string followPath, string weaponPath, Vector3 localPosition,
            Vector3 localEuler, float torsoWeight)
        {
            if (string.IsNullOrEmpty(followPath)) throw new ArgumentException("跟随骨路径不能为空", nameof(followPath));
            if (string.IsNullOrEmpty(weaponPath)) throw new ArgumentException("武器骨路径不能为空", nameof(weaponPath));
            if (!IsFinite(torsoWeight) || torsoWeight < 0f || torsoWeight > 1f)
                throw new ArgumentOutOfRangeException(nameof(torsoWeight), torsoWeight, "持枪幅度须在 0..1");
            RequireFinite(localPosition, nameof(localPosition));
            RequireFinite(localEuler, nameof(localEuler));

            var utf8 = new UTF8Encoding(false, true);
            byte[] followBytes = utf8.GetBytes(followPath);
            byte[] weaponBytes = utf8.GetBytes(weaponPath);
            int size = MagicSize + 4 + (4 + followBytes.Length) + (4 + weaponBytes.Length) + 7 * 4;
            var result = new byte[size];
            int offset = 0;
            Encoding.ASCII.GetBytes(MagicText, 0, MagicSize, result, 0);
            offset += MagicSize;
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), FormatVersion);
            offset += 4;
            WriteBytes(result, ref offset, followBytes);
            WriteBytes(result, ref offset, weaponBytes);
            WriteFloat(result, ref offset, localPosition.x);
            WriteFloat(result, ref offset, localPosition.y);
            WriteFloat(result, ref offset, localPosition.z);
            WriteFloat(result, ref offset, localEuler.x);
            WriteFloat(result, ref offset, localEuler.y);
            WriteFloat(result, ref offset, localEuler.z);
            WriteFloat(result, ref offset, torsoWeight);
            return result;
        }

        private static void Require(ReadOnlySpan<byte> bytes, int offset, int count, string field)
        {
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new InvalidDataException("持枪偏移产物截断：缺 " + field + "（偏移 " + offset + "，长度 " + bytes.Length + "）");
        }

        private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset, string field)
        {
            Require(bytes, offset, 4, field + ".length");
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
            offset += 4;
            if (length <= 0)
                throw new InvalidDataException("持枪偏移产物 " + field + " 长度非法：" + length);
            Require(bytes, offset, length, field);
            string value;
            try
            {
                value = new UTF8Encoding(false, true).GetString(bytes.Slice(offset, length));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("持枪偏移产物 " + field + " 不是合法 UTF8", ex);
            }
            offset += length;
            return value;
        }

        private static float ReadFloat(ReadOnlySpan<byte> bytes, ref int offset, string field)
        {
            Require(bytes, offset, 4, field);
            float value = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4)));
            offset += 4;
            if (!IsFinite(value))
                throw new InvalidDataException("持枪偏移产物 " + field + " 非有限值：" + value);
            return value;
        }

        private static void WriteBytes(byte[] target, ref int offset, byte[] value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value.Length);
            offset += 4;
            value.AsSpan().CopyTo(target.AsSpan(offset, value.Length));
            offset += value.Length;
        }

        private static void WriteFloat(byte[] target, ref int offset, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
            offset += 4;
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

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void RequireFinite(Vector3 value, string name)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                throw new ArgumentOutOfRangeException(name, value, "持枪偏移不得含 NaN/Infinity");
        }
    }
}
