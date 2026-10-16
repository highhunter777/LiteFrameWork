using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace LiteGame.Editor
{
    /// <summary>
    /// 工程忽略规则：**复刻 NormalIgnoreRule 全部行为**，差异两处——
    /// ①`/LiteGame/Lua/` 下的 DefaultAsset（.lua）**放行收集**（否则 Lua 文件被
    /// "Default asset cannot be packed" 拒绝）；②<see cref="DevAssetExclusions"/>：**dev-only 资产排除**
    /// （《UI工作台设计》§5"发布剥离口径"W2——Scenes 收集组对 `Assets/Scenes` 是整目录 CollectAll，
    /// 工作台场景的排除只能落在本规则）。
    /// BundleCollectorSetting 包级 `IgnoreRuleName` 切换为本规则。影响面：其余收集目录无 DefaultAsset
    /// （Config=.bytes / Scenes=.unity / GameMain/Configs）。
    /// </summary>
    public class LiteGameIgnoreRule : IAssetIgnoreRule
    {
        /// <summary>dev-only 资产（路径单源；**只列 dev 件**——产品场景/资源不得入列）。
        /// 每条路径必须真实存在：EditMode 守卫 <c>WorkbenchDevAssetExclusionEditModeTests</c> 断言其存在，
        /// 改名/移动即红——防"排除名单静默失效"。</summary>
        internal static readonly string[] DevAssetExclusions =
        {
            "Assets/Scenes/UiWorkbench.unity",     // UI 工作台场景（dev-only，编辑器 Play 使用——设计 §1/§5）
        };

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

            // dev-only 资产排除（精确路径匹配；见 DevAssetExclusions）
            foreach (string devPath in DevAssetExclusions)
                if (assetInfo.AssetPath == devPath)
                    return true;

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
