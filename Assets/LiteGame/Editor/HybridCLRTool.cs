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
        public static void Status()
        {
            InstallerController installer = new InstallerController();
            Debug.Log($"[hclr] pkg={installer.PackageVersion} hclr={installer.HybridclrLocalVersion} "
                      + $"il2cppPlus={installer.Il2cppPlusLocalVersion} installed={installer.HasInstalledHybridCLR()}");
            ExitIfBatch(installer.HasInstalledHybridCLR() ? 0 : 1);
        }

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
