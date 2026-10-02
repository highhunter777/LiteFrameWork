using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace LiteGame.Editor
{
    /// <summary>
    /// 工程忽略规则：**复刻 NormalIgnoreRule 全部行为**，
    /// 唯一差异——`/LiteGame/Lua/` 下的 DefaultAsset（.lua）**放行收集**（否则 Lua 文件被
    /// "Default asset cannot be packed" 拒绝）。BundleCollectorSetting 包级
    /// `IgnoreRuleName` 切换为本规则。影响面：其余收集目录无 DefaultAsset
    /// （Config=.bytes / Scenes=.unity / GameMain/Configs）。
    /// </summary>
    public class LiteGameIgnoreRule : IAssetIgnoreRule
    {
        // 单源：引用运行时常量（LuaPreloader.LuaDir），目录改动只改一处
        private static readonly string LuaDir = LiteClient.LuaPreloader.LuaDir;

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

            // 忽略 Unity 引擎无法识别的文件——★唯一差异：Lua 目录放行（.lua 即 DefaultAsset）
            if (assetInfo.AssetType == typeof(UnityEditor.DefaultAsset))
            {
                if (assetInfo.AssetPath.StartsWith(LuaDir))
                    return false;
                UnityEngine.Debug.LogWarning($"Default asset cannot be packed: '{assetInfo.AssetPath}'.");
                return true;
            }

            return s_ignoreFileExtensions.Contains(assetInfo.FileExtension);
        }
    }
}
