using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// LText 表数据装载（《UI框架总设计》§9 文本链 `#text.xlsx → 生成 key/语言数据 → LText 服务`）。
    ///
    /// **数据形态**与既有 Luban 表产物一致（`RawFile/Config/*.bytes`，JSON 数组对象）：
    /// <code>[{"key":"UI.Main.Title","zh-CN":"标题","en":"Title"}, ...]</code>
    /// — locale 是**列名**。这是最小可用形态：新增语言 = 加一列，
    /// **但复数规则与 RTL 需代码评估**（§9"不承诺'加列即可零代码支持'"）。
    ///
    /// **经 <see cref="IJsonSerializer"/> 解析**（Core 只认接口，Newtonsoft 留在 Unity 层——
    /// 第三方不进 Core 是本仓既有纪律）。零 IO：字节由装配点的内容租约通道提供，L1 全覆盖。
    /// </summary>
    public static class LocalizationTable
    {
        /// <summary>表数据中保留的列名（非 locale 列）。</summary>
        private static readonly HashSet<string> ReservedColumns =
            new HashSet<string>(StringComparer.Ordinal) { "key", "comment", "note" };

        /// <summary>
        /// 从表数据 JSON 构建目录。返回 null = 数据非法（畸形/无有效行），调用方按缺表处理。
        ///
        /// <paramref name="strictKeyNaming"/> = true 时，key 命名不合 §9 规范即整表拒绝
        /// （候选校验用）；false 时跳过并计数（运行时宽松读取用）。
        /// </summary>
        public static LocalizationCatalog Parse(string json, IJsonSerializer serializer,
            bool strictKeyNaming, out IReadOnlyList<string> problems)
        {
            var issues = new List<string>();
            problems = issues;

            if (string.IsNullOrWhiteSpace(json)) { issues.Add("数据为空"); return null; }
            if (serializer == null) { issues.Add("缺少 JSON 序列化器"); return null; }

            List<Dictionary<string, string>> rows;
            try
            {
                rows = serializer.Deserialize<List<Dictionary<string, string>>>(json);
            }
            catch (Exception ex)
            {
                issues.Add("解析失败：" + ex.Message);
                return null;
            }

            if (rows == null) { issues.Add("解析结果为空"); return null; }

            var catalog = new LocalizationCatalog();
            int accepted = 0;

            foreach (Dictionary<string, string> row in rows)
            {
                if (row == null) { issues.Add("含空行——跳过"); continue; }

                row.TryGetValue("key", out string key);
                if (string.IsNullOrEmpty(key))
                {
                    issues.Add("某行缺 key——跳过");
                    continue;
                }

                string keyProblem = LTextKey.Validate(key);
                if (keyProblem != null)
                {
                    if (strictKeyNaming) { issues.Add($"{key}：{keyProblem}"); return null; }
                    issues.Add($"{key}：{keyProblem}——跳过");
                    continue;
                }

                bool anyLocale = false;
                foreach (KeyValuePair<string, string> cell in row)
                {
                    if (ReservedColumns.Contains(cell.Key)) continue;
                    catalog.Add(key, cell.Key, cell.Value);
                    anyLocale = true;
                }

                if (!anyLocale) issues.Add($"{key}：无任何语言列——跳过");
                else accepted++;
            }

            if (accepted == 0)
            {
                if (issues.Count == 0) issues.Add("无有效行");
                return null;
            }
            return catalog;
        }

        /// <summary>
        /// 覆盖率诊断（§9 本地化覆盖率）：某 locale 相比源语言缺哪些 key。
        /// 发布期据此判断翻译完整性——比"能不能跑"更有意义的发布门禁。
        /// </summary>
        public static IReadOnlyList<string> FindMissingFor(LocalizationCatalog catalog,
            IEnumerable<string> allKeys, string locale)
        {
            var missing = new List<string>();
            if (catalog == null || allKeys == null || string.IsNullOrEmpty(locale)) return missing;

            foreach (string key in allKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                // 回退到源语言也算该 locale 未覆盖
                if (!catalog.TryGet(key, locale, out _, out bool usedFallback) || usedFallback)
                    missing.Add(key);
            }
            return missing;
        }
    }
}
