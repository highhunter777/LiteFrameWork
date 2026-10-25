using System;
using System.Collections.Generic;
using System.IO;
using LiteGame.Editor.UiTools;
using LiteGame.Editor.UiTools.Validation;
using Newtonsoft.Json;
using UnityEditor;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>矩阵运行参数（窗口/评估入口共用；空集合＝默认集）。</summary>
    public sealed class UiPreviewMatrixOptions
    {
        public string PagePath;
        public IReadOnlyList<string> ProfileIds;
        public IReadOnlyList<string> Locales;
        public IReadOnlyList<float> FontScales;
        public string OutputDir;
        /// <summary>测试/故障注入：语言表目录覆盖（空＝ConfigService.DataDir）。</summary>
        public string LocaleDataDir;
    }

    /// <summary>
    /// 矩阵作业状态（跨 Play 域重载持久化到 SessionState）：阶段/游标/修订/组合/降级/失败。
    /// 纯数据；行为归 <see cref="UiPreviewMatrixRunner"/>。
    /// </summary>
    internal sealed class UiPreviewMatrixJobState
    {
        public UiPreviewMatrixJobState()
        {
        }

        [JsonProperty("pagePath", Order = 0)] public string PagePath;
        [JsonProperty("pageName", Order = 1)] public string PageName;
        [JsonProperty("outputDir", Order = 2)] public string OutputDir;
        [JsonProperty("originalScene", Order = 3)] public string OriginalScene;
        [JsonProperty("status", Order = 4)] public string Status;
        [JsonProperty("stage", Order = 5)] public string Stage;
        [JsonProperty("cursor", Order = 6)] public int Cursor;
        [JsonProperty("revision", Order = 7)] public int Revision;
        [JsonProperty("deadline", Order = 8)] public double Deadline;
        [JsonProperty("startedAt", Order = 9)] public string StartedAt;
        [JsonProperty("error", Order = 10, NullValueHandling = NullValueHandling.Ignore)] public string Error;
        [JsonProperty("degraded", Order = 11)] public List<string> Degraded = new List<string>();
        [JsonProperty("combos", Order = 12)] public List<UiPreviewMatrixComboState> Combos = new List<UiPreviewMatrixComboState>();
        [JsonProperty("originalGameViewWidth", Order = 13)] public int OriginalGameViewWidth;
        [JsonProperty("originalGameViewHeight", Order = 14)] public int OriginalGameViewHeight;
    }

    /// <summary>单个矩阵组合的作业快照（设备档/安全区/语言/字体档/产物/结果）。</summary>
    internal sealed class UiPreviewMatrixComboState
    {
        public UiPreviewMatrixComboState()
        {
        }

        [JsonProperty("profileId", Order = 0)] public string ProfileId;
        [JsonProperty("width", Order = 1)] public int Width;
        [JsonProperty("height", Order = 2)] public int Height;
        [JsonProperty("insetLeft", Order = 3)] public float InsetLeft;
        [JsonProperty("insetTop", Order = 4)] public float InsetTop;
        [JsonProperty("insetRight", Order = 5)] public float InsetRight;
        [JsonProperty("insetBottom", Order = 6)] public float InsetBottom;
        [JsonProperty("locale", Order = 7)] public string Locale;
        [JsonProperty("fontScale", Order = 8)] public float FontScale;
        [JsonProperty("longText", Order = 9)] public bool LongText;
        [JsonProperty("fileName", Order = 10)] public string FileName;
        [JsonProperty("resolutionApplied", Order = 11)] public bool ResolutionApplied;
        [JsonProperty("captured", Order = 12)] public bool Captured;
        [JsonProperty("failure", Order = 13, NullValueHandling = NullValueHandling.Ignore)] public string Failure;
    }

    /// <summary>矩阵作业的持久化/路径/组合转换单源（Runner 与窗口共用）。</summary>
    internal static class UiPreviewMatrixJobStore
    {
        private const string JobKey = "LiteGame.UiPreviewMatrix.Job";

        public static void Persist(UiPreviewMatrixJobState job)
            => SessionState.SetString(JobKey, UiDeterministicJson.Serialize(job));

        public static UiPreviewMatrixJobState Load()
        {
            string json = SessionState.GetString(JobKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                UiPreviewMatrixJobState job = UiDeterministicJson.Deserialize<UiPreviewMatrixJobState>(json);
                return job?.Combos == null || job.Combos.Count == 0 ? null : job;
            }
            catch (Exception)
            {
                SessionState.EraseString(JobKey);
                return null;
            }
        }

        public static void Clear() => SessionState.EraseString(JobKey);

        public static string FullPath(string relative)
            => Path.Combine(UiValidator.ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        public static string ScreenshotPath(UiPreviewMatrixJobState job, string fileName)
            => Path.Combine(FullPath(job.OutputDir), fileName);

        public static UiPreviewCombo ToCombo(UiPreviewMatrixComboState state)
            => new UiPreviewCombo
            {
                ProfileId = state.ProfileId,
                Locale = state.Locale,
                LongText = state.LongText,
                FontScale = state.FontScale,
            };
    }
}
