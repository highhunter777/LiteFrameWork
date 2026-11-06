using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Validation
{
    /// <summary>一条例外登记（《UI 编辑器工具链专项设计》§6.3 /《UI 制作规范》§255）。</summary>
    public sealed class UIRuleExceptionEntry
    {
        [JsonProperty("ruleId", Order = 0)]
        public string RuleId;

        [JsonProperty("asset", Order = 1)]
        public string Asset;

        /// <summary>节点路径；空＝该资产任意节点。</summary>
        [JsonProperty("node", Order = 2)]
        public string Node;

        [JsonProperty("owner", Order = 3)]
        public string Owner;

        [JsonProperty("reason", Order = 4)]
        public string Reason;

        /// <summary>到期日（`yyyy-MM-dd`）；当天仍有效。</summary>
        [JsonProperty("expiry", Order = 5)]
        public string Expiry;
    }
}
