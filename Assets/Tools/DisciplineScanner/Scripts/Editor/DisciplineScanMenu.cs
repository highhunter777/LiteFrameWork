using System;
using System.Collections.Generic;
using System.Text;
using Tools.DisciplineScan;
using UnityEditor;
using UnityEngine;

namespace Tools.DisciplineScan.Editor
{
    /// <summary>
    /// 纪律扫描菜单（Editor 侧）：只负责定位项目根、跑引擎、输出与退出码。
    /// 规则与扫描逻辑全在 <see cref="DisciplineScanner"/>（零依赖，dotnet 测试共用同一引擎）。
    ///
    /// CI 入口：<c>Unity -batchmode -quit -executeMethod Tools.DisciplineScan.Editor.DisciplineScanMenu.RunCli</c>
    /// </summary>
    public static class DisciplineScanMenu
    {
        public static void ScanMenu()
        {
            List<KeyValuePair<string, LintViolation>> hits = Run();
            string report = Format(hits);

            if (hits.Count == 0)
            {
                Debug.Log("[DisciplineScanner] 纪律扫描通过（0 违规）。\n" + report);
                EditorUtility.DisplayDialog("纪律扫描", "通过：无违规。", "OK");
            }
            else
            {
                Debug.LogError("[DisciplineScanner] 纪律扫描发现 " + hits.Count + " 条违规：\n" + report);
                EditorUtility.DisplayDialog("纪律扫描", "发现 " + hits.Count + " 条违规，详见 Console。", "OK");
            }
        }

        /// <summary>工具平台入口：返回违规数与报告，不弹窗。</summary>
        public static (int violations, string report) RunForTool()
        {
            List<KeyValuePair<string, LintViolation>> hits = Run();
            return (hits.Count, Format(hits));
        }

        /// <summary>CI 入口：有违规退出码 1，否则 0。</summary>
        public static void RunCli()
        {
            List<KeyValuePair<string, LintViolation>> hits = Run();
            if (hits.Count == 0)
            {
                Console.WriteLine("[DisciplineScanner] lint OK");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.Error.WriteLine("[DisciplineScanner] lint FAILED: " + hits.Count + " violation(s)");
                Console.Error.WriteLine(Format(hits));
                EditorApplication.Exit(1);
            }
        }

        private static List<KeyValuePair<string, LintViolation>> Run()
        {
            string projectRoot = ProjectLocator.FindProjectRoot();
            return DisciplineScanner.ScanDefault(projectRoot);
        }

        private static string Format(List<KeyValuePair<string, LintViolation>> hits)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                sb.Append('[').Append(hits[i].Key).Append("] ").Append(hits[i].Value).Append('\n');
            }
            sb.Append("目标数：").Append(ScanTargets.Default.Length)
              .Append("；违规 ").Append(hits.Count).Append(" 条。");
            return sb.ToString();
        }
    }
}
