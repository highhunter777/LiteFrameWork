using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using YooAsset;

namespace LiteFramework
{
    /// <summary>
    /// 资源加载统一门面（沿用 AssetService 静态门面）。
    /// </summary>
    public static class AssetService
    {
        public const string DefaultPackageName = "DefaultPackage";

        private static ResourcePackage s_package;
        private static bool s_initialized;

        public static bool Initialized => s_initialized;

        /// <summary>已初始化的资源包（SceneService 等同程序集内部消费；业务禁止直接使用）。</summary>
        internal static ResourcePackage Package
        {
            get
            {
                ThrowIfNotInitialized();
                return s_package;
            }
        }

        /// <summary>
        /// 初始化资源包。
        /// </summary>
        public static async UniTask InitAsync(string packageName = DefaultPackageName,
            CancellationToken ct = default, string builtinPackageRoot = null)
        {
            if (s_initialized) return;

#if UNITY_EDITOR
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (!YooAssets.TryGetPackage(packageName, out var package))
                package = YooAssets.CreatePackage(packageName);
            s_package = package;

            // 编辑器模拟：先执行模拟构建生成虚拟包根目录，再用编辑器文件系统加载。
            PackageBuildResult simulateResult =
                EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
            InitializePackageOptions initializeOptions = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory),
            };
#else
            // Player：OfflinePlayMode——内置文件系统从包根应答版本/清单/Bundle。
            // 包根二选一：非空 = 已确认候选的 bundle/ 镜像（自定义根）；空 = StreamingAssets 内置包。
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (!YooAssets.TryGetPackage(packageName, out var package))
                package = YooAssets.CreatePackage(packageName);
            s_package = package;

            InitializePackageOptions initializeOptions = new OfflinePlayModeOptions
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

            s_initialized = true;
            // 冒烟标记（player-smoke.ps1 判据）：release Player 下 Log.Info 不落 Player.log，
            // 健康分支须用 Debug.Log 直打——非诊断残留，勿删（C1-⑩ 口径）。
            UnityEngine.Debug.Log($"[Asset] ready package={packageName} version={package.GetPackageVersion()} root={(string.IsNullOrEmpty(builtinPackageRoot) ? "builtin" : builtinPackageRoot)}");
        }

        /// <summary>加载资源对象。失败抛（含 location），句柄内部 Release。</summary>
        public static async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfNotInitialized();
            AssetHandle handle = s_package.LoadAssetAsync<T>(location);
            try
            {
                await handle.AsUniTask(ct);
                if (handle.AssetObject is T asset) return asset;
                throw new InvalidOperationException(
                    $"资源类型不符:{location} 期望 {typeof(T).Name} 实得 {handle.AssetObject?.GetType().Name ?? "null"}");
            }
            finally
            {
                handle.Release();
            }
        }

        /// <summary>加载 RawFile 二进制（配置 .bytes 主路径）。失败抛（含 location）。</summary>
        public static UniTask<byte[]> LoadRawFileBytesAsync(string location, CancellationToken ct = default)
            => LoadRawFileCore(location, ct, raw => raw.bytes);

        /// <summary>加载原生文本（Lua 文件路径，预载用）。</summary>
        public static UniTask<string> LoadRawFileTextAsync(string location, CancellationToken ct = default)
            => LoadRawFileCore(location, ct, raw => raw.text);

        /// <summary>
        /// 原生文件加载核心。
        /// </summary>
        private static async UniTask<T> LoadRawFileCore<T>(string location, CancellationToken ct, Func<TextAsset, T> extract)
        {
            ThrowIfNotInitialized();
            AssetHandle handle = s_package.LoadAssetAsync<TextAsset>(location);
            try
            {
                await handle.AsUniTask(ct);
                if (handle.AssetObject is TextAsset raw) return extract(raw);
                throw new InvalidOperationException($"原生文件加载失败:{location} 实得 {handle.AssetObject?.GetType().Name ?? "null"}");
            }
            finally
            {
                handle.Release();
            }
        }

        private static void ThrowIfNotInitialized()
        {
            if (!s_initialized)
                throw new InvalidOperationException("AssetService 未初始化——InitAsync 必须先于一切加载（ProcedurePreload 驱动）");
        }
    }
}
