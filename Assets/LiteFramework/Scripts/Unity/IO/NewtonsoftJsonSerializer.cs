using Newtonsoft.Json;

namespace LiteFramework
{
    /// <summary>IJsonSerializer 的 Newtonsoft 实现。**纯实现类,不是 MonoBehaviour**——由 GameEntry.Awake
    /// `new` 出来注入 FileSys.Init。用静态 JsonConvert 调用（线程安全）：FileSys 的异步 JSON 在线程池序列化。</summary>
    public sealed class NewtonsoftJsonSerializer : IJsonSerializer
    {
        public string Serialize<T>(T value) => JsonConvert.SerializeObject(value);

        public T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    }
}
