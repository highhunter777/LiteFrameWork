namespace LiteGame
{
    /// <summary>
    /// 根位移内容摘要端口（战斗侧对身份面的唯一暴露）：当前已装载 `rootmotion.bin` 的整段字节 SHA-256。
    ///
    /// **时序**：容器装配期由战斗域注册实现；首个对局装载成功之前为空字符串（内容身份可读，
    /// 但此时没有对局在跑，摘要不参与任何准入判定）。`LiteFramework` 的 ContentIdentity 只经本端口取字符串，
    /// 不引用任何战斗域类型。
    /// </summary>
    public interface IRootMotionContentDigest
    {
        /// <summary>当前根位移产物的摘要（64 位小写十六进制；未装载 = 空字符串）。</summary>
        string Digest { get; }
    }
}
