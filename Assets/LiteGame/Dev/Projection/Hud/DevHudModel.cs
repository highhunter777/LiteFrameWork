#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>HUD 行色调（与渲染后端无关；IMGUI 回退时统一白色）。</summary>
    internal enum HudTone
    {
        Normal,
        Muted,
        Warn,
        Error,
    }

    /// <summary>一行读数：文本 + 色调 + 可选过滤标签（如日志 Tag / 模块名）。段件只产出数据，不接触具体渲染器。</summary>
    internal readonly struct HudLine
    {
        public readonly string Text;
        public readonly HudTone Tone;
        public readonly string Tag;

        public HudLine(string text, HudTone tone, string tag = null)
        {
            Text = text;
            Tone = tone;
            Tag = tag;
        }
    }

    /// <summary>段件渲染上下文（当前只有宽度；后续过滤/折叠态可扩）。</summary>
    internal readonly struct HudContext
    {
        public readonly int Width;

        public HudContext(int width) => Width = width;
    }

    /// <summary>段件的输出口：只收“行 + 色调”（tag 供 @tag 过滤）。</summary>
    internal interface IHudTextSink
    {
        void Line(string text, HudTone tone = HudTone.Normal, string tag = null);
    }

    /// <summary>DevHUD 段件：只读一个面，输出若干行；可见性由宿主/段自身决定。</summary>
    internal interface IDevHudSection
    {
        string Title { get; }
        int Order { get; }
        bool Visible { get; }
        void Append(IHudTextSink sink, in HudContext ctx);
    }

    /// <summary>一轮采样的段结果（宿主复用同一批实例，避免每轮建容器）。</summary>
    internal sealed class DevHudSectionData
    {
        public readonly string Title;
        public readonly DevHudSectionFlags Flag;
        public readonly List<HudLine> Lines = new List<HudLine>(32);
        public bool Visible = true;

        public DevHudSectionData(string title, DevHudSectionFlags flag)
        {
            Title = title;
            Flag = flag;
        }
    }
}
#endif
