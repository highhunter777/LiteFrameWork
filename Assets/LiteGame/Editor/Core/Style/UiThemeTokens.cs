using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 令牌源模型（《UI 编辑器工具链专项设计》§8.1；格式以 `Docs/设计文档/客户端/UI系统/UiStyleTokens.schema.json` 2.0 为准）。
    /// 字段序由 `JsonProperty(Order)` 固定，作为规范文本序列化的唯一顺序来源。
    /// 组清单集中在 <see cref="UiThemeGroups.Enumerate"/>：新增一组＝加一个组属性 + 该枚举里一行 + 下游 SO/快照数组。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeTokens
    {
        [JsonProperty("schemaVersion", Order = 0)] public string SchemaVersion;

        [JsonProperty("meta", Order = 1)] public UiThemeMeta Meta;

        [JsonProperty("groups", Order = 2)] public UiThemeGroups Groups;
    }

    /// <summary>令牌源元数据（名称/版本/主题名/说明；`theme` 供将来多变体用，首版只有 Default）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeMeta
    {
        [JsonProperty("name", Order = 0)] public string Name;

        [JsonProperty("version", Order = 1)] public string Version;

        [JsonProperty("theme", Order = 2)] public string Theme;

        [JsonProperty("description", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }

    /// <summary>七组令牌（schema 要求七组都存在；组内可为空＝该组尚无消费者，不造臆造值）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeGroups
    {
        [JsonProperty("color", Order = 0)] public UiThemeGroup<UiThemeColorToken> Color;

        [JsonProperty("typography", Order = 1)] public UiThemeGroup<UiThemeTypographyToken> Typography;

        [JsonProperty("spacing", Order = 2)] public UiThemeGroup<UiThemeNumberToken> Spacing;

        [JsonProperty("radius", Order = 3)] public UiThemeGroup<UiThemeNumberToken> Radius;

        [JsonProperty("elevation", Order = 4)] public UiThemeGroup<UiThemeElevationToken> Elevation;

        [JsonProperty("motion", Order = 5)] public UiThemeGroup<UiThemeMotionToken> Motion;

        [JsonProperty("icon", Order = 6)] public UiThemeGroup<UiThemeNumberToken> Icon;

        /// <summary>
        /// 七组统一枚举（**组清单单源**）：组名、允许的 ID 前缀、组对象一次给出。
        /// 校验/规范排序都只遍历这里——新增组不散落在多个 `ValidateGroup(...)` 调用点。
        /// </summary>
        public IEnumerable<(string Name, string[] Prefixes, IUiThemeGroup Group)> Enumerate()
        {
            yield return ("color", new[] { "color." }, Color);
            yield return ("typography", new[] { "font." }, Typography);
            yield return ("spacing", new[] { "space." }, Spacing);
            yield return ("radius", new[] { "radius." }, Radius);
            yield return ("elevation", new[] { "elev." }, Elevation);
            yield return ("motion", new[] { "motion.", "ease." }, Motion);
            yield return ("icon", new[] { "icon." }, Icon);
        }
    }

    /// <summary>令牌组的非泛型视图：让"七组"可以在一个循环里遍历，而不必为每组写一遍调用。</summary>
    public interface IUiThemeGroup
    {
        /// <summary>组是否存在（JSON 里缺该组时为 false——由校验器报"七组必须存在"）。</summary>
        bool Present { get; }

        /// <summary>组内令牌（ID → 令牌对象）；不存在时返回空序列。</summary>
        IEnumerable<(string Id, object Token)> Enumerate();

        /// <summary>组内令牌按 ID `Ordinal` 就地排序（规范文本口径；幂等）。</summary>
        void SortTokens();
    }

    /// <summary>一组令牌：字典键为令牌 ID（schema 的 `tokens` 对象）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeGroup<T> : IUiThemeGroup
    {
        [JsonProperty("tokens", Order = 0)] public Dictionary<string, T> Tokens;

        [JsonIgnore] public bool Present => Tokens != null;

        public IEnumerable<(string Id, object Token)> Enumerate()
        {
            if (Tokens == null) yield break;
            foreach (KeyValuePair<string, T> pair in Tokens) yield return (pair.Key, pair.Value);
        }

        public void SortTokens()
        {
            if (Tokens == null) return;
            var sorted = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, T> pair in Tokens.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                sorted[pair.Key] = pair.Value;
            Tokens = sorted;
        }
    }

    /// <summary>颜色令牌：`#RRGGBB` 或 `#RRGGBBAA`（8bit 通道，schema `colorValue` 唯一格式）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeColorToken
    {
        [JsonProperty("value", Order = 0, Required = Required.Always)] public string Value;

        [JsonProperty("description", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }

    /// <summary>文字令牌（schema 必填：fontFamily/fontSize/lineHeight/weight；letterSpacing 可选）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeTypographyToken
    {
        [JsonProperty("fontFamily", Order = 0)] public string FontFamily;

        [JsonProperty("fontSize", Order = 1)] public float FontSize;

        [JsonProperty("lineHeight", Order = 2)] public float LineHeight;

        [JsonProperty("weight", Order = 3)] public string Weight;

        [JsonProperty("letterSpacing", Order = 4)] public float LetterSpacing;

        [JsonProperty("description", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }

    /// <summary>数值令牌（间距/圆角/图标尺寸）；`value` 必填（0 是合法值，故不能靠默认值判缺失）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeNumberToken
    {
        [JsonProperty("value", Order = 0, Required = Required.Always)] public float Value;

        [JsonProperty("description", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }

    /// <summary>层级令牌（颜色 + 偏移/模糊/扩散）；偏移与模糊必填（0 合法，不能靠默认值判缺失）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeElevationToken
    {
        [JsonProperty("color", Order = 0)] public string Color;

        [JsonProperty("offsetX", Order = 1, Required = Required.Always)] public float OffsetX;

        [JsonProperty("offsetY", Order = 2, Required = Required.Always)] public float OffsetY;

        [JsonProperty("blur", Order = 3, Required = Required.Always)] public float Blur;

        [JsonProperty("spread", Order = 4, Required = Required.Always)] public float Spread;

        [JsonProperty("description", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }

    /// <summary>动效令牌（时长毫秒，必填；0 合法）。</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class UiThemeMotionToken
    {
        [JsonProperty("durationMs", Order = 0, Required = Required.Always)] public float DurationMs;

        [JsonProperty("easing", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Easing;

        [JsonProperty("description", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Description;
    }
}
