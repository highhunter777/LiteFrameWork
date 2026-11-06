using System.Collections.Generic;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.SceneManagement;
using YooAsset;

namespace LiteFramework
{
    /// <summary>
    /// IContentService 的 YooAsset 适配（《商业级通用客户端框架总设计》§8.1"用可注入 IContentService 取代静态所有权，YooAsset 作为后端" + §8.2 资源租约）。
    /// </summary>
    public sealed class YooAssetContentService : IContentService, IGenerationSink, IContentLocationCatalog
    {
        /// <summary>默认资源包名（包创建/冒烟/候选根判定共用）。</summary>
        public const string DefaultPackageName = "DefaultPackage";

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
        private readonly IFileStore _files;
        private ResourcePackage _package;
        private ContentGeneration _generation = ContentGeneration.Default;
        private int _initialized;                                 // 0=未 1=已

        /// <summary>内容服务是否已就绪（初始化成功后为真；关闭后为假）。</summary>
        public bool IsInitialized => Volatile.Read(ref _initialized) == 1;

        /// <summary>当前内容代次（诊断/激活事务切换点；切换由 PatchCoordinator 驱动）。</summary>
        public ContentGeneration CurrentGeneration => _generation;

        /// <summary>存活加载条目数（在途等待 + 有效持有——诊断/泄漏断言用）。</summary>
        public int LiveEntries => _loads.LiveEntryCount;

        public YooAssetContentService(IFileStore files = null)
        {
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
            _loads = new SharedLoadCoordinator<LoadKey, AssetHandle>(LoadHandleAsync, UnloadHandle);
        }

        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (IsInitialized) return;                            // 幂等：重复初始化不改代次/不重建包
            // 包根选择（真实资产包段）：激活记录给出**已确认候选**且候选资产包版本文件已就位时， 以候选 `content/candidate/bundle` 为包根（绝对路径）；否则沿用内置包根（StreamingAssets/yoo）。
            ActivationRecord record = new FileActivationRecordIO(files: _files).TryLoad();
            string bundleRoot = ResolveConfirmedBundleRoot(record);
            await InitializePackageAsync(bundleRoot, ct);
            _generation = GenerationOf(record, bundleRoot);
            Interlocked.Exchange(ref _initialized, 1);
        }

        /// <summary>资源包初始化（编辑器模拟 / Player 内置包）：版本与清单就绪后置包句柄。</summary>
        private async UniTask InitializePackageAsync(string builtinPackageRoot, CancellationToken ct)
        {
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (!YooAssets.TryGetPackage(DefaultPackageName, out ResourcePackage package))
                package = YooAssets.CreatePackage(DefaultPackageName);

            // 另一实例正在初始化：等它结束后再决定认领（引导与用例可能同帧起跑）。
            if (package.InitializeStatus == EOperationStatus.Processing)
                await UniTask.WaitUntil(() => package.InitializeStatus != EOperationStatus.Processing, cancellationToken: ct);

            // 包已被其它实例初始化（引导与用例并存）：认领同一包，不重复初始化。
            if (package.PackageValid)
            {
                _package = package;
                UnityEngine.Debug.Log($"[Asset] ready package={DefaultPackageName} version={package.GetPackageVersion()} root={(string.IsNullOrEmpty(builtinPackageRoot) ? "builtin" : builtinPackageRoot)}");
                return;
            }

            InitializePackageOptions initializeOptions;
#if UNITY_EDITOR
            // 编辑器模拟：先执行模拟构建生成虚拟包根目录，再用编辑器文件系统加载。
            PackageBuildResult simulateResult =
                EditorSimulateBuildInvoker.Build(DefaultPackageName, (int)EBundleType.VirtualAssetBundle);
            initializeOptions = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory),
            };
#else
            // Player：OfflinePlayMode——内置文件系统从包根应答版本/清单/Bundle。
            // 包根二选一：非空 = 已确认候选的 bundle/ 镜像（自定义根）；空 = StreamingAssets 内置包。
            initializeOptions = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = string.IsNullOrEmpty(builtinPackageRoot)
                    ? FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                    : FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(builtinPackageRoot),
            };
#endif
            await package.InitializePackageAsync(initializeOptions).AsUniTask(ct);
            RequestPackageVersionOperation versionOp = package.RequestPackageVersionAsync();
            await versionOp.AsUniTask(ct);
            LoadPackageManifestOperation manifestOp = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
            await manifestOp.AsUniTask(ct);
            _package = package;

            // 冒烟标记（player-smoke.ps1 判据）：release Player 下 Log.Info 不落 Player.log，健康分支须用 Debug.Log 直打——非诊断残留，勿删（C1-⑩ 口径）。
            UnityEngine.Debug.Log($"[Asset] ready package={DefaultPackageName} version={package.GetPackageVersion()} root={(string.IsNullOrEmpty(builtinPackageRoot) ? "builtin" : builtinPackageRoot)}");
        }

        /// <summary>
        /// 解析已确认候选的资产包根绝对路径；不满足（无候选身份/版本文件缺失）返回 null = 沿用内置包根。
        /// 激活记录由调用方读一次后传入（与代次判定共用同一份记录，避免两次读盘得出不同结论）。
        /// </summary>
        private string ResolveConfirmedBundleRoot(ActivationRecord record)
        {
            if (!CandidateBundleRootResolver.IsCandidateBundleReady(record?.ConfirmedReleaseId,
                ReleaseLayout.CandidateBundleRootRelative, DefaultPackageName, _files.Exists))
                return null;
            return string.Concat(UnityEngine.Application.persistentDataPath, "/",
                ReleaseLayout.CandidateBundleRootRelative);
        }

        /// <summary>
        /// 启动代次 = 实际生效的已确认身份：以候选包根启动才用记录里的确认代次；
        /// 退回内置包根时一律 <see cref="ContentGeneration.Default"/>（不把“候选”写进加载键）。
        /// </summary>
        private static ContentGeneration GenerationOf(ActivationRecord record, string bundleRoot)
        {
            if (record == null || bundleRoot == null) return ContentGeneration.Default;
            string releaseId = string.IsNullOrEmpty(record.ConfirmedReleaseId)
                ? ContentGeneration.BuiltinReleaseId
                : record.ConfirmedReleaseId;
            return new ContentGeneration(releaseId, record.ConfirmedVersion);
        }

        /// <summary>
        /// 代次推进（<see cref="IGenerationSink"/>，《热更与内容发布专项设计》§9）。
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

            // generation 为 default（未指定）= 取当前代；显式携带 = 固定代（旧 Scope 不从 Current 混取新内容，§8.2）。
            ContentGeneration gen = generation.ReleaseId == null ? _generation : generation;

            AssetLease<AssetHandle> handleLease = await _loads.AcquireAsync(new LoadKey(gen, location, typeof(T)), ct);
            AssetHandle handle = handleLease.Asset;
            if (handle.AssetObject is T typed)
            {
                // 外层租约持有资产视图，释放转发到底层句柄租约（引用计数同一条链）。
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
            var infos = RequirePackage().GetAssetInfos(tag);
            if (infos == null || infos.Length == 0) return Array.Empty<string>();
            var paths = new string[infos.Length];
            for (int i = 0; i < infos.Length; i++) paths[i] = infos[i].AssetPath;
            return paths;
        }

        /// <summary>location 是否在当前代次清单内（<see cref="IContentLocationCatalog"/>；初始化后查询）。</summary>
        public bool IsLocationValid(string location)
            => !string.IsNullOrEmpty(location) && RequirePackage().IsLocationValid(location);

        /// <summary>场景句柄加载（SceneService 同程序集消费；未初始化抛）。</summary>
        internal SceneHandle LoadSceneAsync(string location, LoadSceneMode mode)
            => RequirePackage().LoadSceneAsync(location, mode);

        /// <summary>包句柄取件：未初始化即抛（内容问题显性失败，不静默给空）。</summary>
        private ResourcePackage RequirePackage()
        {
            ResourcePackage package = _package;
            if (Volatile.Read(ref _initialized) == 0 || package == null)
                throw new InvalidOperationException("内容服务未初始化——先 InitializeAsync（ProcedurePreload 驱动）");
            return package;
        }

        public UniTask ShutdownAsync(CancellationToken ct = default)
        {
            _loads.Dispose();                                    // 释放面：已加载句柄 Release、在途尽力取消、迟到丢弃（幂等）
            Interlocked.Exchange(ref _initialized, 0);
            return UniTask.CompletedTask;
        }

        // ---- 底层加载/卸载（协调器回调，主线程）----。

        private async UniTask<AssetHandle> LoadHandleAsync(LoadKey key, CancellationToken ct)
        {
            AssetHandle handle = RequirePackage().LoadAssetAsync(key.Location);
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
