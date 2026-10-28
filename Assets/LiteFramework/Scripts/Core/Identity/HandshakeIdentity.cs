using System;

namespace LiteFramework
{
    /// <summary>握手身份：只暴露 Join/换票需要的稳定字段（不把整个 IdentityContext 带进网络层）。</summary>
    public readonly struct HandshakeIdentity
    {
        public string BuildHash { get; }

        public HandshakeIdentity(string buildHash)
        {
            if (string.IsNullOrEmpty(buildHash)) throw new ArgumentException("BuildHash 不得为空", nameof(buildHash));
            BuildHash = buildHash;
        }
    }
}
