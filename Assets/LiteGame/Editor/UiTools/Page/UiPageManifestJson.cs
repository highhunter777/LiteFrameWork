using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>页面清单 JSON 的确定性读写（口径单源＝<see cref="UiDeterministicJson"/>）。</summary>
    public static class UiPageManifestJson
    {
        public static string Serialize(UiPageManifest manifest) => UiDeterministicJson.Serialize(manifest);

        public static UiPageManifest Deserialize(string json) => UiDeterministicJson.Deserialize<UiPageManifest>(json);
    }

    public static class UiPageManifestPaths
    {
        /// <summary>清单落点（《UI 编辑器工具链专项设计》§8.3）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UiTools/UiPageManifest.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UiValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UiPageManifestStore
    {
        public static UiPageManifest Load(string assetPath = UiPageManifestPaths.DefaultPath)
        {
            string full = UiPageManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("页面清单不存在：" + assetPath, full);
            return UiPageManifestJson.Deserialize(File.ReadAllText(full));
        }

        /// <summary>清单存在则读取；不存在返回 null（首次生成）。</summary>
        public static UiPageManifest LoadIfExists(string assetPath = UiPageManifestPaths.DefaultPath)
        {
            string full = UiPageManifestPaths.FullPath(assetPath);
            if (!File.Exists(full)) return null;
            return UiPageManifestJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(UiPageManifest manifest, string assetPath = UiPageManifestPaths.DefaultPath)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string full = UiPageManifestPaths.FullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);
            File.WriteAllText(full, UiPageManifestJson.Serialize(manifest), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
