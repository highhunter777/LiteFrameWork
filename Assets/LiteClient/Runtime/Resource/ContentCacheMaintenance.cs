using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteClient
{
    /// <summary>受控内容缓存清理结果（恢复链诊断面——如实记录删了什么、记录是否回退）。</summary>
    public readonly struct ContentCacheClearResult
    {
        public ContentCacheClearResult(bool cleared, int deletedFiles, long deletedBytes, bool rolledBackToBuiltin)
        {
            Cleared = cleared;
            DeletedFiles = deletedFiles;
            DeletedBytes = deletedBytes;
            RolledBackToBuiltin = rolledBackToBuiltin;
        }

        /// <summary>本次是否真的删了产物（false = 目录本就为空/不存在，幂等调用）。</summary>
        public bool Cleared { get; }

        /// <summary>删除的文件数（删除前清点）。</summary>
        public int DeletedFiles { get; }

        /// <summary>删除的字节数（删除前清点）。</summary>
        public long DeletedBytes { get; }

        /// <summary>激活记录是否已回退 builtin（下一次启动确定使用内置内容）。</summary>
        public bool RolledBackToBuiltin { get; }

        public override string ToString()
            => $"cleared={Cleared} files={DeletedFiles} bytes={DeletedBytes} rollback={RolledBackToBuiltin}";
    }

    /// <summary>
    /// 受控内容缓存清理端口（恢复动作「清缓存」的实现面，《客户端依赖注入专项设计》§6.2）：
    /// 清的是**下载产物**（候选根 `config/lua/bundle`），不是内置包根；清理后激活记录回退 builtin，
    /// 下一次启动以内置内容确定可用（再按发布通道重新取候选）。
    /// </summary>
    public interface IContentCacheMaintenance
    {
        /// <summary>清空受控内容缓存并把激活记录回退 builtin（幂等）。</summary>
        UniTask<ContentCacheClearResult> ClearAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 受控内容缓存清理（《热更与内容发布专项设计》§8 失败隔离；恢复动作面）。
    ///
    /// **顺序不可颠倒**：先 <see cref="IContentService.ShutdownAsync"/> 释放运行时句柄（候选包根正指向
    /// 待删目录——带着句柄删文件会留下半删状态），再删候选根，最后把激活记录回退 builtin：
    /// 记录若不回退，下次启动仍声称"已确认候选"，而文件已不在，恢复链会把用户留在不确定态。
    ///
    /// **调用语境**：只在"运行时内容可丢弃"的恢复语境调用（启动失败/错误页阻断态）；运行中的对局/页面
    /// 不得调用——本类不重建内容，重启由流程决定。内置包根（StreamingAssets）不在删除范围。
    /// </summary>
    public sealed class ContentCacheMaintenance : IContentCacheMaintenance
    {
        /// <summary>激活记录回退原因（写入 <see cref="ActivationRecord.LastFailure"/>，可诊断）。</summary>
        public const string RollbackReason = "恢复动作：清理受控内容缓存";

        private readonly IContentService _content;
        private readonly IFileStore _files;
        private readonly ActivationTransactionStore _activations;

        public ContentCacheMaintenance(IContentService content, IFileStore files,
            ActivationTransactionStore activations)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        }

        public async UniTask<ContentCacheClearResult> ClearAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // ① 释放运行时句柄（幂等）——候选包根的 YooAsset 句柄必须先归零再删文件
            await _content.ShutdownAsync(ct);

            // ② 清点后删除候选根（下载产物；不存在 = 空清，幂等）
            string[] files = _files.GetFilesRecursive(ReleaseLayout.CandidateRootRelative);
            long bytes = 0;
            for (int i = 0; i < files.Length; i++)
            {
                long length = _files.GetFileLength(files[i]);
                if (length > 0) bytes += length;
            }
            _files.DeleteDirectory(ReleaseLayout.CandidateRootRelative);

            // ③ 激活记录回退 builtin（记录与磁盘一致；序号/完整性照常落盘）
            _activations.RollbackToBuiltin(RollbackReason);

            var result = new ContentCacheClearResult(files.Length > 0, files.Length, bytes, true);
            Log.Info($"受控内容缓存已清理（{result}）——下次启动以内置内容为准", LogTag.Content);
            return result;
        }
    }
}
