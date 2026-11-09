using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace LiteGame.Editor
{
    /// <summary>
    /// 工程忽略规则：复刻 NormalIgnoreRule 全部行为（脚本资源收集面已随脚本宿主退出，无特殊放行）。
    /// </summary>
    public class LiteGameIgnoreRule : IAssetIgnoreRule
    {
        private static readonly HashSet<string> s_ignoreFileExtensions = new HashSet<string>()
            { "", ".so", ".cs", ".js", ".boo", ".meta", ".cginc", ".hlsl" };

        public bool IsIgnoreAsset(EditorAssetInfo assetInfo)
        {
            if (assetInfo.AssetPath.StartsWith("Assets/") == false && assetInfo.AssetPath.StartsWith("Packages/") == false)
            {
                UnityEngine.Debug.LogError($"Asset path is invalid: '{assetInfo.AssetPath}'.");
                return true;
            }

            // 忽略文件夹
            if (AssetDatabase.IsValidFolder(assetInfo.AssetPath))
                return true;

            // 忽略编辑器图标资源
            if (assetInfo.AssetPath.Contains("/Gizmos/"))
                return true;

            // 忽略编辑器专属资源
            if (assetInfo.AssetPath.Contains("/Editor/") || assetInfo.AssetPath.Contains("/Editor Resources/"))
                return true;

            // 忽略编辑器下的类型资源
            if (assetInfo.AssetType == typeof(LightingDataAsset))
                return true;
            if (assetInfo.AssetType == typeof(LightmapParameters))
                return true;

            // 忽略 Unity 引擎无法识别的文件
            if (assetInfo.AssetType == typeof(UnityEditor.DefaultAsset))
            {
                UnityEngine.Debug.LogWarning($"Default asset cannot be packed: '{assetInfo.AssetPath}'.");
                return true;
            }

            return s_ignoreFileExtensions.Contains(assetInfo.FileExtension);
        }
    }
}
