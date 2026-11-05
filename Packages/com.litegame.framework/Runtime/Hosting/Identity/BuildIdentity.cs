using System;

namespace LiteFramework
{
    /// <summary>
    /// 构建身份（BuildIdentity）：一次安装包的稳定身份面。只由组合根构造，
    /// 业务代码不各自拼 buildHash/AppVersion/平台/渠道。
    /// </summary>
    public sealed class BuildIdentity
    {
        /// <summary>联机版本哈希（与 Server 同源；Join/换票/重连版本基准）。</summary>
        public string BuildHash { get; }

        /// <summary>安装包版本（Application.version；诊断与兼容身份串）。</summary>
        public string AppVersion { get; }

        /// <summary>运行平台（Application.platform 的稳定名，如 WindowsPlayer）。</summary>
        public string Platform { get; }

        /// <summary>发布渠道（空 = 未声明；渠道包由部署/构建注入）。</summary>
        public string Channel { get; }

        /// <summary>Lua Bridge API 版本（能力集合单源 = BridgeCapabilitySet.Default）。</summary>
        public int BridgeApiVersion { get; }

        public BuildIdentity(string buildHash, string appVersion, string platform, string channel, int bridgeApiVersion)
        {
            if (string.IsNullOrEmpty(buildHash)) throw new ArgumentException("BuildHash 不得为空", nameof(buildHash));
            if (bridgeApiVersion < 0) throw new ArgumentOutOfRangeException(nameof(bridgeApiVersion));
            BuildHash = buildHash;
            AppVersion = appVersion ?? string.Empty;
            Platform = platform ?? string.Empty;
            Channel = channel ?? string.Empty;
            BridgeApiVersion = bridgeApiVersion;
        }
    }
}
