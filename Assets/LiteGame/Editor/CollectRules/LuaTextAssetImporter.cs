using UnityEditor.AssetImporters;

namespace LiteGame.Editor
{
    /// <summary>
    /// .lua 的 TextAsset 导入器： .lua 无原生导入器（DefaultAsset），YooAsset 收集后 `LoadAssetAsync&lt;TextAsset&gt。
    /// </summary>
    [ScriptedImporter(1, "lua")]
    public class LuaTextAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = System.IO.File.ReadAllText(ctx.assetPath);
            var asset = new UnityEngine.TextAsset(text);
            ctx.AddObjectToAsset("main", asset);
        }
    }
}
