using System.Collections.Generic;
using System.Text;
using LiteGame.Editor;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 资源与模板完整性（L2 子集，2026-09-18 补；《待办总览》§5-27）。
    ///
    /// 为什么归 L2：这类"资源/装配坏了"的问题 **L1 结构性看不见**（dotnet 按路径 glob 编译、不读 .meta、
    /// 不实例化 prefab）——与 2026-09-15 的非法 meta 事故同属一层。
    ///
    /// 范围取舍：只做**不返工**的子集（UI 模板 + prefab 完整性）。装配冒烟（GameEntry→ProcedureMain）
    /// 明确留 M11——SimView/控制器/地图尚未定形，此刻写必然重写。
    /// </summary>
    public sealed class AssetIntegrityEditModeTests : UnityTestBase
    {
        private const string WidgetDir = "Assets/UI/Widgets";   // 2026-09-19：UI 已从 LiteGame 迁到顶层 Assets/UI

        [Test]
        [Category(TestCategory.Asset)]
        public void UI模板_全部可加载且无缺失脚本()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WidgetDir });
            Assert.Greater(guids.Length, 0, $"{WidgetDir} 下没有模板 prefab——目录或收集规则坏了");

            var bad = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null)
                {
                    bad.Add($"{path}：加载失败");
                    continue;
                }

                int missing = 0;
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) missing++;   // null 组件 = 脚本引用丢失（Missing Script）
                }
                if (missing > 0) bad.Add($"{path}：缺失脚本 {missing} 处");
            }

            var sb = new StringBuilder();
            foreach (string b in bad) sb.Append('\n').Append(b);
            Assert.IsEmpty(bad, $"模板 prefab 共 {guids.Length} 个，问题 {bad.Count} 个：{sb}");
        }

        [Test]
        [Category(TestCategory.Asset)]
        public void UI控件模板_自检全通过()
        {
            // 与菜单「LiteGame/UI/校验控件模板」同源（WidgetPrefabCheck.RunAll）——不重复实现断言
            var (pass, fail) = WidgetPrefabCheck.RunAll();
            Assert.Greater(pass, 0, "自检未执行任何断言——检查项被清空或模板目录变了");
            Assert.Zero(fail, $"控件模板自检失败 {fail} 项（通过 {pass} 项）");
        }
    }
}
