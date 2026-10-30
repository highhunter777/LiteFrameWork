#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace LiteGame
{
    /// <summary>运行期 dev 覆盖件的系统 SDF 字体单源：DevHUD 与控制台共用同一候选链。
    /// 无字体文件可用时返回 null，由宿主决定降级方式（不在此抛）。</summary>
    internal static class DevUiToolkitFont
    {
        public static FontAsset CreateSdfFont()
        {
            foreach ((string path, int face) in Candidates())
            {
                try
                {
                    if (!System.IO.File.Exists(path)) continue;
                    FontAsset asset = FontAsset.CreateFontAsset(path, face, 90, 9,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                    if (asset != null) return asset;
                }
                catch (Exception)
                {
                    // 该字体文件不可读/格式不支持：尝试下一个
                }
            }
            return null;
        }

        private static (string path, int face)[] Candidates()
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor)
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                return new[]
                {
                    (System.IO.Path.Combine(dir, "msyh.ttc"), 0),   // 微软雅黑
                    (System.IO.Path.Combine(dir, "simhei.ttf"), 0), // 黑体
                    (System.IO.Path.Combine(dir, "simsun.ttc"), 0), // 宋体
                };
            }
            if (Application.platform == RuntimePlatform.OSXPlayer ||
                Application.platform == RuntimePlatform.OSXEditor)
            {
                return new[] { ("/System/Library/Fonts/PingFang.ttc", 0) };
            }
            return new[]
            {
                ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 0),
                ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 0),
            };
        }
    }
}
#endif
