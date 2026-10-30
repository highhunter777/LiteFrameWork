using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LiteFramework
{
    /// <summary>
    /// 网络契约 JSON 的**唯一行为定义**（双端共用：客户端直接引用，服务端源链接共编）。
    ///
    /// 为什么住适配层而不是契约目录：共享契约件必须零第三方（R12 适配器边界纪律），
    /// 而本件必然引用序列化库——放这里既合法，又能被服务端链接。
    ///
    /// 三条设置的含义：
    /// <list type="bullet">
    /// <item>camelCase：线上字段名口径（改它 = 改协议）；</item>
    /// <item><c>DateParseHandling.None</c>：禁 Newtonsoft 把"看起来像日期"的字符串自动转 `DateTime`
    ///   （转回去格式就变了，属于静默改形状）；</item>
    /// <item><c>MissingMemberHandling.Error</c>：**未知字段直接失败**——契约漂移立刻炸，
    ///   而不是被静默忽略后表现为"某个字段永远为 null"。</item>
    /// </list>
    /// 禁止在本项目其它地方另建一套 settings（换了库/换了 resolver 就是换协议）。
    /// </summary>
    public static class WireJsonSettings
    {
        /// <summary>契约序列化设置（唯一实例；只读使用，不要在调用点改它）。</summary>
        public static readonly JsonSerializerSettings Value = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error,
        };
    }
}
