namespace LiteFramework
{
    /// <summary>
    /// 网络契约 JSON 编解码端口（Core，零第三方）。
    /// </summary>
    public interface IJsonCodec
    {
        /// <summary>解析任意 JSON 值（对象/数组/标量）；语法非法返回 false 且 <paramref name="node"/> 为 null。</summary>
        bool TryParse(string json, out IJsonNode node);

        /// <summary>JSON 字符串字面量（含首尾引号与转义）。<c>null</c> 视为空串（返回 <c>""</c>）。</summary>
        string Quote(string value);

        /// <summary>按契约设置序列化（字段名口径与两端一致）。</summary>
        string Serialize<T>(T value);

        /// <summary>按契约设置反序列化；空串/语法非法/形状不符返回 false（不抛）。</summary>
        bool TryDeserialize<T>(string json, out T value) where T : class;
    }
}
