using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UiTools.Contracts
{
    /// <summary>契约 JSON 的确定性读写：固定字段序、LF 换行、UTF-8 无 BOM、无时间戳（口径单源＝<see cref="UiDeterministicJson"/>）。</summary>
    public static class UiContractJson
    {
        public static string Serialize(UiPageContract contract) => UiDeterministicJson.Serialize(contract);

        public static UiPageContract Deserialize(string json) => UiDeterministicJson.Deserialize<UiPageContract>(json);
    }

    public static class UiContractPaths
    {
        public const string ContractDirectory = "Assets/LiteGame/Editor/UiTools/Contracts";

        public static string ForPage(string pageName) => ContractDirectory + "/" + pageName + ".contract.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UiValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UiContractStore
    {
        public static UiPageContract Load(string assetPath)
        {
            string full = UiContractPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("契约不存在：" + assetPath, full);
            return UiContractJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(string assetPath, UiPageContract contract)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            string full = UiContractPaths.FullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);
            File.WriteAllText(full, UiContractJson.Serialize(contract), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
