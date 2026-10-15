using System.IO;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 部署烙入读取件（构建流水线的客户端半边）：<c>StreamingAssets/deploy.args</c> → 参数全文。
    ///
    /// **文件形态**：每行一个 <c>-key=value</c>（<see cref="MetaDeployConfig"/> 与
    /// <see cref="LiteClient.ContentDeployConfig"/> 的 token 形态——烙入文件就是**持久化的命令行参数**，
    /// 由 <c>scripts/build/build-player.ps1</c> 按 <c>BuildProfile.endpoints</c> 在构建前写入/清删）。
    /// **装载合并序**：命令行参数 > 本烙入 > 字段默认（信封 PSK 再往后仍有环境变量口）——
    /// 合并在两装载器各自的 <c>Load(args, bakedArgsText)</c> 纯函数内。
    ///
    /// **平台边界**：Standalone/Editor 的 streamingAssetsPath 是普通文件路径——直读成立。
    /// **Android 的 streamingAssetsPath 在 APK 内**，<see cref="File.Exists"/> 恒 false →
    /// 如实返回 null（无烙入，退化为命令行/环境变量装载），**不报错不猜**；Android 烙入
    /// 需 UnityWebRequest 读流——出现该平台分发需求时另批接（本件只服务桌面分发先行）。
    /// </summary>
    internal static class DeployBake
    {
        /// <summary>烙入文件名（StreamingAssets 下；内容 = 参数行流）。</summary>
        public const string FileName = "deploy.args";

        /// <summary>读烙入全文（缺文件/平台不可直读 → null = 无烙入）。</summary>
        public static string ReadArgs()
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
