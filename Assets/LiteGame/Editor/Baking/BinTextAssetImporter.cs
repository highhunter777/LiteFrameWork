using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// 烘焙数据产物 `.bin` 的导入器：把原生字节以 **Base64 文本**承载为 <see cref="TextAsset"/>。
    ///
    /// **为什么需要它**：`.bin` 在引擎里是 DefaultAsset——YooAsset 收集器拒绝打包
    /// （"Default asset cannot be packed"），运行期 `LoadAssetAsync` 也取不到字节；
    /// 本引擎版本的 TextAsset 没有 byte[] 构造，无法直接承载任意二进制。
    /// 因此导入层做一次无损 Base64 转写，内容包内仍是 TextAsset（与 `.bytes` 表数据同一通道），
    /// 运行时读 `text` → Base64 解码 → 数据装载器（WeaponGripCatalog.Load 等）。
    ///
    /// **文件本体保持原生二进制**：磁盘上的 `grip.bin` 就是紧凑二进制格式（测试/发布摘要直接对字节算 SHA-256），
    /// Base64 只存在于导入产物（Assets 之外）。同内容重烘 → 同一 Base64 → 资产不变。
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
