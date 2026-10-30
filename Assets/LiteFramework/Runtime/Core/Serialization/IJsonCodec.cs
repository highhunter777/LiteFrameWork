namespace LiteFramework
{
    /// <summary>
    /// 网络契约 JSON 编解码端口（Core，零第三方）。
    ///
    /// 两种读取面，按消费方语义二选一：
    /// <list type="bullet">
    /// <item><see cref="TryParse"/> + <see cref="IJsonNode"/>：**严格类别读取**——类别不符即失败，
    ///   适合"对端形状不可信"的场景（外部内容清单、探针）；</item>
    /// <item><see cref="TryDeserialize{T}"/>：**类型化反序列化**——适合"两端共用同一份 DTO"的契约
    ///   （形状在编译期单源，宽松转换不再是风险来源）。</item>
    /// </list>
    ///
    /// <see cref="Serialize{T}"/> 与 <see cref="Deserialize{T}"/> 共用同一份契约设置
    /// （`WireJsonSettings`），保证两端字段名/日期/null 行为一致。
    ///
    /// 实现住 <c>Assets/LiteFramework/Runtime/Adapters/Newtonsoft/</c>：业务代码不得直接引用序列化库。
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
