using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UITools.Widgets
{
    /// <summary>控件清单 JSON 的确定性读写（口径单源＝<see cref="UIDeterministicJson"/>）。</summary>
    public static class UIWidgetManifestJson
    {
        public static string Serialize(UIWidgetManifest manifest) => UIDeterministicJson.Serialize(manifest);

        public static UIWidgetManifest Deserialize(string json)
            => UIDeterministicJson.Deserialize<UIWidgetManifest>(json);
    }

    public static class UIWidgetManifestPaths
    {
        /// <summary>清单落点（《UI 编辑器工具链专项设计》§8.3）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UITools/UIWidgetManifest.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UIValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UIWidgetManifestStore
    {
        public static UIWidgetManifest Load(string assetPath = UIWidgetManifestPaths.DefaultPath)
        {
            string full = UIWidgetManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("控件清单不存在：" + assetPath, full);
            return UIWidgetManifestJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(UIWidgetManifest manifest, string assetPath = UIWidgetManifestPaths.DefaultPath)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string full = UIWidgetManifestPaths.FullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);
            File.WriteAllText(full, UIWidgetManifestJson.Serialize(manifest), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
