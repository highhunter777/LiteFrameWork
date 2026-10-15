using System;
using System.Globalization;

namespace LiteGame
{
    /// <summary>
    /// 联机入口部署配置（《上云测试专项设计》§3 批C："服务器地址/端口与 PSK 可配（构建期注入）"）。
    ///
    /// 三项注入面：
    /// - <see cref="MetaUrl"/>：Meta 基址（游客登录/票据/战绩查询的同一主机）。**空 = 未配置** →
    ///   联机入口显性拒绝（登录页报"未配置服务器"，不连任何地址——fail-closed，不悄悄回退本地）；
    /// - <see cref="TimeoutSeconds"/>：Meta 单请求超时；
    /// - <see cref="EnvelopeKeyBase64"/>：KCP 安全信封 PSK（Base64、32 字节）。**空 = 不注入**，
    ///   传输装配回落 <see cref="LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment"/> 既有口
    ///   （与部署侧 `LITENET_SECURE_ENVELOPE_KEY` 同名——两道注入口不互斥，命令行优先）。
    ///
    /// 与 <see cref="LiteClient.ContentCdnConfig"/> 同源先例：运行时来源是命令行参数（部署/测试注入），
    /// 解析是纯函数（EditMode 直测）；基址不是信任边界——访问令牌/Join Ticket 的效力由服务端验签把守。
    /// </summary>
    public sealed class MetaEntryConfig
    {
        /// <summary>Meta 基址（末尾不带 /，如 <c>https://meta.example.com</c>）。空 = 未配置。</summary>
        public string MetaUrl = "";

        /// <summary>Meta 单请求超时（秒）。</summary>
        public int TimeoutSeconds = 15;

        /// <summary>KCP 安全信封 PSK（Base64，32 字节）。空 = 未注入（回落环境变量口）。</summary>
        public string EnvelopeKeyBase64 = "";

        /// <summary>是否配置了 Meta 基址（联机入口的放行判据——缺配置显性拒绝，不连任何地址）。</summary>
        public bool HasMeta => !string.IsNullOrEmpty(MetaUrl);
    }

    /// <summary>
    /// 部署配置装载（命令行 → <see cref="MetaEntryConfig"/>）。
    /// <see cref="Parse"/> 是纯函数（参数数组进、配置出）——EditMode 直测；
    /// <see cref="Load"/> 是 Unity 入口（读进程命令行）。
    ///
    /// 参数形态（与 <see cref="LiteClient.ContentDeployConfig"/> 同款，两种等价支持）：
    /// <c>-meta.url=https://meta.example.com</c> 或 <c>-meta.url https://meta.example.com</c>；
    /// 未出现的键保持默认。非法值静默保持默认——部署参数不是攻击面，但也不让它让启动失败。
    ///
    /// **三源装载（发布链）**：<see cref="Load(string[], string)"/> 合并**命令行参数 > 包内烙入文件
    /// （StreamingAssets/deploy.args——构建流水线按 BuildProfile.endpoints 写入的参数行流）>
    /// 字段默认**：烙入是分发渠道的零参数启动面（信封 PSK 烙入后环境变量口语义不变——传输装配
    /// 在命令行/烙入皆空时仍回落 <c>LITENET_SECURE_ENVELOPE_KEY</c>）。
    /// </summary>
    public static class MetaDeployConfig
    {
        /// <summary>参数键（剥掉前导 - 后匹配；大小写不敏感）。</summary>
        public const string KeyMetaUrl = "meta.url";
        public const string KeyMetaTimeout = "meta.timeout";
        public const string KeyEnvelopeKey = "envelope.key";

        /// <summary>Unity 入口：读进程命令行（Editor/Player 同源）。</summary>
        public static MetaEntryConfig Load() => Parse(Environment.GetCommandLineArgs());

        /// <summary>三源装载：命令行 > 包内烙入（<paramref name="bakedArgsText"/> = deploy.args 全文；
        /// null/空白 = 无烙入文件，退化为纯命令行装载）。纯函数可直测。
        /// 实现为 **token 层拼接**（烙入流在前、命令行流在后，<see cref="Parse"/> 顺序覆盖 ⇒ 后见者赢）：
        /// 字段默认值非零（如 <see cref="MetaEntryConfig.TimeoutSeconds"/>=15），按字段值判"命令行给没给过"
        /// 结构性不可行——token 层单流无此歧义。</summary>
        public static MetaEntryConfig Load(string[] args, string bakedArgsText)
        {
            string[] baked = Tokenize(bakedArgsText);
            if (baked.Length == 0) return Parse(args);
            if (args == null || args.Length == 0) return Parse(baked);
            var merged = new string[baked.Length + args.Length];
            baked.CopyTo(merged, 0);
            args.CopyTo(merged, baked.Length);
            return Parse(merged);
        }

        /// <summary>烙入文本 → 参数 token 流：按空白切（一行一个 <c>-key=value</c>，与命令行同形）。</summary>
        public static string[] Tokenize(string bakedArgsText)
        {
            if (string.IsNullOrWhiteSpace(bakedArgsText)) return Array.Empty<string>();
            return bakedArgsText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>解析参数数组（纯函数；null/空数组 = 全默认）。</summary>
        public static MetaEntryConfig Parse(string[] args)
        {
            var config = new MetaEntryConfig();
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

                if (string.IsNullOrEmpty(value)) continue;   // 显式空值 = 保持默认

                if (string.Equals(key, KeyMetaUrl, StringComparison.OrdinalIgnoreCase))
                    config.MetaUrl = value.TrimEnd('/');
                else if (string.Equals(key, KeyMetaTimeout, StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                         && seconds >= 1)
                    config.TimeoutSeconds = seconds;
                else if (string.Equals(key, KeyEnvelopeKey, StringComparison.OrdinalIgnoreCase))
                    config.EnvelopeKeyBase64 = value.Trim();
            }

            return config;
        }
    }
}
