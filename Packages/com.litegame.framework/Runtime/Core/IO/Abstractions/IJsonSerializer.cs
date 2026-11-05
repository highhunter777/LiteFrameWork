using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IJsonSerializer
    {                            // Core 只认接口；Newtonsoft 实现在 Unity 层（第三方不进 Core）
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }
}
