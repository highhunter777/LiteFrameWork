using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Luban;
using cfg;
using LiteGame.Modules.Config;

namespace LiteGame
{
    /// <summary>
    /// 候选 config/ 段的激活前语义校验（H4-2）：
    /// 全量表字节 → 候选 cfg.Tables → ConfigTableValidator.Validate。
    ///
    /// **不按激活代次判断候选**：健康确认发生在确认提交之前，必须直接读候选根——否则探针永远
    /// 看到 builtin 而跳过。无 config 段（bundle-only）返回 null；不发布、不写盘、不碰已发布状态。
    /// </summary>
    public sealed class ConfigCandidateHealthProbe : IHealthProbe
    {
        private readonly IFileStore _files;
        private readonly string _candidateRoot;

        public ConfigCandidateHealthProbe(IFileStore files,
            string candidateRoot = ReleaseLayout.CandidateRootRelative)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
        }

        public string Name => "candidate-config-tables";

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (candidate == null) return UniTask.FromResult("候选为空");
            if (!CandidateConfigSource.HasConfigDirectory(_files, _candidateRoot))
                return UniTask.FromResult<string>(null);
            if (ct.IsCancellationRequested) return UniTask.FromResult("候选配置校验已取消");

            if (!CandidateConfigSource.TryReadAllAt(_files, _candidateRoot,
                    out IReadOnlyDictionary<string, byte[]> bytes, out string problem))
                return UniTask.FromResult(problem);

            try
            {
                var tables = new Tables(file =>
                {
                    if (!bytes.TryGetValue(file, out byte[] tableBytes) || tableBytes == null)
                        throw new InvalidOperationException($"候选配置缺表：{file}");
                    return new ByteBuf(tableBytes);
                });
                return UniTask.FromResult(ConfigTableValidator.Validate(tables));
            }
            catch (Exception ex)
            {
                return UniTask.FromResult("候选配置建表失败：" + ex.Message);
            }
        }
    }
}
