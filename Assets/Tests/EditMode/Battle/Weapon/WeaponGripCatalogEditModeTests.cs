using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 持枪偏移数据产物（grip.bin）装载契约：真实产物字段/摘要 + 坏输入校验 + 字节往返。
    /// 真实产物缺失时**显性失败**（不 skip）：数据产物必须由烘焙工具箱「持枪偏移」页导出后入库。
    /// </summary>
    public sealed class WeaponGripCatalogEditModeTests
    {
        private static byte[] ReadArtifactOrFail()
        {
            string path = WeaponGripCatalog.DefaultAssetPath;
            Assert.IsTrue(File.Exists(path), "缺 grip.bin——先跑烘焙工具箱「持枪偏移」页导出：" + path);
            return File.ReadAllBytes(path);
        }

        [Test]
        public void 真实产物_装载字段与摘要()
        {
            byte[] bytes = ReadArtifactOrFail();
            WeaponGripCatalog catalog = WeaponGripCatalog.Load(bytes);

            Assert.AreEqual("root/add_weapon_r", catalog.WeaponPath, "武器骨路径（相对 Animator 根，结构契约）");
            StringAssert.EndsWith("hand_r", catalog.FollowPath, "跟随骨＝持枪手 hand_r");
            Assert.Less(catalog.LocalPosition.magnitude, 0.05f,
                "枪骨与持枪手世界位重合（烘焙量级：差 0.6mm）");
            Assert.GreaterOrEqual(catalog.TorsoWeight, 0.99f, "烘焙幅度 = 完全跟随（0..1 内）");
            Assert.LessOrEqual(catalog.TorsoWeight, 1f, "烘焙幅度 = 完全跟随（0..1 内）");

            Assert.AreEqual(64, catalog.Digest.Length, "SHA-256 十六进制长度");
            Assert.AreEqual(Sha256Hex(bytes), catalog.Digest, "Digest 对整段输入字节算 SHA-256");
            Assert.AreEqual(catalog.Digest, WeaponGripCatalog.Load(bytes).Digest, "同字节摘要稳定（幂等装载）");
        }

        [Test]
        public void 坏magic_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            bytes[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(bytes));
        }

        [Test]
        public void 坏版本_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), WeaponGripCatalog.FormatVersion + 1);
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(bytes));
        }

        [Test]
        public void 截断_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            byte[] truncated = new byte[bytes.Length - 3];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(truncated));
        }

        [Test]
        public void 幅度越界_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            WriteFloat(bytes, bytes.Length - 4, 1.5f);
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(bytes));
        }

        [Test]
        public void NaN_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            int localPosOffset = 4 + 4 + (4 + Encoding.UTF8.GetByteCount("root/hand_r"))
                + (4 + Encoding.UTF8.GetByteCount("root/add_weapon_r"));
            WriteFloat(bytes, localPosOffset, float.NaN);
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(bytes));
        }

        [Test]
        public void 尾随字节_抛InvalidDataException()
        {
            byte[] bytes = Minimal();
            byte[] padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, padded, bytes.Length);
            Assert.Throws<InvalidDataException>(() => WeaponGripCatalog.Load(padded));
        }

        [Test]
        public void 字节往返_最小合法数组与Encode逐字节一致()
        {
            const string follow = "root/hand_r";
            const string weapon = "root/add_weapon_r";
            var pos = new Vector3(0.001f, -0.002f, 0.003f);
            var euler = new Vector3(1f, 270f, -2f);
            const float weight = 0.75f;

            byte[] bytes = Build(follow, weapon, pos, euler, weight);
            WeaponGripCatalog catalog = WeaponGripCatalog.Load(bytes);
            Assert.AreEqual(follow, catalog.FollowPath);
            Assert.AreEqual(weapon, catalog.WeaponPath);
            Assert.AreEqual(pos.x, catalog.LocalPosition.x, 0f);
            Assert.AreEqual(pos.y, catalog.LocalPosition.y, 0f);
            Assert.AreEqual(pos.z, catalog.LocalPosition.z, 0f);
            Assert.AreEqual(euler.x, catalog.LocalEuler.x, 0f);
            Assert.AreEqual(euler.y, catalog.LocalEuler.y, 0f);
            Assert.AreEqual(euler.z, catalog.LocalEuler.z, 0f);
            Assert.AreEqual(weight, catalog.TorsoWeight, 0f);

            byte[] encoded = WeaponGripCatalog.Encode(follow, weapon, pos, euler, weight);
            CollectionAssert.AreEqual(bytes, encoded, "写端与测试构造的格式逐字节一致（单一格式定义）");

            byte[] fileBytes = WriteTempAndLoad(encoded);
            Assert.AreEqual(catalog.Digest, WeaponGripCatalog.Load(fileBytes).Digest, "经文件往返摘要一致");
        }

        private static byte[] WriteTempAndLoad(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(), "grip_catalog_roundtrip.bin");
            try
            {
                File.WriteAllBytes(path, bytes);
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static byte[] Minimal()
        {
            return Build("root/hand_r", "root/add_weapon_r",
                new Vector3(0.001f, 0.002f, 0.003f), new Vector3(4f, 5f, 6f), 1f);
        }

        /// <summary>按格式定义构造最小合法字节（测试侧独立实现——格式回归的一道闸）。</summary>
        private static byte[] Build(string followPath, string weaponPath, Vector3 pos, Vector3 euler, float weight)
        {
            var utf8 = new UTF8Encoding(false, true);
            byte[] follow = utf8.GetBytes(followPath);
            byte[] weapon = utf8.GetBytes(weaponPath);
            var bytes = new byte[4 + 4 + (4 + follow.Length) + (4 + weapon.Length) + 7 * 4];
            int offset = 0;
            Encoding.ASCII.GetBytes("GRIP").CopyTo(bytes, offset);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), WeaponGripCatalog.FormatVersion);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), follow.Length);
            offset += 4;
            follow.CopyTo(bytes, offset);
            offset += follow.Length;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), weapon.Length);
            offset += 4;
            weapon.CopyTo(bytes, offset);
            offset += weapon.Length;
            foreach (float value in new[] { pos.x, pos.y, pos.z, euler.x, euler.y, euler.z, weight })
            {
                WriteFloat(bytes, offset, value);
                offset += 4;
            }
            return bytes;
        }

        private static void WriteFloat(byte[] target, int offset, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes);
            }
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
