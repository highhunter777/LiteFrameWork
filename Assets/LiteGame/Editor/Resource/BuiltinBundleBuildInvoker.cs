using System;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 内置 Bundle 包构建入口（C1：《商业级通用客户端框架总设计》§8.1 + 热更专项——
    /// Offline 健康路径的包来源。C0-③ 曾以双标记放行"错误 UI 分支"，本入口补齐"AssetService 就绪"分支的
    /// 可重复生产方式）。
    ///
    /// 流程：ScriptableBuildPipeline 真实构建 DefaultPackage（SBP 1.21.25）→
    /// BundledCopyOption.ClearAndCopyAll 把产物清空复制到 StreamingAssets/yoo →
    /// Player 构建时随包分发（OfflinePlayMode 内置文件系统应答）。
    ///
    /// 入口：①菜单（手工）②Run()（CI/Player 构建链可编程调用）。Player 构建前必须先跑本构建，
    /// 否则 StreamingAssets 缺 BuiltinCatalog → Preload 失败 → 错误 UI（确定错误态，设计内行为）。
    /// </summary>
    public static class BuiltinBundleBuildInvoker
    {
        private const string PackageName = "DefaultPackage";

        /// <summary>版本号每次构建唯一化（YooAsset 同版本输出目录已存在会拒绝重跑——ErrorCode115）。</summary>
        private static string NewVersion() =>
            "1.0-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

        [MenuItem("LiteGame/Resource/构建内置 Bundle 包（Offline）")]
        public static void BuildFromMenu()
        {
            Run();
            Debug.Log("[BuiltinBundle] 构建完成（详见 Console 构建日志）");
        }

        /// <summary>无轮询状态（Pipeline eval 主线程窗口 5s < SBP 构建时长——异步启动 + 轮询本状态）。</summary>
        public static string Status { get; private set; } = "idle";

        /// <summary>最近一次构建的**产物目录**（PackageRoot，含版本/清单/Bundle——候选打包的输入）。</summary>
        public static string LastOutputDirectory { get; private set; } = string.Empty;

        /// <summary>异步启动构建（EditorApplication.delayCall 下一帧执行；调用方轮询 <see cref="Status"/>）。</summary>
        public static void RunAsync()
        {
            Status = "starting";
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Status = "running";
                    Run();
                    Status = "success";
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Status = "failed: " + ex.Message;
                }
            };
        }

        /// <summary>
        /// 候选构建的**异步**入口（CLI/Pipeline 调用——同步入口超出 eval 主线程窗口；
        /// 完成后 <see cref="Status"/> = success/failed，产物目录见 <see cref="LastOutputDirectory"/>）。
        /// </summary>
        public static void RunCandidateAsync()
        {
            Status = "starting";
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Status = "running";
                    RunForCandidate(out _);
                    Status = "success";
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Status = "failed: " + ex.Message;
                }
            };
        }

        /// <summary>Windows Offline 包便捷入口（Pipeline eval/CI 调用——避免 eval 内联写 BuildTarget 类型名）。</summary>
        public static void RunStandaloneWindows64() => Run(BuildTarget.StandaloneWindows64);

        /// <summary>
        /// **候选专用入口**（样例①真实资产包段）：构建 DefaultPackage 但**不复制到 StreamingAssets**
        /// ——内置包保持既有版本与内容（否则内置包带上新收集组的内容，样例证据自毁）。
        /// 产物目录经 <paramref name="outputDirectory"/> 返回（同时记录到 <see cref="LastOutputDirectory"/>），
        /// 由发布脚本以 `publish-candidate.ps1 -BundleSource &lt;dir&gt;` 打成候选的 `bundle/` 段。
        /// </summary>
        [MenuItem("LiteGame/Resource/样例：构建候选 Bundle（不覆盖内置包）")]
        public static void RunForCandidateFromMenu()
        {
            RunForCandidate(out string dir);
            Debug.Log($"[BuiltinBundle] 候选构建完成（未覆盖内置包）：{dir}");
        }

        public static void RunForCandidate(out string outputDirectory)
            => RunCore(BuildTarget.StandaloneWindows64, EBundledCopyOption.None, out outputDirectory);

        /// <summary>同步构建（Unity 主线程调用——SBP 构建过程内部自管异步等待）。
        /// **显式目标平台**：跟随 activeBuildTarget 会在 C0-② 会话遗留 Android 目标时打出 ARM64 包进
        /// Windows Player（实测 02:18 踩坑）——Offline 冒烟的宿主是 Windows，目标固定并主动切换。</summary>
        public static void Run(BuildTarget buildTarget = BuildTarget.StandaloneWindows64)
            => RunCore(buildTarget, EBundledCopyOption.ClearAndCopyAll, out _);

        private static void RunCore(BuildTarget buildTarget, EBundledCopyOption copyOption, out string outputDirectory)
        {
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, buildTarget);
            }

            var buildParameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = nameof(ScriptableBuildPipeline),
                BuildBundleType = (int)EBundleType.AssetBundle,
                BuildTarget = buildTarget,
                PackageName = PackageName,
                PackageVersion = NewVersion(),
                PackageNote = "C1 offline builtin package",
                ClearBuildCacheFiles = false,          // 增量构建（缓存清理是排障手段，不走常规路径）
                UseAssetDependencyDB = true,           // 增量依赖库（构建提速）
                VerifyBuildingResult = true,           // 构建结果校验（§4 原则 10：先验证后提交）
                FileNameStyle = EFileNameStyle.HashName,
                BundledCopyOption = copyOption,
                BundledCopyParams = string.Empty,
            };

            var pipeline = new ScriptableBuildPipeline();
            BuildResult result = pipeline.Run(buildParameters, enableLog: true);

            if (!result.Success)
                throw new InvalidOperationException($"内置 Bundle 构建失败（{result.FailedTask}）：{result.ErrorInfo}");

            LastOutputDirectory = result.OutputPackageDirectory;
            outputDirectory = result.OutputPackageDirectory;
            Debug.Log($"[BuiltinBundle] 构建成功：package={PackageName} version={buildParameters.PackageVersion} target={buildTarget} copy={copyOption} output={result.OutputPackageDirectory}");

            // 候选路径（copy=None 会跳过 YooAsset 的 TaskCreateCatalog）补生成内置目录文件：
            // 运行时以候选包根初始化时，内置文件系统必读 `BuiltinCatalog.bytes`（文件名 → 包 GUID 映射）——
            // 缺它 = 激活后启动 404（实测）。目录生成写进**构建输出目录**（不碰 StreamingAssets，
            // "不覆盖内置包"语义保持）；镜像到候选 `bundle/**` 后随发布走。
            if (copyOption == EBundledCopyOption.None)
                CreateBuiltinCatalogInOutput(result.OutputPackageDirectory);
        }

        /// <summary>
        /// 在构建输出目录内生成 `BuiltinCatalog.bytes`/`.json`（YooAsset 内置目录文件）。
        /// 经反射调用 YooAsset 内部 `BuiltinCatalogHelper.CreateFile`——序列化格式的单一来源留在包内，
        /// 本工程不复制其二进制格式（包升级不脱节）；解密器同内置构建路径（不加密时为 null）。
        /// </summary>
        private static void CreateBuiltinCatalogInOutput(string outputDirectory)
        {
            Type helperType = Type.GetType("YooAsset.BuiltinCatalogHelper, YooAsset")
                ?? throw new InvalidOperationException("YooAsset.BuiltinCatalogHelper 未找到（包结构变更？）");
            System.Reflection.MethodInfo createFile = helperType.GetMethod("CreateFile",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("BuiltinCatalogHelper.CreateFile 未找到（包结构变更？）");
            bool ok = (bool)createFile.Invoke(null, new object[] { null, PackageName, outputDirectory });
            if (!ok) throw new InvalidOperationException($"候选内置目录生成失败：{outputDirectory}");
            Debug.Log($"[BuiltinBundle] 候选内置目录已生成：{outputDirectory}（BuiltinCatalog.bytes/.json）");
        }
    }
}
