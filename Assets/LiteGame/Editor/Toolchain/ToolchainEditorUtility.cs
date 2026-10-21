using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace LiteGame.Toolchain
{
    internal static class ToolchainEditorUtility
    {
        public static string ProjectRoot
        {
            get { return ToolchainManifestLoader.ProjectRoot; }
        }

        public static void OpenDocument(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                EditorUtility.DisplayDialog("工具文档", "该工具没有登记文档路径。", "OK");
                return;
            }

            string fullPath = Path.Combine(ProjectRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                EditorUtility.DisplayDialog("工具文档", "文档不存在：\n" + fullPath, "OK");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Toolchain] 打开文档失败：" + fullPath + " / " + exception.Message);
            }
        }

        public static bool ConfirmRun(EditorToolDescriptor descriptor)
        {
            if (!descriptor.NeedsConfirmation) return true;
            string scope = descriptor.Tool.destructive ? "标记为破坏性" : "会写入 " + descriptor.WritesLabel;
            return EditorUtility.DisplayDialog(
                "确认执行 " + descriptor.Title,
                descriptor.Summary + "\n\n" + scope + "，确定执行？",
                "执行",
                "取消");
        }

        public static void CopyCommand(string command)
        {
            GUIUtility.systemCopyBuffer = command ?? string.Empty;
            Debug.Log("[Toolchain] 已复制命令：" + command);
        }
    }
}
