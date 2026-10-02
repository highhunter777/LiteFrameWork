using UnityEditor.AssetImporters;

namespace LiteGame.Editor
{
    /// <summary>
    /// .lua 的 TextAsset 导入器：
    /// .lua 无原生导入器（DefaultAsset），YooAsset 收集后 `LoadAssetAsync&lt;TextAsset&gt;` 恒 null——
    /// 本导入器把 .lua 主对象声明为 TextAsset，Lua 预载链路（LuaPreloader → AssetService）随之打通。
    /// EditorSimulateMode 与真机 bundle 内的对象形态一致（都是导入产物）。
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
