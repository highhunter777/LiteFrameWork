using System.Collections.Generic;
using System.Text.Json;
using LiteFramework;
using StjKind = System.Text.Json.JsonValueKind;

namespace LiteFramework.Core.Tests.Meta
{
    /// <summary>
    /// L1 侧的独立 <see cref="IJsonCodec"/> 实现（System.Text.Json，不依赖 Newtonsoft）： 证明"契约编解码只认端口"这一抽象成立，且行为不依赖某个库的默认值。
    /// </summary>
    internal sealed class StjJsonCodec : IJsonCodec
    {
        private static readonly JsonSerializerOptions WebOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public bool TryParse(string json, out IJsonNode node)
        {
            node = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                    node = Node.From(document.RootElement);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public string Quote(string value) => value == null ? "\"\"" : JsonSerializer.Serialize(value);

        public string Serialize<T>(T value) => JsonSerializer.Serialize(value, WebOptions);

        public bool TryDeserialize<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                value = JsonSerializer.Deserialize<T>(json, WebOptions);
                return value != null;
            }
            catch (JsonException)
            {
                value = null;
                return false;
            }
        }

        /// <summary>物化后的只读节点（类别 + 单值 + 子节点）。</summary>
        private sealed class Node : IJsonNode
        {
            private string _compact;
            private string _string;
            private long _integer;
            private bool _boolean;
            private List<Node> _array;
            private Dictionary<string, Node> _object;

            public JsonValueKind Kind { get; private set; }

            public string ToCompactJson() => _compact;

            public bool TryConvertTo<T>(out T value) where T : class
            {
                try
                {
                    value = JsonSerializer.Deserialize<T>(_compact, new JsonSerializerOptions { IncludeFields = true });
                    return value != null;
                }
                catch (JsonException) { value = null; return false; }
            }

            internal static Node From(JsonElement element)
            {
                var node = new Node { _compact = JsonSerializer.Serialize(element) };
                switch (element.ValueKind)
                {
                    case StjKind.Object:
                        node.Kind = JsonValueKind.Object;
                        var map = new Dictionary<string, Node>(System.StringComparer.Ordinal);
                        foreach (JsonProperty property in element.EnumerateObject())
                            map[property.Name] = From(property.Value);
                        node._object = map;
                        break;
                    case StjKind.Array:
                        node.Kind = JsonValueKind.Array;
                        var list = new List<Node>();
                        foreach (JsonElement item in element.EnumerateArray()) list.Add(From(item));
                        node._array = list;
                        break;
                    case StjKind.String:
                        node.Kind = JsonValueKind.String;
                        node._string = element.GetString();
                        break;
                    case StjKind.Number:
                        if (element.TryGetInt64(out long integer))
                        {
                            node.Kind = JsonValueKind.Integer;
                            node._integer = integer;
                        }
                        else
                        {
                            node.Kind = JsonValueKind.Float;
                        }
                        break;
                    case StjKind.True:
                        node.Kind = JsonValueKind.Boolean;
                        node._boolean = true;
                        break;
                    case StjKind.False:
                        node.Kind = JsonValueKind.Boolean;
                        node._boolean = false;
                        break;
                    default:
                        node.Kind = JsonValueKind.Null;
                        break;
                }
                return node;
            }

            public bool TryGetString(out string value)
            {
                value = _string;
                return Kind == JsonValueKind.String;
            }

            public bool TryGetInt64(out long value)
            {
                value = _integer;
                return Kind == JsonValueKind.Integer;
            }

            public bool TryGetBoolean(out bool value)
            {
                value = _boolean;
                return Kind == JsonValueKind.Boolean;
            }

            public bool TryGetMember(string name, out IJsonNode member)
            {
                member = null;
                if (Kind != JsonValueKind.Object || _object == null) return false;
                if (!_object.TryGetValue(name, out Node child)) return false;
                member = child;
                return true;
            }

            public int Count => Kind == JsonValueKind.Array && _array != null ? _array.Count : 0;

            public bool TryGetElement(int index, out IJsonNode element)
            {
                element = null;
                if (Kind != JsonValueKind.Array || _array == null) return false;
                if (index < 0 || index >= _array.Count) return false;
                element = _array[index];
                return true;
            }
        }
    }
}
