using System.IO;
using LiteGame.Toolchain;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 主题生成链的路径单源（《UI 编辑器工具链专项设计》§8.1/§8.3）：
    /// 令牌源 JSON 是**设计态源**，放编辑器目录、不进包；`UiThemeAsset` 是生成物，放 `GameData/Config/UI`（收集组内）。
    /// 两处路径只在本类声明，工具/规则/夹具都引用这里，不各自拼字符串。
    /// </summary>
    public static class UiThemePaths
    {
        /// <summary>令牌源（编辑器目录，不进包；`Assets/GameData/Config` 是 YooAsset 收集路径）。</summary>
        public const string DefaultJsonPath = "Assets/LiteGame/Editor/UiTools/UiStyleTokens.json";

        /// <summary>生成物（设计 §8.3 指定的运行时读面落点；单源＝<see cref="LiteClient.UI.UiThemePaths.DefaultAssetPath"/>——运行期装载与编辑器生成共用）。</summary>
        public const string DefaultAssetPath = LiteClient.UI.UiThemePaths.DefaultAssetPath;

        /// <summary>工程根绝对路径（资产/源码相对路径的解析基准，与校验核心同一单源）。</summary>
        public static string ProjectRoot => ToolchainManifestLoader.ProjectRoot;

        /// <summary>把工程根相对路径转为绝对路径。</summary>
        public static string FullPath(string assetPath)
            => Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
