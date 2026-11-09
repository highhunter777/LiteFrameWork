using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LiteSim
{
    /// <summary>
    /// 战斗/玩法配置规范化摘要（《商业级通用服务端框架总设计》§5 P0-5：协议字段 configHash 的**唯一合法来源**—— SHA-256；禁止运行时哈希（GetHashCode 有进程随机种子。））。
    /// </summary>
    public static class CombatConfigDigest
    {
        /// <summary>基摘要方案标签（规范文本首行——方案/覆盖面变更即摘要值变更，两端同批发布）。</summary>
        public const string BaseSchemeTag = "gameplay-digest-v1";

        /// <summary>终值方案标签（本局规则位段首行）。</summary>
        public const string RulesSchemeTag = "gameplay-rules-v1";

        /// <summary>判定面表的一段字节输入（两端各自采集：客户端从装载候选缓存取、服务端从盘上读——
        /// 路径与顺序的**单源**是 <c>TableLayout.JudgmentFiles</c>，禁第二份清单）。</summary>
        public readonly struct Entry
        {
            /// <summary>表相对路径（= 判定面清单条目，如 <c>Combat/tbweapon</c>）。</summary>
            public readonly string Path;

            /// <summary>该表 .bytes 原始字节。</summary>
            public readonly byte[] Bytes;

            public Entry(string path, byte[] bytes)
            {
                if (string.IsNullOrEmpty(path)) throw new ArgumentException("表路径不得为空", nameof(path));
                Path = path;
                Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            }
        }

        /// <summary>基摘要（判定面表字节全量）：两端装载同一份 .bytes → 同值必同摘要。</summary>
        public static uint ComputeBase(IReadOnlyList<Entry> tables) => Compute(CanonicalBaseText(tables));

        /// <summary>基摘要规范文本（诊断/测试——同时是摘要算法的唯一输入口径）：
        /// 标签行 + 逐表 "相对路径 空格 表字节 SHA-256 全量 hex"。空表集=合法形态（无表身份形态），
        /// 但生产两端必经装载链（客户端 Preload / 服务端 ServerTableLoader）注入真表集。</summary>
        public static string CanonicalBaseText(IReadOnlyList<Entry> tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var sb = new StringBuilder(64 + tables.Count * 80);
            sb.Append(BaseSchemeTag).Append('\n');
            for (int i = 0; i < tables.Count; i++)
            {
                Entry entry = tables[i];
                sb.Append(entry.Path).Append(' ').Append(ContentHash(entry.Bytes)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>终值（本局身份）：基摘要 + 测试规则初值。房间创建（服务端）与本地复算（客户端）
        /// 各自用**将实际生效的规则位**调用——不等即"这局判据不同源"。</summary>
        public static uint WithTestRules(uint baseDigest, bool noDeath, bool infiniteAmmo)
            => Compute(CanonicalRulesText(baseDigest, noDeath, infiniteAmmo));

        /// <summary>终值规范文本（诊断/测试）：标签行 + 基摘要 8 位 hex + 两行规则位。</summary>
        public static string CanonicalRulesText(uint baseDigest, bool noDeath, bool infiniteAmmo)
        {
            var sb = new StringBuilder(64);
            sb.Append(RulesSchemeTag).Append('\n')
              .Append(baseDigest.ToString("x8", CultureInfo.InvariantCulture)).Append('\n')
              .Append(noDeath ? 1 : 0).Append('\n')
              .Append(infiniteAmmo ? 1 : 0);
            return sb.ToString();
        }

        /// <summary>单表内容摘要（SHA-256 全量 hex）——进规范文本的每表行。</summary>
        public static string ContentHash(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(64);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>给定规范化文本的摘要（文本级入口——测试/工具：两份不同配置文本必得不同摘要，
        /// 无需构造表字节）。</summary>
        public static uint Compute(string canonicalText)
        {
            if (canonicalText == null) throw new ArgumentNullException(nameof(canonicalText));
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalText);
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToUInt32(hash, 0);
        }
    }
}
