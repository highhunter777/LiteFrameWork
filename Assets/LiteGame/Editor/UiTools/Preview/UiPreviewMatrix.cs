using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LiteGame;
using LiteGame.Editor.UiTools;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>矩阵组合（一个待截图单元：设备档×语言×长文本×字体缩放）。</summary>
    public sealed class UiPreviewCombo
    {
        public string ProfileId;
        public string Locale;          // zh-CN | en | pseudo
        public bool LongText;          // 伪本地化（长文本）已在 Locale=pseudo 表达时此位保留扩展
        public float FontScale;
    }

    /// <summary>
    /// 预览矩阵计划器（P2-3 B3；工具链设计 §7.1「截图矩阵：中/英/长文本/安全区/设备档；交接证据，不是门禁替代」）。
    /// 纯函数面：组合展开与索引序列化可独立测试；执行态（Play 循环/截图/中断）在 editor action 状态机。
    /// </summary>
    public static class UiPreviewMatrix
    {
        /// <summary>默认语言集：中 / 英 / 伪本地化（长文本）。</summary>
        public static IReadOnlyList<string> DefaultLocales => UiPreviewLocale.SupportedLocales;

        /// <summary>默认字体缩放档（总设计 §7.5 钦定）。</summary>
        public static IReadOnlyList<float> DefaultFontScales => PreviewFontScale.SupportedScales;

        /// <summary>展开组合（页 × 入矩阵设备档 × 语言 × 字体缩放；确定性顺序：档 id Ordinal→语言→缩放）。</summary>
        public static List<UiPreviewCombo> Expand(UiDeviceProfileDocument document,
            IReadOnlyList<string> locales = null, IReadOnlyList<float> fontScales = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            IReadOnlyList<string> localeSet = locales == null || locales.Count == 0 ? DefaultLocales : locales;
            IReadOnlyList<float> scaleSet = fontScales == null || fontScales.Count == 0 ? DefaultFontScales : fontScales;
            var combos = new List<UiPreviewCombo>();
            foreach (UiDeviceProfile profile in UiDeviceProfileCatalog.MatrixProfiles(document))
            {
                foreach (string locale in localeSet)
                {
                    foreach (float scale in scaleSet)
                    {
                        combos.Add(new UiPreviewCombo
                        {
                            ProfileId = profile.Id,
                            Locale = locale,
                            LongText = string.Equals(locale, "pseudo", StringComparison.Ordinal),
                            FontScale = scale,
                        });
                    }
                }
            }
            return combos;
        }

        /// <summary>组合文件名（确定性、平台安全：&lt;profile&gt;_&lt;locale&gt;_&lt;scale&gt;.png）。</summary>
        public static string FileName(UiPreviewCombo combo)
        {
            if (combo == null) throw new ArgumentNullException(nameof(combo));
            string scale = combo.FontScale.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_');
            return $"{Sanitize(combo.ProfileId)}_{Sanitize(combo.Locale)}_{scale}.png";
        }

        /// <summary>语言是否不可用（矩阵据此判该组合失败，不假绿）。</summary>
        public static bool IsLocaleUnavailable(string locale, string status)
            => !string.IsNullOrEmpty(locale) && string.Equals(status, "unavailable", StringComparison.Ordinal);

        /// <summary>matrix.json 内容（组合清单/分辨率应用结果/时间/降级记录——确定性字段序、LF）。</summary>
        public static string BuildIndex(string pageName, IReadOnlyList<UiPreviewCombo> combos,
            IReadOnlyDictionary<string, bool> resolutionApplied, IReadOnlyList<string> degraded)
            => BuildIndexCore(pageName, combos, resolutionApplied, degraded, null, false,
                UiPreviewMatrixRunStatus.Completed, null);

        internal static string BuildIndexCore(string pageName, IReadOnlyList<UiPreviewCombo> combos,
            IReadOnlyDictionary<string, bool> resolutionApplied,
            IReadOnlyList<string> degraded, IReadOnlyDictionary<string, string> failures, bool interrupted,
            UiPreviewMatrixRunStatus status, IReadOnlyCollection<string> capturedFiles)
        {
            var index = new MatrixIndex
            {
                Page = pageName ?? string.Empty,
                CapturedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Status = status.ToString(),
                Interrupted = interrupted,
            };
            if (degraded != null) index.Degraded.AddRange(degraded);
            if (combos != null)
            {
                foreach (UiPreviewCombo combo in combos)
                {
                    if (combo == null) continue;
                    string file = FileName(combo);
                    string error = null;
                    failures?.TryGetValue(file, out error);
                    index.Combos.Add(new IndexCombo
                    {
                        File = file,
                        ProfileId = combo.ProfileId,
                        Locale = combo.Locale,
                        FontScale = combo.FontScale,
                        LongText = combo.LongText,
                        ResolutionApplied = resolutionApplied != null
                                             && resolutionApplied.TryGetValue(file, out bool applied) && applied,
                        Captured = capturedFiles == null
                            ? string.IsNullOrEmpty(error)
                            : capturedFiles.Contains(file),
                        Error = string.IsNullOrEmpty(error) ? null : error,
                    });
                }
            }
            return UiDeterministicJson.Serialize(index);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            char[] chars = value.Select(character =>
                char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_'
                    ? character
                    : '-').ToArray();
            return new string(chars);
        }

        private sealed class MatrixIndex
        {
            [JsonProperty("page", Order = 0)] public string Page;
            [JsonProperty("capturedAt", Order = 1)] public string CapturedAt;
            [JsonProperty("status", Order = 2)] public string Status;
            [JsonProperty("combos", Order = 3)] public List<IndexCombo> Combos = new List<IndexCombo>();
            [JsonProperty("degraded", Order = 4)] public List<string> Degraded = new List<string>();
            [JsonProperty("interrupted", Order = 5)] public bool Interrupted;
        }

        private sealed class IndexCombo
        {
            [JsonProperty("file", Order = 0)] public string File;
            [JsonProperty("profileId", Order = 1)] public string ProfileId;
            [JsonProperty("locale", Order = 2)] public string Locale;
            [JsonProperty("fontScale", Order = 3)] public float FontScale;
            [JsonProperty("longText", Order = 4)] public bool LongText;
            [JsonProperty("resolutionApplied", Order = 5)] public bool ResolutionApplied;
            [JsonProperty("captured", Order = 6)] public bool Captured;
            [JsonProperty("error", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
            public string Error;
        }
    }

    /// <summary>矩阵运行终态（失败隔离：单个组合失败不改变整体可完成；中断为独立终态）。</summary>
    public enum UiPreviewMatrixRunStatus
    {
        Running,
        Completed,
        CompletedWithFailures,
        Interrupted,
        Failed,
    }

    /// <summary>
    /// 矩阵运行态（P2-3 B3 / AC3）：逐组合记录成功/失败/降级，中断安全；索引由运行态统一产出。
    /// </summary>
    public sealed class UiPreviewMatrixRun
    {
        private readonly List<UiPreviewCombo> _combos;
        private readonly List<UiPreviewCombo> _succeeded = new List<UiPreviewCombo>();
        private readonly List<UiPreviewCombo> _failed = new List<UiPreviewCombo>();
        private readonly Dictionary<string, bool> _resolutionApplied = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _degraded = new List<string>();
        private bool _interrupted;
        private bool _runFailed;

        public UiPreviewMatrixRun(string pageName, IReadOnlyList<UiPreviewCombo> combos)
        {
            PageName = pageName ?? string.Empty;
            _combos = combos?.Where(combo => combo != null).ToList() ?? new List<UiPreviewCombo>();
        }

        public string PageName { get; }

        public UiPreviewMatrixRunStatus Status
        {
            get
            {
                if (_runFailed) return UiPreviewMatrixRunStatus.Failed;
                if (_interrupted) return UiPreviewMatrixRunStatus.Interrupted;
                int done = _succeeded.Count + _failed.Count;
                if (done < _combos.Count) return UiPreviewMatrixRunStatus.Running;
                return _failed.Count > 0
                    ? UiPreviewMatrixRunStatus.CompletedWithFailures
                    : UiPreviewMatrixRunStatus.Completed;
            }
        }

        public IReadOnlyList<UiPreviewCombo> Succeeded => _succeeded;
        public IReadOnlyList<UiPreviewCombo> Failed => _failed;
        public IReadOnlyList<string> Degraded => _degraded;

        public void RecordSuccess(UiPreviewCombo combo, bool resolutionApplied)
        {
            if (combo == null || _interrupted) return;
            string file = UiPreviewMatrix.FileName(combo);
            if (_errors.ContainsKey(file)) return;
            if (_succeeded.Any(item => string.Equals(UiPreviewMatrix.FileName(item), file, StringComparison.Ordinal))) return;
            _succeeded.Add(combo);
            _resolutionApplied[file] = resolutionApplied;
        }

        public void RecordFailure(UiPreviewCombo combo, string reason)
            => RecordFailure(combo, reason, false);

        public void RecordFailure(UiPreviewCombo combo, string reason, bool resolutionApplied)
        {
            if (combo == null || _interrupted) return;
            string file = UiPreviewMatrix.FileName(combo);
            if (_errors.ContainsKey(file)) return;
            _failed.Add(combo);
            _errors[file] = reason ?? string.Empty;
            _resolutionApplied[file] = resolutionApplied;
        }

        public void MarkDegraded(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && !_degraded.Contains(reason)) _degraded.Add(reason);
        }

        public void MarkInterrupted() => _interrupted = true;

        public void MarkFailed() => _runFailed = true;

        public string BuildIndex()
            => UiPreviewMatrix.BuildIndexCore(PageName, _combos, _resolutionApplied, _degraded, _errors, _interrupted,
                Status, _succeeded.Select(UiPreviewMatrix.FileName).ToList());
    }
}
