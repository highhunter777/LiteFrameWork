using System;
using System.IO;
using LiteGame.Editor.UITools;
using LiteGame.Editor.UITools.Validation;
using UnityEditor;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>矩阵作业的持久化/路径/组合转换单源（Runner 与窗口共用）。</summary>
    internal static class UIPreviewMatrixJobStore
    {
        private const string JobKey = "LiteGame.UIPreviewMatrix.Job";

        public static void Persist(UIPreviewMatrixJobState job)
            => SessionState.SetString(JobKey, UIDeterministicJson.Serialize(job));

        public static UIPreviewMatrixJobState Load()
        {
            string json = SessionState.GetString(JobKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                UIPreviewMatrixJobState job = UIDeterministicJson.Deserialize<UIPreviewMatrixJobState>(json);
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
            => Path.Combine(UIValidator.ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        public static string ScreenshotPath(UIPreviewMatrixJobState job, string fileName)
            => Path.Combine(FullPath(job.OutputDir), fileName);

        public static UIPreviewCombo ToCombo(UIPreviewMatrixComboState state)
            => new UIPreviewCombo
            {
                ProfileId = state.ProfileId,
                Locale = state.Locale,
                LongText = state.LongText,
                FontScale = state.FontScale,
            };
    }
}
