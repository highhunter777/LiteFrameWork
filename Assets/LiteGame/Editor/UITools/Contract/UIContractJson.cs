using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace LiteGame.Editor.UITools.Contracts
{
    /// <summary>契约 JSON 的确定性读写：固定字段序、LF 换行、UTF-8 无 BOM、无时间戳（口径单源＝<see cref="UIDeterministicJson"/>）。</summary>
    public static class UIContractJson
    {
        public static string Serialize(UIPageContract contract) => UIDeterministicJson.Serialize(contract);

        public static UIPageContract Deserialize(string json) => UIDeterministicJson.Deserialize<UIPageContract>(json);
    }

    public static class UIContractPaths
    {
        public const string ContractDirectory = "Assets/LiteGame/Editor/UITools/Contracts";

        public static string ForPage(string pageName) => ContractDirectory + "/" + pageName + ".contract.json";

        public static string FullPath(string assetPath)
            => Path.Combine(Validation.UIValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static class UIContractStore
    {
        public static UIPageContract Load(string assetPath)
        {
            string full = UIContractPaths.FullPath(assetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("契约不存在：" + assetPath, full);
            return UIContractJson.Deserialize(File.ReadAllText(full));
        }

        public static void Save(string assetPath, UIPageContract contract)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            string full = UIContractPaths.FullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string assetDir = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            bool folderKnown = string.IsNullOrEmpty(assetDir) || AssetDatabase.IsValidFolder(assetDir);
            // 原子替换：生成物半写会让契约对账/消费面读到坏 JSON。
            UIAtomicFile.WriteAllText(full, UIContractJson.Serialize(contract), new UTF8Encoding(false));
            if (!folderKnown) AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
