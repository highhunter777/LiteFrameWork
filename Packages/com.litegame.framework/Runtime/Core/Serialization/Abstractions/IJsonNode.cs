
namespace LiteFramework
{
    /// <summary>
    /// 只读 JSON 节点（Core 端口，零第三方）。
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
