using System;
using System.Collections.Generic;
using LiteFramework.UI;
using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>
    /// 页面清单（《UI 系统总设计》§5.1/§10.3：编辑器清单，不进运行时、不进包；字段语义与对账口径见 《UI 编辑器工具链专项设计》§8.5）。
    /// </summary>
    public sealed class UIPageManifest
    {
        public const string CurrentSchemaVersion = "1.0";

        /// <summary>`cacheStrategy` 默认值（词表真相＝<see cref="UICacheStrategy"/>；表列与清单条目共用）。</summary>
        public static readonly string DefaultCacheStrategy = UICacheStrategy.Lru.ToString();

        /// <summary>缓存策略是否在词表内（词表真相＝<see cref="UICacheStrategy"/>）。</summary>
        public static bool IsValidCacheStrategy(string cacheStrategy)
            => !string.IsNullOrEmpty(cacheStrategy)
               && Array.Exists(Enum.GetNames(typeof(UICacheStrategy)),
                   value => string.Equals(value, cacheStrategy, StringComparison.Ordinal));

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("generator", Order = 1)]
        public UIPageManifestGeneratorInfo Generator { get; set; } = new UIPageManifestGeneratorInfo();

        [JsonProperty("pages", Order = 2)]
        public List<UIPageManifestEntry> Pages { get; set; } = new List<UIPageManifestEntry>();

        [JsonProperty("warnings", Order = 3)]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>注册表内容指纹（含身份与维护字段；不含告警/生成器版本/时间戳）。</summary>
        [JsonProperty("sourceHash", Order = 4)]
        public string SourceHash { get; set; }
    }

    public sealed class UIPageManifestGeneratorInfo
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; } = "ui-page-manifest";

        [JsonProperty("version", Order = 1)]
        public string Version { get; set; } = "1.0";
    }

    /// <summary>单个页面条目（按 prefab 唯一）：身份（表投影）+ 维护态（工具维护）。</summary>
    public sealed class UIPageManifestEntry
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; }

        [JsonProperty("prefabPath", Order = 1)]
        public string PrefabPath { get; set; }

        /// <summary>`tbuiform` 登记行（同一 prefab 可多行；草稿为空）。按 formId 升序。</summary>
        [JsonProperty("forms", Order = 2)]
        public List<UIPageFormEntry> Forms { get; set; } = new List<UIPageFormEntry>();

        /// <summary>缓存策略（per-form 缓存策略列；缺省 LRU）。</summary>
        [JsonProperty("cacheStrategy", Order = 4)]
        public string CacheStrategy { get; set; } = UIPageManifest.DefaultCacheStrategy;

        [JsonProperty("owner", Order = 5)]
        public string Owner { get; set; } = "art.ui";

        [JsonProperty("status", Order = 6)]
        public string Status { get; set; } = nameof(UIPageStatus.Draft);

        [JsonProperty("designRef", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
        public string DesignRef { get; set; }

        [JsonProperty("deviceProfiles", Order = 8)]
        public List<string> DeviceProfiles { get; set; } = new List<string>();

        [JsonProperty("notes", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
        public string Notes { get; set; }
    }

    /// <summary>`tbuiform` 单行登记（同一页面可有多个 formId）。</summary>
    public sealed class UIPageFormEntry
    {
        [JsonProperty("formId", Order = 0)]
        public int FormId { get; set; }

        [JsonProperty("layer", Order = 1)]
        public int Layer { get; set; }

        [JsonProperty("fullScreen", Order = 2)]
        public bool FullScreen { get; set; }
    }
}
