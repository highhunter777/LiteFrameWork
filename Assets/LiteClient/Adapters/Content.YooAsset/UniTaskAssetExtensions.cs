using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace LiteGame
{
    /// <summary>
    /// YooAsset 异步操作 → UniTask 适配（手册 9.0 横切件：YooAsset 回调 → UniTask）。
    /// 只在封装层内部使用（§7.7：业务拿到的是 UniTask 签名，不接触 YooAsset 句柄类型）。
    /// 取消语义：YooAsset 操作不支持中途取消——ct 在操作边界检查；等待中的操作不可取消，
    /// 流程级取消（离场）由 ProcedureBase 的 cts 在下一个 await 检查点生效。
    /// 注：3.0.5 里 HandleBase 不继承 AsyncOperationBase 且 Completed 事件按具体类型声明——故分两组重载。
    /// </summary>
    public static class UniTaskAssetExtensions
    {
        // ---- 操作族（InitializePackage / RequestPackageVersion / LoadPackageManifest / UnloadScene 等）----

        /// <summary>等待任意 YooAsset 操作完成；失败转 InvalidOperationException（带 YooAsset 错误信息）。</summary>
        public static UniTask<T> AsUniTask<T>(this T op, CancellationToken ct = default) where T : AsyncOperationBase
        {
            ct.ThrowIfCancellationRequested();
            if (op.IsDone)
            {
                ThrowIfFailed(op.Status, op.Error);
                return UniTask.FromResult(op);
            }

            var source = new UniTaskCompletionSource<T>();
            op.Completed += completed =>
            {
                var t = (T)completed;
                if (t.Status == EOperationStatus.Failed)
                    source.TrySetException(new InvalidOperationException($"YooAsset 操作失败:{t.Error}"));
                else
                    source.TrySetResult(t);
            };
            return source.Task;
        }

        // ---- 句柄族（AssetHandle / SceneHandle——Completed 为各自类型事件，须分开订阅）----

        public static UniTask<AssetHandle> AsUniTask(this AssetHandle handle, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (handle.IsDone)
            {
                ThrowIfFailed(handle.Status, handle.Error);
                return UniTask.FromResult(handle);
            }

            var source = new UniTaskCompletionSource<AssetHandle>();
            handle.Completed += h =>
            {
                if (h.Status == EOperationStatus.Failed)
                    source.TrySetException(new InvalidOperationException($"YooAsset 操作失败:{h.Error}"));
                else
                    source.TrySetResult(h);
            };
            return source.Task;
        }

        public static UniTask<SceneHandle> AsUniTask(this SceneHandle handle, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (handle.IsDone)
            {
                ThrowIfFailed(handle.Status, handle.Error);
                return UniTask.FromResult(handle);
            }

            var source = new UniTaskCompletionSource<SceneHandle>();
            handle.Completed += h =>
            {
                if (h.Status == EOperationStatus.Failed)
                    source.TrySetException(new InvalidOperationException($"YooAsset 操作失败:{h.Error}"));
                else
                    source.TrySetResult(h);
            };
            return source.Task;
        }

        private static void ThrowIfFailed(EOperationStatus status, string error)
        {
            if (status == EOperationStatus.Failed)
                throw new InvalidOperationException($"YooAsset 操作失败:{error}");
        }
    }
}
