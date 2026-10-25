using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UiTools.Validation;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>设备档安全区 inset（px，相对该设备档分辨率）。</summary>
    public sealed class UiSafeInsets
    {
        [JsonProperty("left", Order = 0)] public float Left;
        [JsonProperty("top", Order = 1)] public float Top;
        [JsonProperty("right", Order = 2)] public float Right;
        [JsonProperty("bottom", Order = 3)] public float Bottom;
    }

    /// <summary>设备档（总设计 §7.3：分辨率/方向/安全区/DPI；数据单源＝JSON，代码零字面量）。</summary>
    public sealed class UiDeviceProfile
    {
        [JsonProperty("id", Order = 0)] public string Id;
        [JsonProperty("name", Order = 1)] public string Name;
        [JsonProperty("width", Order = 2)] public int Width;
        [JsonProperty("height", Order = 3)] public int Height;
        [JsonProperty("orientation", Order = 4)] public string Orientation;      // landscape | portrait
        [JsonProperty("aspect", Order = 5)] public float Aspect;                 // 宽/高（校验器核对派生一致性）
        [JsonProperty("dpi", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
        public float? Dpi;                                                       // 信息性（布局档不消费，留运行期预算面）
        [JsonProperty("safeInsets", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
        public UiSafeInsets SafeInsets;
        [JsonProperty("notes", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
        public string Notes;
        [JsonProperty("inMatrix", Order = 9)] public bool InMatrix;              // 竖屏预留等默认不入矩阵
    }

    /// <summary>设备档案文档（schemaVersion + 档位列表）。</summary>
    public sealed class UiDeviceProfileDocument
    {
        [JsonProperty("schemaVersion", Order = 0)] public string SchemaVersion;
        [JsonProperty("profiles", Order = 1)] public List<UiDeviceProfile> Profiles = new List<UiDeviceProfile>();
    }

    /// <summary>
    /// 设备档案读面（《UI 编辑器工具链专项设计》§7.1「设备档在预览工具栏统一切换；不在多个场景复制配置」）：
    /// JSON 单源＋校验＋确定性排序（按 id Ordinal、无时间戳——沿 UiStyleTokens 先例）。
    /// 禁静态缓存（同 UiTokenCatalog 口径）：每次操作装载一次、实例下传。
    /// </summary>
    public static class UiDeviceProfileCatalog
    {
        public const string DefaultJsonPath = "Assets/LiteGame/Editor/UiTools/UiDeviceProfiles.json";

        private const float AspectTolerance = 0.001f;

        private static readonly Regex IdPattern =
            new Regex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

        /// <summary>按路径装载（缺省＝DefaultJsonPath）；文件缺失/非法抛异常（不伪造空档）。</summary>
        public static UiDeviceProfileDocument Load(string jsonPath = null)
        {
            string path = string.IsNullOrEmpty(jsonPath) ? DefaultJsonPath : jsonPath;
            string full = Path.IsPathRooted(path)
                ? path
                : Path.Combine(UiValidator.ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) throw new FileNotFoundException("设备档案不存在：" + path, full);
            return Parse(File.ReadAllText(full));
        }

        /// <summary>解析 JSON 文本（夹具用）；未知字段/重复键/非法 JSON 显式失败。</summary>
        public static UiDeviceProfileDocument Parse(string json)
        {
            UiDeviceProfileDocument document = UiDeterministicJson.DeserializeStrict<UiDeviceProfileDocument>(json);
            if (document == null) throw new InvalidOperationException("设备档案 JSON 解析为空");
            document.Profiles = document.Profiles ?? new List<UiDeviceProfile>();
            SortProfiles(document);
            return document;
        }

        /// <summary>校验（一次收集全部问题）：ID 文法、宽高比与分辨率一致、inset 非负且不越分辨率、方向枚举。</summary>
        public static IReadOnlyList<string> Validate(UiDeviceProfileDocument document)
        {
            var issues = new List<string>();
            if (document == null)
            {
                issues.Add("设备档案为空");
                return issues;
            }
            if (string.IsNullOrWhiteSpace(document.SchemaVersion)) issues.Add("schemaVersion 缺失");
            if (document.Profiles == null || document.Profiles.Count == 0)
            {
                issues.Add("profiles 不能为空");
                return issues;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < document.Profiles.Count; index++)
            {
                UiDeviceProfile profile = document.Profiles[index];
                if (profile == null)
                {
                    issues.Add($"profiles[{index}] 为空");
                    continue;
                }
                string label = string.IsNullOrWhiteSpace(profile.Id) ? $"profiles[{index}]" : profile.Id;

                if (string.IsNullOrWhiteSpace(profile.Id) || !IdPattern.IsMatch(profile.Id))
                    issues.Add($"{label}: id 文法非法（小写字母/数字/点/横线）");
                else if (!seenIds.Add(profile.Id))
                    issues.Add($"{label}: id 重复");

                if (string.IsNullOrWhiteSpace(profile.Name)) issues.Add($"{label}: name 为空");
                if (profile.Width <= 0 || profile.Height <= 0)
                {
                    issues.Add($"{label}: 宽高必须为正");
                }
                else if (profile.Orientation == "landscape" && profile.Width <= profile.Height)
                {
                    issues.Add($"{label}: landscape 档宽度必须大于高度");
                }
                else if (profile.Orientation == "portrait" && profile.Height <= profile.Width)
                {
                    issues.Add($"{label}: portrait 档高度必须大于宽度");
                }

                if (profile.Orientation != "landscape" && profile.Orientation != "portrait")
                    issues.Add($"{label}: orientation 非法（landscape|portrait）");

                if (profile.Aspect <= 0f)
                {
                    issues.Add($"{label}: aspect 必须为正");
                }
                else if (profile.Width > 0 && profile.Height > 0)
                {
                    float derived = profile.Width / (float)profile.Height;
                    if (Math.Abs(derived - profile.Aspect) > AspectTolerance)
                        issues.Add($"{label}: aspect 与宽高派生不一致（声明 {profile.Aspect:0.######}，派生 {derived:0.######}）");
                }

                if (profile.Dpi.HasValue && profile.Dpi.Value <= 0f)
                    issues.Add($"{label}: dpi 必须为正");

                if (profile.SafeInsets == null)
                {
                    issues.Add($"{label}: safeInsets 缺失");
                    continue;
                }

                UiSafeInsets insets = profile.SafeInsets;
                if (insets.Left < 0f || insets.Top < 0f || insets.Right < 0f || insets.Bottom < 0f)
                    issues.Add($"{label}: inset 不能为负");
                if (profile.Width > 0 && (insets.Left > profile.Width || insets.Right > profile.Width
                                          || insets.Left + insets.Right > profile.Width))
                    issues.Add($"{label}: 左右 inset 越界");
                if (profile.Height > 0 && (insets.Top > profile.Height || insets.Bottom > profile.Height
                                           || insets.Top + insets.Bottom > profile.Height))
                    issues.Add($"{label}: 上下 inset 越界");
            }

            return issues;
        }

        /// <summary>规范文本（按 id Ordinal 排序、LF、无时间戳——确定性对账用）。</summary>
        public static string Serialize(UiDeviceProfileDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Profiles = document.Profiles ?? new List<UiDeviceProfile>();
            SortProfiles(document);
            return UiDeterministicJson.Serialize(document);
        }

        /// <summary>按 id 取档；未知返回 false（不猜）。</summary>
        public static bool TryGet(UiDeviceProfileDocument document, string id, out UiDeviceProfile profile)
        {
            profile = document?.Profiles?
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id, id, StringComparison.Ordinal));
            return profile != null;
        }

        /// <summary>入矩阵的档位（InMatrix=true，按 id 排序）。</summary>
        public static IReadOnlyList<UiDeviceProfile> MatrixProfiles(UiDeviceProfileDocument document)
        {
            if (document?.Profiles == null) return Array.Empty<UiDeviceProfile>();
            return document.Profiles
                .Where(profile => profile != null && profile.InMatrix)
                .OrderBy(profile => profile.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static void SortProfiles(UiDeviceProfileDocument document)
        {
            document.Profiles.Sort((left, right) =>
                string.CompareOrdinal(left?.Id, right?.Id));
        }
    }
}
