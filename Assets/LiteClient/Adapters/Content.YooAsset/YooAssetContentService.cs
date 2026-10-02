using System.Collections.Generic;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using YooAsset;

namespace LiteGame
{
    /// <summary>
    /// IContentService 的 YooAsset 适配（《商业级通用客户端框架总设计》§8.1"用可注入
    /// IContentService 取代静态所有权，YooAsset 作为后端" + §8.2 资源租约）：
    ///
    /// - **租约持有句柄**：AssetHandle 加载完成后不立即 Release——句柄由 <see cref="SharedLoadCoordinator{TKey,TAsset}"/>
    ///   持有到引用归零，租约 Dispose → handle.Release()（YooAsset 引用计数递减，实际卸载由包调度）。
    /// - **并发合并**：同 (代次, location, 类型) 的并发获取共享一次底层加载；单人取消不取消共享任务；
    ///   全员退出后迟到结果就地卸载（YooAsset 操作不可中途取消——UniTaskAssetExtensions 边界检查语义）。
    /// - **初始化**：委托 AssetService.InitAsync（幂等；EditorSimulate/Offline 与主链共用）——
    ///   Host 模式下载/校验/激活/回滚事务由 PatchCoordinator 承担。
    /// - **主线程 only**（YooAsset 操作无线程安全承诺——与 AssetService 同款纪律）。
    /// </summary>
    public sealed class YooAssetContentService : IContentService, IGenerationSink
    {
        /// <summary>加载键：(内容代次, location, 类型)——§8.2"加载与缓存键包含代次"。</summary>
        private readonly struct LoadKey : IEquatable<LoadKey>
        {
            public readonly ContentGeneration Generation;
            public readonly string Location;
            public readonly Type Type;

            public LoadKey(ContentGeneration generation, string location, Type type)
            {
                Generation = generation;
                Location = location;
                Type = type;
            }

            public bool Equals(LoadKey other)
                => Generation.Equals(other.Generation)
                   && string.Equals(Location, other.Location, StringComparison.Ordinal)
                   && Type == other.Type;

            public override bool Equals(object obj) => obj is LoadKey other && Equals(other);

            public override int GetHashCode()
                => Generation.GetHashCode()
                   ^ (Location == null ? 0 : StringComparer.Ordinal.GetHashCode(Location))
                   ^ (Type == null ? 0 : Type.GetHashCode());

            public override string ToString() => $"{Generation}|{Location}|{Type?.Name}";
        }

        private readonly SharedLoadCoordinator<LoadKey, AssetHandle> _loads;
        private ContentGeneration _generation = ContentGeneration.Default;
        private int _initialized;                                 // 0=未 1=已

        /// <summary>当前内容代次（诊断/激活事务切换点；切换由 PatchCoordinator 驱动）。</summary>
        public ContentGeneration CurrentGeneration => _generation;

        /// <summary>存活加载条目数（在途等待 + 有效持有——诊断/泄漏断言用）。</summary>
        public int LiveEntries => _loads.LiveEntryCount;

        public YooAssetContentService()
        {
            _loads = new SharedLoadCoordinator<LoadKey, AssetHandle>(LoadHandleAsync, UnloadHandle);
        }

        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            // 包根选择（真实资产包段）：激活记录给出**已确认候选**且候选资产包版本文件已就位时，
            // 以候选 `content/candidate/bundle` 为包根（绝对路径）；否则沿用内置包根（StreamingAssets/yoo）。
            string bundleRoot = ResolveConfirmedBundleRoot();
            await AssetService.InitAsync(ct: ct, builtinPackageRoot: bundleRoot);
            _generation = ContentGeneration.Default;
            Interlocked.Exchange(ref _initialized, 1);
        }

        /// <summary>
        /// 解析已确认候选的资产包根绝对路径；不满足（无候选身份/版本文件缺失）返回 null = 沿用内置包根。
        /// 激活记录经 <see cref="FileActivationRecordIO"/> 读（与 Patch 流程同一记录、同一 FileSys 根）。
        /// </summary>
        private static string ResolveConfirmedBundleRoot()
        {
            ActivationRecord record = new FileActivationRecordIO().TryLoad();
            if (!CandidateBundleRootResolver.IsCandidateBundleReady(record?.ConfirmedReleaseId,
                ReleaseLayout.CandidateBundleRootRelative, AssetService.DefaultPackageName, FileSys.Exists))
                return null;
            return string.Concat(UnityEngine.Application.persistentDataPath, "/",
                ReleaseLayout.CandidateBundleRootRelative);
        }

        /// <summary>
        /// 代次推进（<see cref="IGenerationSink"/>，《热更与内容发布专项设计》§9）。
        ///
        /// <see cref="PatchCoordinator"/> 在「确认提交」后推进到新代次、在「失败回退」时提示回已确认代次。
        /// **已持有的租约不受影响**：租约持有的是句柄，旧代次的键仍指向旧句柄，
        /// 新获取才走新代次（§9"旧 Scope 继续从自己的 generation 加载"）。
        /// </summary>
        public void Advise(ContentGeneration generation)
        {
            if (generation.ReleaseId == null) return;             // 未指定 = 不改动（防御：default 无身份）
            _generation = generation;
        }

        public async UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default, CancellationToken ct = default)
            where T : class
        {
            if (string.IsNullOrEmpty(location)) throw new ArgumentNullException(nameof(location));
            if (Volatile.Read(ref _initialized) == 0)
                throw new InvalidOperationException($"YooAssetContentService 未初始化——先 InitializeAsync（{location}）");

            // generation 为 default（未指定）= 取当前代；显式携带 = 固定代（旧 Scope 不从 Current 混取新内容，§8.2）
            ContentGeneration gen = generation.ReleaseId == null ? _generation : generation;

            AssetLease<AssetHandle> handleLease = await _loads.AcquireAsync(new LoadKey(gen, location, typeof(T)), ct);
            AssetHandle handle = handleLease.Asset;
            if (handle.AssetObject is T typed)
            {
                // 外层租约持有资产视图，释放转发到底层句柄租约（引用计数同一条链）
                return new AssetLease<T>(location, typed, _ => handleLease.Dispose());
            }

            handleLease.Dispose();                                // 类型不符：立即归还底层引用再显性失败
            throw new InvalidOperationException(
                $"资源类型不符:{location} 期望 {typeof(T).Name} 实得 {handle.AssetObject?.GetType().Name ?? "null"}");
        }

        /// <summary>按 tag 列内容路径（YooAsset 侧：<c>GetAssetInfos</c>；3.0.5 该重载即按 tag 查询）。</summary>
        public IReadOnlyList<string> ListAssetPathsByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return Array.Empty<string>();
            var infos = AssetService.Package.GetAssetInfos(tag);
            if (infos == null || infos.Length == 0) return Array.Empty<string>();
            var paths = new string[infos.Length];
            for (int i = 0; i < infos.Length; i++) paths[i] = infos[i].AssetPath;
            return paths;
        }

        public UniTask ShutdownAsync(CancellationToken ct = default)
        {
            _loads.Dispose();                                    // 释放面：已加载句柄 Release、在途尽力取消、迟到丢弃（幂等）
            Interlocked.Exchange(ref _initialized, 0);
            return UniTask.CompletedTask;
        }

        // ---- 底层加载/卸载（协调器回调，主线程）----

        private static async UniTask<AssetHandle> LoadHandleAsync(LoadKey key, CancellationToken ct)
        {
            AssetHandle handle = AssetService.Package.LoadAssetAsync(key.Location);
            try
            {
                await handle.AsUniTask(ct);
                return handle;
            }
            catch (OperationCanceledException)
            {
                handle.Release();                                // 取消路径：句柄立即归还（不留半截加载）
                throw;
            }
            catch (Exception ex)
            {
                handle.Release();
                throw new InvalidOperationException($"资源加载失败:{key.Location}", ex);   // 失败含 location（契约）
            }
        }

        private static void UnloadHandle(LoadKey key, AssetHandle handle)
        {
            handle.Release();                                    // YooAsset 引用计数递减——实际卸载由包调度
        }
    }
}
