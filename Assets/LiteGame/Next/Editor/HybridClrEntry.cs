using System;
using HybridCLR.Commands;
using HybridCLR.Installer;
using UnityEditor;
using UnityEngine;

namespace LiteGameNext.Editor
{
    /// <summary>HybridCLR 批处理入口（构建机/CI 以 -executeMethod 调用）；只经包内公开 API，不依赖旧树工具。</summary>
    public static class HybridClrEntry
    {
        /// <summary>安装 HybridCLR（已装则跳过）；失败以退出码 2 结束批处理。</summary>
        public static void Install()
        {
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR()) installer.InstallDefaultHybridCLR();
            bool ok = installer.HasInstalledHybridCLR();
            Debug.Log("[hclr] installed=" + ok);
            ExitIfBatch(ok ? 0 : 2);
        }

        /// <summary>打印安装状态；未装以退出码 2 结束批处理。</summary>
        public static void Status()
        {
            var installer = new InstallerController();
            bool ok = installer.HasInstalledHybridCLR();
            Debug.Log("[hclr] pkg=" + installer.PackageVersion
                + " hclr=" + installer.HybridclrLocalVersion
                + " il2cppPlus=" + installer.Il2cppPlusLocalVersion
                + " installed=" + ok);
            ExitIfBatch(ok ? 0 : 2);
        }

        /// <summary>生成全部热更产物（桥接/裁剪/链接/热更 dll）。</summary>
        public static void GenerateAll()
        {
            PrebuildCommand.GenerateAll();
            Debug.Log("[hclr] generate all ok");
        }

        private static void ExitIfBatch(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
