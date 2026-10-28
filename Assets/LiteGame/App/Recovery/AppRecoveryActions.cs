using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 生产恢复动作：导出诊断到 <c>persistentDataPath/recovery/</c>；致命错误在 Player 内安全退出
    /// （编辑器保留现场，不 <c>Application.Quit</c>——排查优先）；清受控内容缓存委托内容域
    /// <see cref="IContentCacheMaintenance"/>（未注入 = 该动作不可用，如实返回 false，不假装成功）。
    /// </summary>
    public sealed class AppRecoveryActions : IRecoveryActions
    {
        private readonly string _root;
        private readonly IContentCacheMaintenance _cache;

        /// <param name="root">诊断落盘根（默认 <c>persistentDataPath/recovery</c>；测试可注入临时目录）。</param>
        /// <param name="cache">受控内容缓存清理（可空 = 该恢复动作不可用——装配点应注入）。</param>
        public AppRecoveryActions(string root = null, IContentCacheMaintenance cache = null)
        {
            _root = string.IsNullOrEmpty(root)
                ? Path.Combine(Application.persistentDataPath, "recovery")
                : root;
            _cache = cache;
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

        public async UniTask<bool> ClearContentCacheAsync(CancellationToken ct)
        {
            if (_cache == null)
            {
                Log.Error("恢复动作：受控内容缓存清理未接入（装配点未注入 IContentCacheMaintenance）", LogTag.Bootstrap);
                return false;
            }

            try
            {
                ContentCacheClearResult result = await _cache.ClearAsync(ct);
                Log.Error($"恢复动作：受控内容缓存已清理（{result}）", LogTag.Bootstrap);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"恢复动作：受控内容缓存清理失败：{ex.Message}", LogTag.Bootstrap);
                return false;
            }
        }
    }
}
