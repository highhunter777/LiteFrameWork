using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Validation
{
    /// <summary>例外文件（手工维护的配置，不是生成物）。</summary>
    public sealed class UIRuleExceptionsDocument
    {
        public const string CurrentSchemaVersion = "1.0";

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion = CurrentSchemaVersion;

        [JsonProperty("exceptions", Order = 1)]
        public List<UIRuleExceptionEntry> Exceptions = new List<UIRuleExceptionEntry>();
    }
}
