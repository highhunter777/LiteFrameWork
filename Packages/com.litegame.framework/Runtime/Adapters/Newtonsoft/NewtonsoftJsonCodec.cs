using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiteFramework
{
    /// <summary>
    /// <see cref="IJsonCodec"/> 的 Newtonsoft 实现（第三方收口点）。
    ///
    /// 两种读取面都实现：
    /// <list type="bullet">
    /// <item><see cref="TryParse"/>：节点按需包装 <see cref="JToken"/>，取值先比 <see cref="JTokenType"/>
    ///   （严格类别，用于"对端形状不可信"的场景）；</item>
    /// <item><see cref="TryDeserialize{T}"/> / <see cref="Serialize{T}"/>：走 <see cref="WireJsonSettings"/>，
    ///   与 MetaServer 同一份设置（camelCase + 禁日期自动解析 + 未知字段报错）。</item>
    /// </list>
    ///
    /// **不得**给类型化路径另建 settings：换 resolver 就是换协议。
    /// </summary>
    public sealed class NewtonsoftJsonCodec : IJsonCodec
    {
        public bool TryParse(string json, out IJsonNode node)
        {
            node = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                node = new Node(JToken.Parse(json));
                return true;
            }
            catch (Exception)
            {
                // 坏 JSON / 深层嵌套越界等一律按"无法解析"返回 false——调用方据此判非法响应，
                // 不向网络层抛（解析失败是确定错误态，不是异常）。
                return false;
            }
        }

        public string Quote(string value) => value == null ? "\"\"" : JsonConvert.ToString(value);

        public string Serialize<T>(T value) => JsonConvert.SerializeObject(value, WireJsonSettings.Value);

        public bool TryDeserialize<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, WireJsonSettings.Value);
                return value != null;
            }
            catch (Exception)
            {
                // 语法非法 / 未知字段（MissingMemberHandling.Error）/ 类型不可转换 —— 统一按形状不符返回。
                value = null;
                return false;
            }
        }

        /// <summary>只读节点包装：<see cref="JToken"/> 的严格类别映射。</summary>
        private sealed class Node : IJsonNode
        {
            private readonly JToken _token;

            internal Node(JToken token) => _token = token;

            public JsonValueKind Kind => Map(_token.Type);

            public bool TryGetString(out string value)
            {
                value = null;
                if (_token.Type != JTokenType.String) return false;
                value = (string)_token;
                return true;
            }

            public bool TryGetInt64(out long value)
            {
                value = 0;
                if (_token.Type != JTokenType.Integer) return false;
                value = (long)_token;
                return true;
            }

            public bool TryGetBoolean(out bool value)
            {
                value = false;
                if (_token.Type != JTokenType.Boolean) return false;
                value = (bool)_token;
                return true;
            }

            public bool TryGetMember(string name, out IJsonNode member)
            {
                member = null;
                if (!(_token is JObject obj)) return false;
                JToken child = obj[name];
                if (child == null) return false;      // 缺省成员 = 不存在（值为 null 的成员仍返回 true，类别为 Null）
                member = new Node(child);
                return true;
            }

            public int Count => _token is JArray array ? array.Count : 0;

            public bool TryGetElement(int index, out IJsonNode element)
            {
                element = null;
                if (!(_token is JArray array) || index < 0 || index >= array.Count) return false;
                element = new Node(array[index]);
                return true;
            }

            /// <summary>JTokenType → <see cref="JsonValueKind"/>（未列类别一律按 Null）。</summary>
            private static JsonValueKind Map(JTokenType type)
            {
                switch (type)
                {
                    case JTokenType.Boolean: return JsonValueKind.Boolean;
                    case JTokenType.Integer: return JsonValueKind.Integer;
                    case JTokenType.Float: return JsonValueKind.Float;
                    case JTokenType.String: return JsonValueKind.String;
                    case JTokenType.Array: return JsonValueKind.Array;
                    case JTokenType.Object: return JsonValueKind.Object;
                    default: return JsonValueKind.Null;
                }
            }
        }
    }
}
