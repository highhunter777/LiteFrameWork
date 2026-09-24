using System;
using System.Collections.Generic;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配参数（《Meta 服务专项设计》§10 配置与启动、§13 预算与容量）。
    ///
    /// **必须是属性而非字段**（与 <c>RoomServer.Runtime.RoomConfig</c> 的字段形态不同，这是有意的）：
    /// `ConfigurationBinder` 只绑定属性，字段形态会让 §10 的"版本化文件 / 环境变量"配置源
    /// **静默不生效**（2026-09-25 实测：字段形态从配置文件读到值后仍是内联默认值）。
    ///
    /// **每项都有范围校验**（§10"均有范围校验"），拒绝值直接导致启动失败返回非零退出码，
    /// 不得带默认错配置继续运行。
    ///
    /// 本批为宿主骨架：只承载监听、上限与关闭时限。业务参数（Mongo/Redis/限流阈值/Outbox 容量）
    /// 归 G3/R4，接入时在此追加属性与校验，不另建第二套配置类。
    /// </summary>
    public sealed class MetaConfig
    {
        // ---- 监听（§4.1 Kestrel；测试用 http://127.0.0.1:0 取临时端口）----

        /// <summary>监听地址。生产必须为 HTTPS 端点（§12"外部接口一律 TLS"）。</summary>
        public string BindAddress { get; set; } = "http://127.0.0.1:5000";

        // ---- 请求边界（对齐《服务端总设计》§5 P0-3"包体在解析前限制长度"）----

        /// <summary>入站请求体上限（字节）。落在 Kestrel 的 MaxRequestBodySize，解析前即拒绝。</summary>
        public int MaxInboundBytes { get; set; } = 65536;

        // ---- 优雅关闭（§10 优雅关闭四步）----

        /// <summary>关闭时限（秒）：Host 在该期限内完成在途请求与后台服务停止，超时强制结束。</summary>
        public int ShutdownTimeoutSeconds { get; set; } = 15;

        public static MetaConfig Default() => new MetaConfig();

        /// <summary>
        /// 范围校验（§10）。返回全部违规项——一次报告所有问题，不逐条反复试错启动。
        /// 空列表 = 通过。**非法配置必须阻止启动**，不得降级为默认值。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(BindAddress))
            {
                errors.Add("BindAddress 不得为空");
            }
            else if (!Uri.TryCreate(BindAddress, UriKind.Absolute, out Uri parsed))
            {
                errors.Add("BindAddress 不是合法绝对 URI：" + BindAddress);
            }
            else if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add("BindAddress 只支持 http/https，实际：" + parsed.Scheme);
            }

            // 1MB 上限与 §5 P0-3 的"解析前限制长度"一致；低于 1KB 会挡住正常登录载荷
            if (MaxInboundBytes < 1024 || MaxInboundBytes > 1024 * 1024)
            {
                errors.Add("MaxInboundBytes 必须在 1024..1048576，实际：" + MaxInboundBytes);
            }

            if (ShutdownTimeoutSeconds < 1 || ShutdownTimeoutSeconds > 300)
            {
                errors.Add("ShutdownTimeoutSeconds 必须在 1..300，实际：" + ShutdownTimeoutSeconds);
            }

            return errors;
        }
    }
}
