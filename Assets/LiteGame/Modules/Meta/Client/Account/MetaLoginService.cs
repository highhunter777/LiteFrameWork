using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using MetaWire;

namespace LiteGame
{
    /// <summary>root 登录入口：游客登录 + 部署事实；不持令牌。</summary>
    public sealed class MetaLoginService : IMetaConfigFacts
    {
        /// <summary>云端测试房模板名（服务端 `rooms.sandbox`；单源）。</summary>
        public const string TestRoomTemplate = "sandbox";

        private readonly IMetaClient _client;
        private readonly Func<string> _deviceId;
        private readonly IdentityContext _identity;

        public MetaLoginService(IMetaClient client, Func<string> deviceId, string serverUrl, IdentityContext identity)
        {
            _client = client;
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            ServerUrl = serverUrl ?? string.Empty;
        }

        public bool IsConfigured => _client != null;
        public string ServerUrl { get; }
        public string BuildHash => _identity.Handshake.BuildHash;

        public async UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync()
        {
            if (!IsConfigured)
                return MetaCall<GuestLoginInfo>.Failed(
                    MetaFailure.Rejected(0, "entry.not-configured", "未配置服务器地址（-meta.url）"));
            return await _client.GuestLoginAsync(_deviceId());
        }
    }
}
