using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>页面清单 JSON 的确定性读写（口径单源＝<see cref="UIDeterministicJson"/>）。</summary>
    public static class UIPageManifestJson
    {
        public static string Serialize(UIPageManifest manifest) => UIDeterministicJson.Serialize(manifest);

        public static UIPageManifest Deserialize(string json) => UIDeterministicJson.Deserialize<UIPageManifest>(json);
    }

    public static class UIPageManifestPaths
    {
        /// <summary>清单落点（《UI 编辑器工具链专项设计》§8.3）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UITools/UIPageManifest.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UIValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UIPageManifestStore
    {
        public static UIPageManifest Load(string assetPath = UIPageManifestPaths.DefaultPath)
        {
            string full = UIPageManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("页面清单不存在：" + assetPath, full);
            return UIPageManifestJson.Deserialize(File.ReadAllText(full));
        }

        /// <summary>清单存在则读取；不存在返回 null（首次生成）。</summary>
        public static UIPageManifest LoadIfExists(string assetPath = UIPageManifestPaths.DefaultPath)
        {
            string full = UIPageManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) return null;
            return UIPageManifestJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(UIPageManifest manifest, string assetPath = UIPageManifestPaths.DefaultPath)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string full = UIPageManifestPaths.FullPath(assetPath);
            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);

            // 共享原子写：同目录临时件 + 替换（失败重试/退化为覆盖写），不留半截 JSON。
            UIAtomicFile.WriteAllText(full, UIPageManifestJson.Serialize(manifest), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
