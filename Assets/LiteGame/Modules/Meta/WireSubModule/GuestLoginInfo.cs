namespace MetaWire
{
    /// <summary>游客登录响应：<c>{ accountId, accessToken, tokenType, expiresAtMs }</c>。
    /// 令牌对客户端不透明（只透传）；`tokenType` 恒定 "Bearer"。</summary>
    public sealed class GuestLoginInfo
    {
        public string AccountId { get; set; }
        public string AccessToken { get; set; }
        public string TokenType { get; set; }
        public long ExpiresAtMs { get; set; }
    }
}
