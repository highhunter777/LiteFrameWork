namespace LiteFramework
{
    /// <summary>
    /// 内容签名验证端口（采用成熟签名实现和确定的签名字节编码； 客户端只带信任公钥，私钥由受控发布签名环境持有）。
    /// </summary>
    public interface ISignatureVerifier
    {
        /// <summary>验证签名。data/signature 为 null 或公钥不可用 → false。</summary>
        bool Verify(byte[] data, byte[] signature);
    }
}
