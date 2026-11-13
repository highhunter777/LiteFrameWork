using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LiteGameNext.Editor
{
    /// <summary>内置 Bundle 批处理入口：反射调 `BuiltinBundleBuildInvoker.Run()`，绕开 delayCall 且不静态依赖旧树。</summary>
    public static class BundleBuildEntry
    {
        private const string InvokerType = "LiteGame.EditorTools.BuiltinBundleBuildInvoker, LiteGame.Editor";

        /// <summary>构建 Windows 档内置 Bundle；成功退出码 0，失败 1。</summary>
        public static void BuildWindows() => RunAndExit(BuildTarget.StandaloneWindows64);

        /// <summary>按 `-bundleTarget &lt;BuildTarget 名&gt;` 构建；缺省 Windows。</summary>
        public static void BuildFromArgs()
        {
            BuildTarget target = BuildTarget.StandaloneWindows64;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-bundleTarget") continue;
                if (Enum.TryParse(args[i + 1], out BuildTarget parsed)) target = parsed;
                break;
            }
            RunAndExit(target);
        }

        private static void RunAndExit(BuildTarget target)
        {
            int code = Run(target);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static int Run(BuildTarget target)
        {
            Type type = Type.GetType(InvokerType, throwOnError: false);
            if (type == null)
            {
                Debug.LogError("[BundleBuild] 找不到 BuiltinBundleBuildInvoker（LiteGame.Editor 未参与编译？）");
                return 1;
            }

            try
            {
                MethodInfo run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (run == null)
                {
                    Debug.LogError("[BundleBuild] BuiltinBundleBuildInvoker.Run 不存在");
                    return 1;
                }
                run.Invoke(null, new object[] { target });

                string status = type.GetProperty("Status", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;
                string output = type.GetProperty("LastOutputDirectory", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;
                Debug.Log("[BundleBuild] status=" + status + " out=" + output);
                return status == "success" ? 0 : 1;
            }
            catch (Exception ex)
            {
                Debug.LogError("[BundleBuild] 失败：" + (ex.InnerException ?? ex));
                return 1;
            }
        }
    }
}
