using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools
{
    /// <summary>
    /// UI 生成物（页面契约/控件清单等）的确定性 JSON 口径（单源）：固定缩进、字段序由
    /// `JsonProperty(Order)` 决定、LF 换行、UTF-8 无 BOM（写盘侧负责编码）、无时间戳。
    ///
    /// 生成物禁手改；重复生成必须逐字节一致（`sourceHash` 之外不引入任何非确定内容）。
    /// </summary>
    public static class UiDeterministicJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateParseHandling = DateParseHandling.None,
        };

        /// <summary>序列化：LF 归一 + 末尾换行（逐字节稳定）。</summary>
        public static string Serialize<T>(T value)
        {
            string json = JsonConvert.SerializeObject(value, Settings);
            return json.Replace("\r\n", "\n").Replace("\r", "\n") + "\n";
        }

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    }
}
