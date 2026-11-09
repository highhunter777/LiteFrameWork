using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// 烘焙数据产物 `.bin` 的导入器：把原生字节以 **Base64 文本**承载为 <see cref="TextAsset"/>。
    /// </summary>
    [ScriptedImporter(1, "bin")]
    public sealed class BinTextAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            var asset = new TextAsset(Convert.ToBase64String(bytes));
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
    }
}
