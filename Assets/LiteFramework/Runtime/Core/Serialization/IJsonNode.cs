namespace LiteFramework
{
    /// <summary>
    /// JSON 值类别（<see cref="IJsonNode.Kind"/> 用）。与 Newtonsoft 的 JTokenType 语义一一对应，
    /// 但不把第三方类型暴露进 Core——第三方映射留在适配层实现里。
    /// </summary>
    public enum JsonValueKind
    {
        Null = 0,
        Boolean = 1,
        Integer = 2,
        Float = 3,
        String = 4,
        Array = 5,
        Object = 6,
    }

    /// <summary>
    /// 只读 JSON 节点（Core 端口，零第三方）。**严格类别读取**：取值方法只在类别匹配时成功——
    /// 数字不隐式转字符串、字符串不隐式转数字。网络契约的"形状不符即判非法响应"依赖这条语义
    /// （宽松反序列化会把 <c>"5"</c> 读成 5、把缺失字段读成默认值，等于静默放行协议漂移）。
    ///
    /// 实现住第三方适配层（<c>Adapters/Newtonsoft/NewtonsoftJsonCodec.cs</c>）；
    /// 业务代码只认本接口与 <see cref="IJsonCodec"/>。
    /// </summary>
    public interface IJsonNode
    {
        /// <summary>本节点的值类别。</summary>
        JsonValueKind Kind { get; }

        /// <summary>仅在类别为 <see cref="JsonValueKind.String"/> 时成功（空串照返回；是否接受空串由调用方判）。</summary>
        bool TryGetString(out string value);

        /// <summary>仅在类别为 <see cref="JsonValueKind.Integer"/> 时成功。</summary>
        bool TryGetInt64(out long value);

        /// <summary>仅在类别为 <see cref="JsonValueKind.Boolean"/> 时成功。</summary>
        bool TryGetBoolean(out bool value);

        /// <summary>仅当本节点是对象且存在该成员时成功（成员值为 null 也成功——由调用方按类别判）。</summary>
        bool TryGetMember(string name, out IJsonNode member);

        /// <summary>数组元素数；非数组为 0。</summary>
        int Count { get; }

        /// <summary>仅当本节点是数组且下标在界内时成功。</summary>
        bool TryGetElement(int index, out IJsonNode element);
    }
}
