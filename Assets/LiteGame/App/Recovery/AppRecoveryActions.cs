using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 生产恢复动作：导出诊断到 <c>persistentDataPath/recovery/</c>；致命错误在 Player 内安全退出
    /// （编辑器保留现场，不 <c>Application.Quit</c>——排查优先）。
    /// 清缓存/回登录不在本件：缺内容缓存清理 API 与账号重入单点，随内容/会话接入补齐。
    /// </summary>
    public sealed class AppRecoveryActions : IRecoveryActions
    {
        private readonly string _root;

        public AppRecoveryActions(string root = null)
        {
            _root = string.IsNullOrEmpty(root)
                ? Path.Combine(Application.persistentDataPath, "recovery")
                : root;
        }

        public UniTask<string> ExportDiagnosticsAsync(string reason, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(_root);
                string path = Path.Combine(_root, $"recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
                var text = new StringBuilder()
                    .AppendLine("time=" + DateTime.UtcNow.ToString("O"))
                    .AppendLine("platform=" + Application.platform)
                    .AppendLine("version=" + Application.version)
                    .AppendLine("unity=" + Application.unityVersion)
                    .AppendLine("reason=" + reason)
                    .ToString();
                File.WriteAllText(path, text, Encoding.UTF8);
                Log.Error($"恢复诊断已导出：{path}", LogTag.Bootstrap);
                return UniTask.FromResult(path);
            }
            catch (Exception ex)
            {
                Log.Error($"恢复诊断导出失败（{reason}）：{ex.Message}", LogTag.Bootstrap);
                return UniTask.FromResult<string>(null);
            }
        }

        public UniTask ExitAsync(CancellationToken ct)
        {
            if (Application.isEditor)
            {
                Log.Error("恢复动作：安全退出（编辑器内跳过 Application.Quit，保留现场）", LogTag.Bootstrap);
                return UniTask.CompletedTask;
            }

            Log.Error("恢复动作：安全退出", LogTag.Bootstrap);
            Application.Quit();
            return UniTask.CompletedTask;
        }
    }
}
