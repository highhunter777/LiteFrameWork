using System;
using System.Globalization;

namespace LiteClient
{
    /// <summary>
    /// CDN 通道部署配置（《热更与内容发布专项设计》§7 部署配置契约）。
    ///
    /// **它决定装配选哪条通道**：<see cref="HasCdn"/> 为 false（未配置基址）= 本地信封通道
    /// （<c>FileSystemCandidateProvider</c> + <c>LocalDirectoryCandidateFetcher</c>，无网络）；
    /// 为 true = CDN 通道（<c>HttpCandidateProvider</c> + <c>HttpCandidateFetcher</c>，信封与文件同基址）。
    ///
    /// **为什么运行时来源是安全的**：基址**不是信任边界**——任何来源返回的信封都要过
    /// RSA 锚点验签（根信任编入 <c>ContentTrustAnchors</c> 应用本体）、文件摘要由编排统一复算；
    /// 被污染的基址只能造成"下载失败/验签拒绝"，进不了权威状态。与 <c>PlayerCapabilities</c>
    /// "不接受运行时可变来源"的约束不同面——那条管兼容判定，本条只管"从哪里取字节"。
    ///
    /// **来源**：命令行参数（部署/测试注入）覆盖字段默认。发布渠道若需烙入基址，
    /// 由构建流水线写命令行参数——本类不设第二处默认值单源。
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
    /// <see cref="Parse"/> 是纯函数（参数数组进、配置出）——可 EditMode 直测；
    /// <see cref="Load"/> 是 Unity 入口（读进程命令行）。
    ///
    /// 参数形态（两种等价支持）：<c>-content.cdnUrl=http://cdn.example.com</c> 或
    /// <c>-content.cdnUrl http://cdn.example.com</c>；未出现的键保持默认。
    /// </summary>
    public static class ContentDeployConfig
    {
        /// <summary>参数键（剥掉前导 - 后匹配；大小写不敏感）。</summary>
        public const string KeyBaseUrl = "content.cdnUrl";
        public const string KeyOfferPath = "content.cdnOfferPath";
        public const string KeyTimeoutSeconds = "content.cdnTimeout";

        /// <summary>Unity 入口：读进程命令行（Editor/Player 同源）。</summary>
        public static ContentCdnConfig Load() => Parse(Environment.GetCommandLineArgs());

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

                // 形态一：-key=value（同 token 内拆）；形态二：-key value（取下一 token）
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
