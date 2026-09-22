using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using YooAsset;

namespace LiteGame
{
    /// <summary>
    /// 资源加载统一门面（静态豁免名单第四位——设计方案 §1.3"沿用方案 A 的 AssetService 静态门面"结论）。
    /// 业务只依赖此类，不直接依赖 YooAsset；换资源方案只改这一个文件。
    /// 契约（写给未来读代码的人）：
    /// ① location 统一使用资源完整路径（如 "Assets/LiteGame/RawFile/Config/demo_tbitem.bytes"）——手册 M2 坑位：RawFile location 用完整路径；
    /// ② UniTask 签名（§7.7），底层 YooAsset 3.0.5（经 UniTaskAssetExtensions 适配）；初始化必须先于一切加载（ProcedurePreload 驱动）；
    /// ③ EditorSimulateMode（编辑器开发）与 OfflinePlayMode（Player 内置包，C0-③）已交付；
    ///    Host/Web 模式与热更流程归 C1 内容更新线（试验件 YooAssetComponent 仅参考）；
    /// ④ 每个句柄用完 Release（本类内部完成），无句柄外泄；加载失败抛 InvalidOperationException 带 location——fail-fast 由流程 Fail() 接；
    /// ⑤ 主线程 only（YooAsset 操作无线程安全承诺，§7.4 同款纪律）。
    /// </summary>
    public static class AssetService
    {
        public const string DefaultPackageName = "DefaultPackage";

        private static ResourcePackage s_package;
        private static bool s_initialized;

        public static bool Initialized => s_initialized;

        /// <summary>已初始化的资源包（SceneService 等同程序集薄壳消费；业务禁止直接使用）。</summary>
        internal static ResourcePackage Package
        {
            get
            {
                ThrowIfNotInitialized();
                return s_package;
            }
        }

        /// <summary>
        /// 初始化资源包。由 ProcedurePreload 调用一次，重复调用幂等。
        /// Editor：EditorSimulateMode（虚拟构建 + 编辑器文件系统直读，零构建成本）。
        /// Player：OfflinePlayMode（C0-③）——内置包（StreamingAssets/yoo）加载，无网络下载；
        ///         StreamingAssets 无内置包时初始化失败 → 流程 Fail → 错误 UI（可诊断，不静默）。
        /// Host/Web 模式与热更流程归 C1 内容更新线（总设计 §8.1）。
        /// 共享尾段：Initialize → 请求版本 → 加载清单（模拟/离线均由对应文件系统应答，3.0 拆分）。
        /// </summary>
        public static async UniTask InitAsync(string packageName = DefaultPackageName, CancellationToken ct = default)
        {
            if (s_initialized) return;

#if UNITY_EDITOR
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (!YooAssets.TryGetPackage(packageName, out var package))
                package = YooAssets.CreatePackage(packageName);
            s_package = package;

            // 编辑器模拟：先执行模拟构建生成虚拟包根目录，再用编辑器文件系统加载
            PackageBuildResult simulateResult =
                EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
            InitializePackageOptions initializeOptions = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory),
            };
#else
            // Player：OfflinePlayMode——内置文件系统从 StreamingAssets 应答版本/清单/Bundle
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (!YooAssets.TryGetPackage(packageName, out var package))
                package = YooAssets.CreatePackage(packageName);
            s_package = package;

            InitializePackageOptions initializeOptions = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
            };
#endif

            await package.InitializePackageAsync(initializeOptions).AsUniTask(ct);

            RequestPackageVersionOperation versionOp = package.RequestPackageVersionAsync();
            await versionOp.AsUniTask(ct);

            LoadPackageManifestOperation manifestOp = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
            await manifestOp.AsUniTask(ct);

            s_initialized = true;
            Log.Info($"AssetService 就绪:package \"{packageName}\" version {package.GetPackageVersion()}", "Asset");
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

        /// <summary>加载原生文本（Lua 文件路径，M3 预载用）。</summary>
        public static UniTask<string> LoadRawFileTextAsync(string location, CancellationToken ct = default)
            => LoadRawFileCore(location, ct, raw => raw.text);

        /// <summary>
        /// 原生文件加载核心。**EditorSimulateMode 下走 TextAsset**——YooAsset 3.0.5 的模拟清单是单一
        /// BuildBundleType（VirtualAssetBundle），RawFileObject 只在 RawFile 管线可加载（实测 ABH 路径
        /// LoadAsset(RawFileObject) 恒 null）。.bytes/.txt 均为 TextAsset，收集器按 PackDirectory 打包；
        /// 混合包/原生文件策略随 M6 打包配置再定（M2实施指导 §实施记录）。
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
