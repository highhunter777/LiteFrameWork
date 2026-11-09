namespace MetaWire
{
    /// <summary>游客登录请求：<c>{ requestId, deviceId }</c>。</summary>
    public sealed class GuestLoginRequest
    {
        public string RequestId { get; set; }
        public string DeviceId { get; set; }
    }
}
