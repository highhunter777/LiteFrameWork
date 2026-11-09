namespace LiteGame
{
    /// <summary>
    /// 根位移内容摘要端口（战斗侧对身份面的唯一暴露）：当前已装载 `rootmotion.bin` 的整段字节 SHA-256。
    /// </summary>
    public interface IRootMotionContentDigest
    {
        /// <summary>当前根位移产物的摘要（64 位小写十六进制；未装载 = 空字符串）。</summary>
        string Digest { get; }
    }
}
