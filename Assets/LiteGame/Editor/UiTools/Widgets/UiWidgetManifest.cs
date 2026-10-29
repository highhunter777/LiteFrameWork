using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Widgets
{
    /// <summary>
    /// 控件清单（《UI 编辑器工具链专项设计》§8.3：`UiWidgetManifest` → `Assets/LiteGame/Editor/UiTools/
    /// UiWidgetManifest.json`，消费者＝控件库/文档/校验）。由 <see cref="UiWidgetManifestGenerator"/>
    /// 从 `Tmp_*` 模板 + <see cref="LiteFramework.UI.WidgetSpec"/> + 受控属性白名单汇总；生成物只读、禁手改。
    ///
    /// 字段序固定、无时间戳、稳定排序——重复生成逐字节一致。
    /// </summary>
    public sealed class UiWidgetManifest
    {
        public const string CurrentSchemaVersion = "1.0";

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("generator", Order = 1)]
        public UiWidgetManifestGeneratorInfo Generator { get; set; } = new UiWidgetManifestGeneratorInfo();

        [JsonProperty("widgets", Order = 2)]
        public List<UiWidgetManifestEntry> Widgets { get; set; } = new List<UiWidgetManifestEntry>();

        [JsonProperty("warnings", Order = 3)]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>声明面指纹（不含路径/GUID/生成器版本/告警）。</summary>
        [JsonProperty("sourceHash", Order = 4)]
        public string SourceHash { get; set; }
    }

    public sealed class UiWidgetManifestGeneratorInfo
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; } = "ui-widget-manifest";

        [JsonProperty("version", Order = 1)]
        public string Version { get; set; } = "1.0";
    }

    /// <summary>单模板条目：声明（WidgetSpec）投影 + 受控属性 + 绑定面 hash。</summary>
    public sealed class UiWidgetManifestEntry
    {
        [JsonProperty("template", Order = 0)]
        public string Template { get; set; }

        [JsonProperty("prefabPath", Order = 1)]
        public string PrefabPath { get; set; }

        [JsonProperty("prefabGuid", Order = 2)]
        public string PrefabGuid { get; set; }

        /// <summary>业务使用段（如 `HpBar_`；结构工具件为 null）。</summary>
        [JsonProperty("usageSegment", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string UsageSegment { get; set; }

        /// <summary>模板根是否声明了 <see cref="LiteFramework.UI.WidgetSpec"/>。</summary>
        [JsonProperty("specPresent", Order = 4)]
        public bool SpecPresent { get; set; }

        [JsonProperty("owner", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public string Owner { get; set; }

        [JsonProperty("summary", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
        public string Summary { get; set; }

        [JsonProperty("slots", Order = 7)]
        public List<UiWidgetSlotEntry> Slots { get; set; } = new List<UiWidgetSlotEntry>();

        [JsonProperty("states", Order = 8)]
        public List<string> States { get; set; } = new List<string>();

        [JsonProperty("events", Order = 9)]
        public List<string> Events { get; set; } = new List<string>();

        [JsonProperty("sizeBehavior", Order = 10, NullValueHandling = NullValueHandling.Ignore)]
        public string SizeBehavior { get; set; }

        [JsonProperty("constraints", Order = 11)]
        public List<string> Constraints { get; set; } = new List<string>();

        /// <summary>受控属性白名单投影（来源＝控件字段上的 `UiWidgetProperty`）。</summary>
        [JsonProperty("properties", Order = 12)]
        public List<UiWidgetPropertyEntry> Properties { get; set; } = new List<UiWidgetPropertyEntry>();

        /// <summary>模板绑定面 hash（`UiBindGenerationHash`；用于契约 `templates[].bindingHash` 对账）。</summary>
        [JsonProperty("bindingHash", Order = 13)]
        public string BindingHash { get; set; }
    }

    public sealed class UiWidgetSlotEntry
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; }

        [JsonProperty("required", Order = 1)]
        public bool Required { get; set; }

        [JsonProperty("description", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    public sealed class UiWidgetPropertyEntry
    {
        [JsonProperty("nodePath", Order = 0)]
        public string NodePath { get; set; }

        [JsonProperty("component", Order = 1)]
        public string Component { get; set; }

        [JsonProperty("member", Order = 2)]
        public string Member { get; set; }

        [JsonProperty("kind", Order = 3)]
        public string Kind { get; set; }

        [JsonProperty("displayName", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty("tooltip", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public string Tooltip { get; set; }
    }
}
