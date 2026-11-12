using System;
using System.Threading;
using Cysharp.Threading.Tasks;
#if !UNITY_EDITOR
using YooAsset;
#endif

namespace LiteGameNext.Platform.Content
{
    /// <summary>YooAsset 操作 → UniTask 的本地桥；Next 不引用旧 LiteFramework 扩展。</summary>
    internal static class NextAssetAwait
    {
#if !UNITY_EDITOR
        public static UniTask<T> Wait<T>(T op, CancellationToken ct) where T : AsyncOperationBase
        {
            ct.ThrowIfCancellationRequested();
            if (op.IsDone)
            {
                Fail(op.Status, op.Error);
                return UniTask.FromResult(op);
            }
            var source = new UniTaskCompletionSource<T>();
            op.Completed += completed =>
            {
                var value = (T)completed;
                if (value.Status == EOperationStatus.Failed)
                    source.TrySetException(new InvalidOperationException("YooAsset 操作失败:" + value.Error));
                else
                    source.TrySetResult(value);
            };
            return source.Task;
        }

        public static UniTask<AssetHandle> Wait(AssetHandle handle, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (handle.IsDone)
            {
                Fail(handle.Status, handle.Error);
                return UniTask.FromResult(handle);
            }
            var source = new UniTaskCompletionSource<AssetHandle>();
            handle.Completed += completed =>
            {
                if (completed.Status == EOperationStatus.Failed)
                    source.TrySetException(new InvalidOperationException("YooAsset 加载失败:" + completed.Error));
                else
                    source.TrySetResult(completed);
            };
            return source.Task;
        }

        private static void Fail(EOperationStatus status, string error)
        {
            if (status == EOperationStatus.Failed)
                throw new InvalidOperationException("YooAsset 操作失败:" + error);
        }
#endif
    }
}
