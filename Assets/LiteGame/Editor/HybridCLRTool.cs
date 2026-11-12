using System;
using HybridCLR.Commands;
using HybridCLR.Installer;
using HybridCLR.Settings;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.Toolchain
{
    /// <summary>HybridCLR 安装/状态/生成入口；构建机与 CI 以 -executeMethod 调用。</summary>
    public static class HybridCLRTool
    {
        /// <summary>热更程序集清单（边界＝Sim/Sync/产品层；AOT 面见路线图 §5.3）。</summary>
        private static readonly string[] HotAssemblies =
        {
            "LiteSim",
            "LiteSync",
            "LiteGame.Core",
            "LiteGame.Shared",
            "LiteGame.Battle.Abstractions",
            "LiteGame.Modules.Battle",
            "LiteGame.Modules.Camera",
            "LiteGame.Modules.Composition",
            "LiteGame.Modules.Config",
            "LiteGame.Modules.Dev",
            "LiteGame.Modules.Input",
            "LiteGame.Modules.Map",
            "LiteGame.Modules.Meta",
            "LiteGame.Modules.Procedure",
            "LiteGame.Modules.Session",
            "LiteGame.Modules.Settings",
            "LiteGame.Modules.UI",
            "LiteGame.Adapters.Dev",
            "LiteGame.Adapters.Luban",
            "LiteGame.Adapters.Unity",
            "LiteGame.App",
            "LiteGameNext",
            "RoomServer.Runtime",
            "RoomServer.Application.Runtime",
        };

        [MenuItem("LiteGame/HybridCLR/Mark Hot Assemblies")]
        public static void MarkHotAssemblies()
        {
            HybridCLRSettings settings = HybridCLRSettings.instance;
            settings.enable = true;
            settings.hotUpdateAssemblies = HotAssemblies;
            HybridCLRSettings.Save();
            Debug.Log($"[hclr] hot assemblies: {string.Join(", ", HotAssemblies)}");
            ExitIfBatch(0);
        }

        [MenuItem("LiteGame/HybridCLR/Generate For Commit")]
        public static void GenerateForCommit()
        {
            InstallerController installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR())
            {
                Debug.LogError("[hclr] not installed; run: python scripts/build/hybridclr.py install");
                ExitIfBatch(2);
                return;
            }
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            CompileDllCommand.CompileDll(target);
            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(target);
            Debug.Log("[hclr] link/def generated for commit");
            ExitIfBatch(0);
        }

        [MenuItem("LiteGame/HybridCLR/Status")]
        public static void Status()
        {
            InstallerController installer = new InstallerController();
            Debug.Log($"[hclr] pkg={installer.PackageVersion} hclr={installer.HybridclrLocalVersion} "
                      + $"il2cppPlus={installer.Il2cppPlusLocalVersion} installed={installer.HasInstalledHybridCLR()}");
            ExitIfBatch(installer.HasInstalledHybridCLR() ? 0 : 1);
        }

        [MenuItem("LiteGame/HybridCLR/Install")]
        public static void Install()
        {
            InstallerController installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR())
            {
                installer.InstallDefaultHybridCLR();
            }
            Debug.Log($"[hclr] installed={installer.HasInstalledHybridCLR()}");
            ExitIfBatch(installer.HasInstalledHybridCLR() ? 0 : 2);
        }

        public static void GenerateAll()
        {
            InstallerController installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR())
            {
                Debug.LogError("[hclr] not installed; run: python scripts/build/hybridclr.py install");
                ExitIfBatch(2);
                return;
            }
            HybridCLRSettings settings = HybridCLRSettings.instance;
            if (settings.hotUpdateAssemblies == null || settings.hotUpdateAssemblies.Length == 0)
            {
                Debug.LogWarning("[hclr] hotUpdateAssemblies is empty (P2 尚未落地)");
            }
            PrebuildCommand.GenerateAll();
            Debug.Log("[hclr] generate all ok");
            ExitIfBatch(0);
        }

        private static void ExitIfBatch(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
