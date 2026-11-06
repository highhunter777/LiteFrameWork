using System.Collections.Generic;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>矩阵运行参数（窗口/评估入口共用；空集合＝默认集）。</summary>
    public sealed class UIPreviewMatrixOptions
    {
        public string PagePath;
        public IReadOnlyList<string> ProfileIds;
        public IReadOnlyList<string> Locales;
        public IReadOnlyList<float> FontScales;
        public string OutputDir;
        /// <summary>测试/故障注入：语言表目录覆盖（空＝ConfigService.DataDir）。</summary>
        public string LocaleDataDir;
    }
}
