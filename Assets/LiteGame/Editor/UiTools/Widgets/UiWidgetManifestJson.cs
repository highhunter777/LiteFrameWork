using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UiTools.Widgets
{
    /// <summary>控件清单 JSON 的确定性读写（口径单源＝<see cref="UiDeterministicJson"/>）。</summary>
    public static class UiWidgetManifestJson
    {
        public static string Serialize(UiWidgetManifest manifest) => UiDeterministicJson.Serialize(manifest);

        public static UiWidgetManifest Deserialize(string json)
            => UiDeterministicJson.Deserialize<UiWidgetManifest>(json);
    }

    public static class UiWidgetManifestPaths
    {
        /// <summary>清单落点（《UI 编辑器工具链专项设计》§8.3）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UiValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UiWidgetManifestStore
    {
        public static UiWidgetManifest Load(string assetPath = UiWidgetManifestPaths.DefaultPath)
        {
            string full = UiWidgetManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("控件清单不存在：" + assetPath, full);
            return UiWidgetManifestJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(UiWidgetManifest manifest, string assetPath = UiWidgetManifestPaths.DefaultPath)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string full = UiWidgetManifestPaths.FullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);
            File.WriteAllText(full, UiWidgetManifestJson.Serialize(manifest), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
