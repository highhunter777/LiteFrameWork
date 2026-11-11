namespace LiteGame.Modules.Meta.Abstractions.Room
{
    /// <summary>
    /// Meta 契约的**客户端本地面**（线形状在 MetaWire，双端共用）。
    /// </summary>
    public enum MetaFailureKind
    {
        /// <summary>传输层失败（连不上/DNS/超时/中断）——暂态，可重试。</summary>
        Network = 0,

        /// <summary>服务端拒绝（非 2xx）：Code 携带 meta.* 稳定错误码（如 auth.unauthorized /
        /// lobby.no-capacity / lobby.version-mismatch），HttpStatus 携带状态码；409/401 等确定性，5xx 暂态。</summary>
        Rejected = 1,

        /// <summary>响应形状非法（2xx 但字段缺失/类型不符/坏 JSON）——确定性，重试无效。</summary>
        InvalidResponse = 2,
    }
}
