using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>
    /// 矩阵作业状态（跨 Play 域重载持久化到 SessionState）：阶段/游标/修订/组合/降级/失败。
    /// 纯数据；行为归 <see cref="UIPreviewMatrixRunner"/>。
    /// </summary>
    internal sealed class UIPreviewMatrixJobState
    {
        public UIPreviewMatrixJobState()
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
        [JsonProperty("combos", Order = 12)] public List<UIPreviewMatrixComboState> Combos = new List<UIPreviewMatrixComboState>();
        [JsonProperty("originalGameViewWidth", Order = 13)] public int OriginalGameViewWidth;
        [JsonProperty("originalGameViewHeight", Order = 14)] public int OriginalGameViewHeight;
    }
}
