using System;
using System.Collections.Generic;
using LiteFramework.UI;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Contracts
{
    /// <summary>
    /// 页面契约模型（字段语义见《UI 编辑器工具链专项设计》§5 与
    /// `Docs/设计文档/客户端/UI系统/UiPageContract.schema.json`）。
    /// 生成物只读：仅 <see cref="UiContractGenerator"/> 写；字段顺序固定，禁止时间戳。
    /// </summary>
    public sealed class UiPageContract
    {
        public const string CurrentSchemaVersion = "2.0";

        /// <summary>
        /// `cacheStrategy` 默认值——`tbuiform` 尚无该列时的唯一默认口径（词表真相是
        /// <see cref="UICacheStrategy"/>；表列落地后只改投影层，不另立第二默认）。
        /// </summary>
        public static readonly string DefaultCacheStrategy = UICacheStrategy.Lru.ToString();

        /// <summary>缓存策略是否在词表内（词表真相＝<see cref="UICacheStrategy"/>）。</summary>
        public static bool IsValidCacheStrategy(string cacheStrategy)
            => !string.IsNullOrEmpty(cacheStrategy)
               && Array.Exists(Enum.GetNames(typeof(UICacheStrategy)),
                   value => string.Equals(value, cacheStrategy, StringComparison.Ordinal));

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("generator", Order = 1)]
        public UiContractGeneratorInfo Generator { get; set; } = new UiContractGeneratorInfo();

        [JsonProperty("page", Order = 2)]
        public UiContractPage Page { get; set; } = new UiContractPage();

        [JsonProperty("controls", Order = 3)]
        public List<UiContractControl> Controls { get; set; } = new List<UiContractControl>();

        [JsonProperty("text", Order = 4)]
        public List<UiContractTextBinding> Text { get; set; } = new List<UiContractTextBinding>();

        [JsonProperty("styles", Order = 5)]
        public List<UiContractStyleBinding> Styles { get; set; } = new List<UiContractStyleBinding>();

        [JsonProperty("templates", Order = 6)]
        public List<UiContractTemplateUse> Templates { get; set; } = new List<UiContractTemplateUse>();

        [JsonProperty("events", Order = 7)]
        public List<UiContractEventBinding> Events { get; set; } = new List<UiContractEventBinding>();

        [JsonProperty("sourceHash", Order = 8)]
        public string SourceHash { get; set; }
    }

    public sealed class UiContractGeneratorInfo
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; } = "ui-contract";

        [JsonProperty("version", Order = 1)]
        public string Version { get; set; } = "1.0";
    }

    public sealed class UiContractPage
    {
        [JsonProperty("formId", Order = 0)]
        public int FormId { get; set; }

        [JsonProperty("name", Order = 1)]
        public string Name { get; set; }

        [JsonProperty("prefabPath", Order = 2)]
        public string PrefabPath { get; set; }

        [JsonProperty("prefabGuid", Order = 3)]
        public string PrefabGuid { get; set; }

        [JsonProperty("luaPath", Order = 4)]
        public string LuaPath { get; set; }

        [JsonProperty("layer", Order = 5)]
        public int Layer { get; set; }

        [JsonProperty("fullScreen", Order = 6)]
        public bool FullScreen { get; set; }

        [JsonProperty("cacheStrategy", Order = 7)]
        public string CacheStrategy { get; set; } = UiPageContract.DefaultCacheStrategy;

        [JsonProperty("owner", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
        public string Owner { get; set; }

        [JsonProperty("designRef", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
        public string DesignRef { get; set; }
    }

    public sealed class UiContractControl
    {
        [JsonProperty("bindName", Order = 0)]
        public string BindName { get; set; }

        [JsonProperty("nodePath", Order = 1)]
        public string NodePath { get; set; }

        [JsonProperty("type", Order = 2)]
        public string Type { get; set; }

        [JsonProperty("kind", Order = 3)]
        public string Kind { get; set; }

        [JsonProperty("required", Order = 4)]
        public bool Required { get; set; }
    }

    public sealed class UiContractTextBinding
    {
        [JsonProperty("nodePath", Order = 0)]
        public string NodePath { get; set; }

        [JsonProperty("key", Order = 1)]
        public string Key { get; set; }

        [JsonProperty("args", Order = 2)]
        public int Args { get; set; }
    }

    public sealed class UiContractStyleBinding
    {
        [JsonProperty("nodePath", Order = 0)]
        public string NodePath { get; set; }

        [JsonProperty("role", Order = 1)]
        public string Role { get; set; }

        [JsonProperty("token", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Token { get; set; }
    }

    public sealed class UiContractTemplateUse
    {
        [JsonProperty("nodePath", Order = 0)]
        public string NodePath { get; set; }

        [JsonProperty("template", Order = 1)]
        public string Template { get; set; }

        [JsonProperty("bindingHash", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string BindingHash { get; set; }
    }

    public sealed class UiContractEventBinding
    {
        [JsonProperty("bindName", Order = 0)]
        public string BindName { get; set; }

        [JsonProperty("event", Order = 1)]
        public string Event { get; set; }

        [JsonProperty("handler", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Handler { get; set; }
    }
}
