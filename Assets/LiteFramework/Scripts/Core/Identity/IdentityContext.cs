using System;

namespace LiteFramework
{
    /// <summary>
    /// 统一身份上下文（路线图 §4.9）：Build/Content/Schema 三层的唯一构造点。
    /// 由 <c>IdentityModule</c> 生成；内容身份在内容/配置装配后绑定一次。
    /// 业务代码只读本上下文，不各自拼 buildHash/configHash/sourceHash。
    /// </summary>
    public sealed class IdentityContext
    {
        private readonly object _gate = new object();
        private Func<ContentIdentity> _contentProvider;

        public BuildIdentity Build { get; }
        public SchemaIdentity Schema { get; }

        public IdentityContext(BuildIdentity build, SchemaIdentity schema)
        {
            Build = build ?? throw new ArgumentNullException(nameof(build));
            Schema = schema;
        }

        /// <summary>当前内容身份；未绑定 = 内置内容空身份（不伪造候选版本）。</summary>
        public ContentIdentity Content
        {
            get
            {
                Func<ContentIdentity> provider;
                lock (_gate) provider = _contentProvider;
                return provider != null ? provider() : ContentIdentity.Empty;
            }
        }

        /// <summary>装配期绑定内容身份提供者（只允许一次；运行期不重绑）。</summary>
        public void BindContent(Func<ContentIdentity> provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_gate)
            {
                if (_contentProvider != null)
                    throw new InvalidOperationException("内容身份已绑定——IdentityContext 只允许组合根绑定一次");
                _contentProvider = provider;
            }
        }

        /// <summary>握手投影（网络层只拿 Join 需要的字段）。</summary>
        public HandshakeIdentity Handshake => new HandshakeIdentity(Build.BuildHash);

        /// <summary>诊断投影（现场包身份面）。</summary>
        public Diagnostics.DiagnosticIdentity Diagnostics
            => new Diagnostics.DiagnosticIdentity(Build.BuildHash, Build.AppVersion, Build.Platform);

        /// <summary>
        /// 本安装包的能力下限。**本版显式 Off**：能力生成物与内容发布侧同批切换前，
        /// 不拿数值轴做判定；身份串（AppVersion/Platform/Channel）仍照常填充。
        /// </summary>
        public PlayerCapabilities CreatePlayerCapabilities() => new PlayerCapabilities
        {
            CompatibilityMode = PlayerCapabilities.CapabilityMode.Off,
            AppVersion = Build.AppVersion,
            Platform = Build.Platform,
            Channel = Build.Channel,
            BridgeApiVersion = Build.BridgeApiVersion,
            ProtocolVersion = Schema.ProtocolVersion,
            SimVersion = Schema.SimVersion,
            ConfigSchemaVersion = Schema.ConfigSchemaVersion,
            SaveSchemaVersion = Schema.SaveSchemaVersion,
        };
    }
}
