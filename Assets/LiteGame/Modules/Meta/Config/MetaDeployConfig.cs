using System;
using System.Globalization;
using LiteGame.Modules.Config;

namespace LiteGame.Modules.Meta.Config
{
    /// <summary>
    /// 联机入口部署配置（《上云测试专项设计》§3 批C："服务器地址/端口与 PSK 可配（构建期注入）"）。
    /// </summary>
    public sealed class MetaEntryConfig
    {
        /// <summary>Meta 基址（末尾不带 /，如 <c>https://meta.example.com</c>）。空 = 未配置。</summary>
        public string MetaUrl = "";

        /// <summary>Meta 单请求超时（秒）。</summary>
        public int TimeoutSeconds = 15;

        /// <summary>KCP 安全信封 PSK（Base64，32 字节）。空 = 未注入（回落环境变量口）。</summary>
        public string EnvelopeKeyBase64 = "";

        /// <summary>游客身份键（服务端 `deviceId` 唯一索引：**同值 = 同账号**）。空 = 未注入（回落机器标识）。
        /// 服务端字段上限 128 UTF-8 字节（超限登录被拒）。</summary>
        public string DeviceId = "";

        /// <summary>云端测试房模板名（空 = 走房间服 `default_template`，普通房为 standard）。非空必须同时给
        /// <see cref="RoomGmKey"/>——服务端校验口令，不匹配 403 且房间不创建。</summary>
        public string RoomTemplate = "";

        /// <summary>云端测试房口令（**只经命令行/烙入注入，不入库、不回显**）。
        /// 仅开发者本地配置——测试者包里不带，因此测试者无法建云端测试房。</summary>
        public string RoomGmKey = "";

        /// <summary>GM 运维端点基址（云端测试房批；空 = 由 <see cref="MetaUrl"/> 推导：同 host + 端口 9101）。</summary>
        /// 只绑隧道地址，故与 meta.url 同 host 即可。</summary>
        public string GmUrl = "";

        /// <summary>GM 端点密钥（`X-GM-Key`；**只在开发者本机配置**，测试者包不得带）。
        /// 与服务端环境变量 `LITENET_GM_KEY` 同值。</summary>
        public string GmKey = "";

        /// <summary>解析后的 GM 端点（推导或显式）。空 = 无 GM 能力（GM 页配置显性灰掉）。</summary>
        public string ResolvedGmUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(GmUrl)) return GmUrl.TrimEnd('/');
                if (string.IsNullOrEmpty(MetaUrl)) return "";
                // meta.url = http://10.8.0.1:5000 → GM = http://10.8.0.1:9101。
                if (System.Uri.TryCreate(MetaUrl, System.UriKind.Absolute, out System.Uri uri))
                    return uri.Scheme + "://" + uri.Host + ":9101";
                return "";
            }
        }

        /// <summary>是否配置了 Meta 基址（联机入口的放行判据——缺配置显性拒绝，不连任何地址）。</summary>
        public bool HasMeta => !string.IsNullOrEmpty(MetaUrl);
    }

    /// <summary>
    /// 部署配置装载（命令行 → MetaEntryConfig）。
    /// </summary>
    public static class MetaDeployConfig
    {
        /// <summary>参数键（剥掉前导 - 后匹配；大小写不敏感）。</summary>
        public const string KeyMetaUrl = "meta.url";
        public const string KeyMetaTimeout = "meta.timeout";
        public const string KeyEnvelopeKey = "envelope.key";
        public const string KeyDeviceId = "device.id";

        /// <summary>建房模板名（联机测试房批；`-room.template=sandbox`）。</summary>
        public const string KeyRoomTemplate = "room.template";

        /// <summary>测试房口令（联机测试房批；`-room.gmkey=...`——**不写进仓库、不出现在日志**）。</summary>
        public const string KeyRoomGmKey = "room.gmkey";

        /// <summary>GM 运维端点基址（缺省由 `meta.url` 推导为 `:9101`）。</summary>
        public const string KeyGmUrl = "gm.url";

        /// <summary>GM 端点密钥（`X-GM-Key`；只配在开发者本机）。</summary>
        public const string KeyGmKey = "gm.key";

        /// <summary>Unity 入口：读进程命令行（Editor/Player 同源）。</summary>
        public static MetaEntryConfig Load() => Parse(Environment.GetCommandLineArgs());

        /// <summary>Unity 入口（**含包内烙入**）：命令行 &gt; `StreamingAssets/deploy.args` &gt; 默认。
        /// 供宿主之外的消费方（GM 页）取同一份配置——`DeployBake` 是程序集内件，外部不可直读。</summary>
        public static MetaEntryConfig LoadWithBaked()
            => Load(Environment.GetCommandLineArgs(), new DeployBake().ReadArgs());   // 编辑器工具一次性读取（无静态 memo）

        /// <summary>
        /// 三源装载：命令行 > 包内烙入（ = deploy.args 全文）。
        /// </summary>
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

                if (string.IsNullOrEmpty(value)) continue;   // 显式空值 = 保持默认

                if (string.Equals(key, KeyMetaUrl, StringComparison.OrdinalIgnoreCase))
                    config.MetaUrl = value.TrimEnd('/');
                else if (string.Equals(key, KeyMetaTimeout, StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                         && seconds >= 1)
                    config.TimeoutSeconds = seconds;
                else if (string.Equals(key, KeyEnvelopeKey, StringComparison.OrdinalIgnoreCase))
                    config.EnvelopeKeyBase64 = value.Trim();
                else if (string.Equals(key, KeyDeviceId, StringComparison.OrdinalIgnoreCase))
                    config.DeviceId = value.Trim();
                else if (string.Equals(key, KeyRoomTemplate, StringComparison.OrdinalIgnoreCase))
                    config.RoomTemplate = value.Trim();
                else if (string.Equals(key, KeyRoomGmKey, StringComparison.OrdinalIgnoreCase))
                    config.RoomGmKey = value.Trim();
                else if (string.Equals(key, KeyGmUrl, StringComparison.OrdinalIgnoreCase))
                    config.GmUrl = value.Trim();
                else if (string.Equals(key, KeyGmKey, StringComparison.OrdinalIgnoreCase))
                    config.GmKey = value.Trim();
            }

            return config;
        }
    }
}
