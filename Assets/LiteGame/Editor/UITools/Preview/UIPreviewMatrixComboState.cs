using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>单个矩阵组合的作业快照（设备档/安全区/语言/字体档/产物/结果）。</summary>
    internal sealed class UIPreviewMatrixComboState
    {
        public UIPreviewMatrixComboState()
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
}
