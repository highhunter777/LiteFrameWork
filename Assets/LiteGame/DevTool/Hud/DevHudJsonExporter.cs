#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LiteGame
{
    /// <summary>F6 导出的入参（DevHUD 采样结果 + 过滤态），导出器不反向读宿主。</summary>
    internal sealed class DevHudExportContext
    {
        public string Session = "";
        public string Backend = "uitk";
        public bool Visible = true;
        public int Left;
        public int Top;
        public int Width;
        public int Font;
        public float Scale = 1f;
        public int LogFilter;
        public bool OnlyMatch;
        public string FilterRaw = "";
        public string FilterNeedle = "";
        public IReadOnlyList<string> FilterTags;
        public IReadOnlyList<DevHudSectionData> Sections;
    }

    /// <summary>DevHUD 读数 JSON 导出（schema devhud/1）；手写转义，避免 JsonUtility 的字段限制。</summary>
    internal static class DevHudJsonExporter
    {
        public static string Export(DevHudExportContext ctx)
        {
            string directory = Path.Combine(Application.persistentDataPath, "diagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                "hud-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + ".json");

            var sb = new StringBuilder(8192);
            sb.Append("{\"schema\":\"devhud/1\",\"tsUtc\":");
            AppendJsonString(sb, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            sb.Append(",\"session\":");
            AppendJsonString(sb, ctx.Session);
            sb.Append(",\"backend\":\"").Append(ctx.Backend).Append('"');
            sb.Append(",\"visible\":").Append(ctx.Visible ? "true" : "false");
            sb.Append(",\"hud\":{\"left\":").Append(ctx.Left)
              .Append(",\"top\":").Append(ctx.Top)
              .Append(",\"width\":").Append(ctx.Width)
              .Append(",\"font\":").Append(ctx.Font)
              .Append(",\"scale\":").Append(ctx.Scale.ToString("0.##", CultureInfo.InvariantCulture))
              .Append(",\"logFilter\":").Append(ctx.LogFilter)
              .Append(",\"onlyMatch\":").Append(ctx.OnlyMatch ? "true" : "false").Append('}');

            sb.Append(",\"filter\":{\"raw\":");
            AppendJsonString(sb, ctx.FilterRaw);
            sb.Append(",\"needle\":");
            AppendJsonString(sb, ctx.FilterNeedle);
            sb.Append(",\"tags\":[");
            if (ctx.FilterTags != null)
            {
                for (int i = 0; i < ctx.FilterTags.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonString(sb, ctx.FilterTags[i]);
                }
            }
            sb.Append("]}");

            sb.Append(",\"sections\":[");
            if (ctx.Sections != null)
            {
                for (int i = 0; i < ctx.Sections.Count; i++)
                {
                    DevHudSectionData data = ctx.Sections[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"title\":");
                    AppendJsonString(sb, data.Title);
                    sb.Append(",\"visible\":").Append(data.Visible ? "true" : "false");
                    sb.Append(",\"lines\":[");
                    for (int j = 0; j < data.Lines.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        sb.Append("{\"tone\":\"").Append(data.Lines[j].Tone).Append('"');
                        sb.Append(",\"tag\":");
                        AppendJsonString(sb, data.Lines[j].Tag);
                        sb.Append(",\"text\":");
                        AppendJsonString(sb, data.Lines[j].Text);
                        sb.Append('}');
                    }
                    sb.Append("]}");
                }
            }
            sb.Append("]}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
#endif
