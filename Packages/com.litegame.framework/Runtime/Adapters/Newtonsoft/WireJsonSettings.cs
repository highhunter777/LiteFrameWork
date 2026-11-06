using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LiteFramework
{
    /// <summary>
    /// 网络契约 JSON 的**唯一行为定义**（双端共用：客户端直接引用，服务端源链接共编）。
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
