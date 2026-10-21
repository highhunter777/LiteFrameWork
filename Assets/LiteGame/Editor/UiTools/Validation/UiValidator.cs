using System.Collections.Generic;
using System.IO;
using LiteGame.Editor.UiTools.Validation.Rules;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation
{
    /// <summary>
    /// UI 校验核心（《UI编辑器工具专项设计》§4）：跑注册的规则集 → <see cref="UiReport"/>。
    ///
    /// **唯一核心，三入口共用**（§4 明文"同一校验核心供菜单、窗口和自动测试调用"）：
    /// 菜单（<c>LiteGame/UI/校验/校验 UI 资产</c>）、CLI（<c>-executeMethod … RunCli</c>，供 CI）、
    /// EditMode 用例。规则按 <see cref="IUiRule"/> 端口注册，核心只遍历、不写死规则。
    ///
    /// **未接入的规则族在报告里显式列出**（§4："未实现的规则标识为待接入，不显示为自动通过"）——
    /// 六族名单见 <see cref="Families"/>，已接入者由实际规则填充。
    /// </summary>
    public static class UiValidator
    {
        /// <summary>UI 源码扫描面（视觉单一来源规则用——与《修改指南》§7 约束面一致）。</summary>
        public static readonly string[] SourceScanDirs =
        {
            "Assets/LiteClient/Runtime",
            "Assets/LiteGame/App",
            "Assets/LiteFramework/Scripts",
            "Assets/LiteView",
        };

        /// <summary>UI 资产扫描面（模板 prefab 所在，供资产面规则用）。</summary>
        public const string ScreensDir = "Assets/ShareResource/UI/Screens";
        public const string WidgetsDir = "Assets/ShareResource/UI/Widgets";
        public const string HudDir = "Assets/ShareResource/UI/Hud";

        /// <summary>规则族全表（§4 六族）。未实现的族以 <c>implemented=false</c> 登记，报告"待接入"栏。</summary>
        public static readonly UiRuleFamily[] Families =
        {
            new UiRuleFamily("绑定", true, "命名文法/前缀词表/私有件（§9.2/§9.4）+ 生成物 hash 对账（§2.1.5）——已接入；BindName 同文/同页同词/编号连续性待接入"),
            new UiRuleFamily("资源", true, "表内资源路径存在性（.prefab/.unity 通用反射面——步 5 接入）；缺引用其余子项/收集组/图集字体名册/生产包排除待接入"),
            new UiRuleFamily("文本", false, "key/占位符/语言列、富文本白名单、fallback 与缺字、样式覆盖"),
            new UiRuleFamily("渲染/适配", true, "视觉单一来源（运行时不得构建视觉，§7）——首条已接入"),
            new UiRuleFamily("行为", false, "复用视觉、输入穿透、焦点、列表重绑、语言变更、取消"),
            new UiRuleFamily("性能", false, "rebuild/批次/GC/打开分段耗时（Profiler/Player，非静态正则）"),
        };

        /// <summary>规则注册表（按此顺序执行；后续步逐条追加）。</summary>
        private static readonly IUiRule[] Rules =
        {
            new RuntimeVisualRule(),
            new BindingNamingRule(),
            new UiBindingHashRule(),
            new AssetTablePathRule(),
        };

        /// <summary>工程根（Assets 之上）。</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>跑全部规则（构造默认上下文——源码面 + 资产面）。</summary>
        public static UiReport Run()
        {
            return Run(BuildDefaultContext());
        }

        /// <summary>跑全部规则（显式上下文——测试可喂合成输入）。</summary>
        public static UiReport Run(UiValidationContext context)
        {
            var report = new UiReport();
            foreach (var family in Families) report.RegisterFamily(family);
            foreach (var rule in Rules) report.AddRange(rule.Check(context));
            return report;
        }

        /// <summary>默认上下文：源码扫描面 + UI 资产（Screens/Widgets/Hud）路径集。</summary>
        public static UiValidationContext BuildDefaultContext()
        {
            var assets = new List<string>();
            assets.AddRange(FindPrefabs(ScreensDir));
            assets.AddRange(FindPrefabs(WidgetsDir));
            assets.AddRange(FindPrefabs(HudDir));
            return new UiValidationContext(ProjectRoot, SourceScanDirs, assets);
        }

        private static IEnumerable<string> FindPrefabs(string dir)
        {
            var paths = new List<string>();
            if (!AssetDatabase.IsValidFolder(dir)) return paths;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
            foreach (var guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }

        // ---------------- 入口：菜单 / CLI ----------------

        private static void MenuValidate()
        {
            var report = Run();
            if (report.Passed)
                Debug.Log("[UI 校验] 通过\n" + report.Render());
            else
                Debug.LogError("[UI 校验] 未通过\n" + report.Render());
        }

        /// <summary>
        /// CLI 入口（CI 用）：<c>-executeMethod LiteGame.Editor.UiTools.Validation.UiValidator.RunCli</c>。
        /// 有 Error 时以非零退出码结束（§4"出错要能让测试非零失败"）。
        /// </summary>
        public static void RunCli()
        {
            var report = Run();
            Debug.Log(report.Render());
            if (!report.Passed)
            {
                Debug.LogError($"[UI 校验] 失败：{report.ErrorCount} 条 Error");
                EditorApplication.Exit(1);
            }
            else
            {
                EditorApplication.Exit(0);
            }
        }
    }
}
