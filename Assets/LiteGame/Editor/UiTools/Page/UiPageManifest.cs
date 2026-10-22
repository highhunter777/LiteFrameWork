using System.Collections.Generic;
using LiteGame.Editor.UiTools.Contracts;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 页面清单（《UI 系统总设计》§5.1/§10.3：编辑器清单，不进运行时、不进包；字段语义与对账口径见
    /// 《UI 编辑器工具链专项设计》§8.5）。
    ///
    /// 与纯生成物（页面契约/控件清单）不同：本清单是**工具维护的注册表**——身份字段从 `tbuiform`
    /// 刷新，维护字段（Owner/状态/设计稿/设备档/备注）由工具 API 更新并在对账时保留。
    /// 禁手改 JSON；`sourceHash` 用于发现外部写入。
    ///
    /// 一个 prefab 可在 `tbuiform` 登记多行（如 `Tmp_Dialog` 的 201 系统错误面 / 206 确认面），
    /// 因此条目按**页面（prefab）**唯一、`forms[]` 承载登记行。
    /// </summary>
    public sealed class UiPageManifest
    {
        public const string CurrentSchemaVersion = "1.0";

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("generator", Order = 1)]
        public UiPageManifestGeneratorInfo Generator { get; set; } = new UiPageManifestGeneratorInfo();

        [JsonProperty("pages", Order = 2)]
        public List<UiPageManifestEntry> Pages { get; set; } = new List<UiPageManifestEntry>();

        [JsonProperty("warnings", Order = 3)]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>注册表内容指纹（含身份与维护字段；不含告警/生成器版本/时间戳）。</summary>
        [JsonProperty("sourceHash", Order = 4)]
        public string SourceHash { get; set; }
    }

    public sealed class UiPageManifestGeneratorInfo
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; } = "ui-page-manifest";

        [JsonProperty("version", Order = 1)]
        public string Version { get; set; } = "1.0";
    }

    /// <summary>单个页面条目（按 prefab 唯一）：身份（表投影）+ 维护态（工具维护）。</summary>
    public sealed class UiPageManifestEntry
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; }

        [JsonProperty("prefabPath", Order = 1)]
        public string PrefabPath { get; set; }

        /// <summary>`tbuiform` 登记行（同一 prefab 可多行；草稿为空）。按 formId 升序。</summary>
        [JsonProperty("forms", Order = 2)]
        public List<UiPageFormEntry> Forms { get; set; } = new List<UiPageFormEntry>();

        /// <summary>Lua 逻辑路径（多行不一致时为 null 并告警）。</summary>
        [JsonProperty("luaPath", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string LuaPath { get; set; }

        [JsonProperty("cacheStrategy", Order = 4)]
        public string CacheStrategy { get; set; } = UiPageContract.DefaultCacheStrategy;

        [JsonProperty("owner", Order = 5)]
        public string Owner { get; set; } = "art.ui";

        [JsonProperty("status", Order = 6)]
        public string Status { get; set; } = nameof(UiPageStatus.Draft);

        [JsonProperty("contractPath", Order = 7)]
        public string ContractPath { get; set; }

        [JsonProperty("contractPresent", Order = 8)]
        public bool ContractPresent { get; set; }

        [JsonProperty("designRef", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
        public string DesignRef { get; set; }

        [JsonProperty("deviceProfiles", Order = 10)]
        public List<string> DeviceProfiles { get; set; } = new List<string>();

        [JsonProperty("notes", Order = 11, NullValueHandling = NullValueHandling.Ignore)]
        public string Notes { get; set; }
    }

    /// <summary>`tbuiform` 单行登记（同一页面可有多个 formId）。</summary>
    public sealed class UiPageFormEntry
    {
        [JsonProperty("formId", Order = 0)]
        public int FormId { get; set; }

        [JsonProperty("layer", Order = 1)]
        public int Layer { get; set; }

        [JsonProperty("fullScreen", Order = 2)]
        public bool FullScreen { get; set; }
    }
}
