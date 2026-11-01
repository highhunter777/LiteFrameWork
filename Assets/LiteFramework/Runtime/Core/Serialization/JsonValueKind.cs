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

}
