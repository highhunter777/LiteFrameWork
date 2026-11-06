using System;
using System.Globalization;

namespace LiteFramework
{
    /// <summary>
    /// CDN 通道部署配置（《热更与内容发布专项设计》§7 部署配置契约）。
    /// </summary>
    public sealed class ContentCdnConfig
    {
        /// <summary>CDN 基址（末尾不带 /）。空 = 未配置 → 本地信封通道。</summary>
        public string BaseUrl = "";

        /// <summary>候选信封相对基址的路径（信封是"当前候选是什么"的发布指针，不随 ReleaseId 变）。</summary>
        public string OfferPath = "candidate.json";

        /// <summary>单请求超时（秒）。</summary>
        public int TimeoutSeconds = 30;

        /// <summary>是否配置了 CDN 通道。</summary>
        public bool HasCdn => !string.IsNullOrEmpty(BaseUrl);
    }

    /// <summary>
    /// 部署配置装载（命令行 → <see cref="ContentCdnConfig"/>）。
    /// </summary>
    public static class ContentDeployConfig
    {
        /// <summary>参数键（剥掉前导 - 后匹配；大小写不敏感）。</summary>
        public const string KeyBaseUrl = "content.cdnUrl";
        public const string KeyOfferPath = "content.cdnOfferPath";
        public const string KeyTimeoutSeconds = "content.cdnTimeout";

        /// <summary>Unity 入口：读进程命令行（Editor/Player 同源）。</summary>
        public static ContentCdnConfig Load() => Parse(Environment.GetCommandLineArgs());

        /// <summary>
        /// 三源装载：命令行 > 包内烙入（<paramref name="bakedArgsText"/> = deploy.args 全文； null/空白 = 无烙入文件，退化为纯命令行装载）。
        /// </summary>
        public static ContentCdnConfig Load(string[] args, string bakedArgsText)
        {
            string[] baked = Tokenize(bakedArgsText);
            if (baked.Length == 0) return Parse(args);
            if (args == null || args.Length == 0) return Parse(baked);
            var merged = new string[baked.Length + args.Length];
            baked.CopyTo(merged, 0);
            args.CopyTo(merged, baked.Length);
            return Parse(merged);
        }

        /// <summary>烙入文本 → 参数 token 流：按空白切（一行一个 <c>-key=value</c>，也兼容
        /// 空白分隔的单行多 token 流——与命令行同形）；文本为空 = 空流。</summary>
        public static string[] Tokenize(string bakedArgsText)
        {
            if (string.IsNullOrWhiteSpace(bakedArgsText)) return Array.Empty<string>();
            return bakedArgsText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>解析参数数组（纯函数；null/空数组 = 全默认）。非法值静默保持默认——
        /// 部署参数不是攻击面（验签兜底），但也不让它让启动失败。</summary>
        public static ContentCdnConfig Parse(string[] args)
        {
            var config = new ContentCdnConfig();
            if (args == null) return config;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg) || arg[0] != '-') continue;

                // 形态一：-key=value（同 token 内拆）；形态二：-key value（取下一 token）。
                string key = arg.TrimStart('-');
                string value = null;
                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    value = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    value = args[++i];
                }

                if (string.IsNullOrEmpty(value)) continue;   // 显式空值 = 保持默认（非法值不让启动失败）

                if (string.Equals(key, KeyBaseUrl, StringComparison.OrdinalIgnoreCase))
                    config.BaseUrl = value.TrimEnd('/');
                else if (string.Equals(key, KeyOfferPath, StringComparison.OrdinalIgnoreCase))
                    config.OfferPath = value.TrimStart('/');
                else if (string.Equals(key, KeyTimeoutSeconds, StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                         && seconds >= 1)
                    config.TimeoutSeconds = seconds;
            }

            return config;
        }
    }
}
