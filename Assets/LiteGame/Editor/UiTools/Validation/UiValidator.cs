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
            "Assets/LiteFramework/Runtime/Modules/ClientRuntime",
            "Assets/LiteGame/App",
            "Assets/LiteFramework/Runtime",
            "Assets/LiteGame/Modules/View",
        };

        /// <summary>UI 资产扫描面（模板 prefab 所在，供资产面规则用）。</summary>
        public const string ScreensDir = "Assets/ShareResource/UI/Screens";
        public const string WidgetsDir = "Assets/ShareResource/UI/Widgets";
        public const string HudDir = "Assets/ShareResource/UI/Hud";

        /// <summary>规则族全表（设计 §6.2 九族 + 项目自定义「渲染/适配」）。已接入＝有真实执行面；族内未落地子项在描述里列明原因。</summary>
        public static readonly UiRuleFamily[] Families =
        {
            new UiRuleFamily("结构", true, "页面根契约（Page 四件套 + SafeArea）与 HUD/Tmp_ 系统面根契约（制作规范 §3.1/§3.3 对齐实况口径）——`ui.page-structure` 已接入"),
            new UiRuleFamily("命名/绑定", true, "命名文法/前缀词表/私有件（§9.2/§9.4）+ 生成物 hash 对账（§2.1.5）——已接入；BindName 同文/同页同词/编号连续性待接入（与五族边界无依赖，另批）"),
            new UiRuleFamily("契约", true, "缺 WidgetSpec、控件/页面清单漂移、契约漂移对账（重生成逐字节——含手改检出）、引用对账（未消费/越界）、文本双写——已接入"),
            new UiRuleFamily("样式", true, "颜色 token 对账（裸颜色——复用 UiStyleTool 单源）与文字档对账（未标记/漂移/失效/实例覆盖——复用 UiTextStyleTool 单源），令牌源↔SO 漂移（`ui.style.token-drift`），引用对账（`ui.style.ref`：未知令牌/值漂移/无目标/widget 三态/writer 自身或祖先双写者——P2-3 B2），Selectable 状态色（`ui.style.selectable`：缺组件/未知令牌/漂移/非 ColorTint 跳过——P2-3 B4）——已接入；令牌值收敛到 JSON→`UiThemeAsset`（P2-3a 12 色 + B2 8 枚状态色 + B4 44 精确 + 5 Selectable + 5 文字角色，其余五组空集）；uGUI 原生 Selectable.colors 已令牌化（46/33+13）"),
            new UiRuleFamily("文本", true, "key 存在性（LTextKeyRef + Lua SetTextKey* 两路）、占位符相符、语言列完整、富文本白名单——已接入；缺字/字体 fallback 待静态化判据（SDF Dynamic 模式运行时补字，无静态口径）"),
            new UiRuleFamily("布局/适配", true, "CanvasScaler 基线与第二套缩放禁令（Page_*，UI-LAYOUT-01）——已接入；锚点漂移/超宽屏溢出待设备档与布局基线（P4）"),
            new UiRuleFamily("资源", true, "表内资源路径存在性（.prefab/.unity 通用反射面）已接入；缺引用其余子项/收集组/图集字体名册/生产包排除待接入（图集基建为零，另批）"),
            new UiRuleFamily("性能", true, "raycastTarget 预算、布局嵌套深度、UI 禁 Animator——静态规则已接入（总设计 §11.6「Player 性能另测」）；图集依赖待图集基建；rebuild/GC/打开耗时归 Player 证据"),
            new UiRuleFamily("行为", true, "PlayMode 执行面：复用复位/语言切换/模态射线穿透（UiBehaviorPlayModeTests）+ 既有覆盖（列表重绑/取消/转场输入锁）——已接入；焦点例待焦点契约冻结"),
            new UiRuleFamily("渲染/适配", true, "视觉单一来源（运行时不得构建视觉，§7）——首条已接入"),
        };

        /// <summary>规则注册表（按此顺序执行；后续步逐条追加）。</summary>
        private static readonly IUiRule[] Rules =
        {
            new RuntimeVisualRule(),
            new BindingNamingRule(),
            new UiBindingHashRule(),
            new AssetTablePathRule(),
            new PageStructureRule(),
            new WidgetSpecRule(),
            new WidgetManifestRule(),
            new PageManifestRule(),
            new TextStaticKeyRule(),
            new TextPlaceholderRule(),
            new TextLocaleColumnRule(),
            new TextRichTextRule(),
            new StyleColorDriftRule(),
            new StyleTextDriftRule(),
            new StyleTokenDriftRule(),
            new StyleRefRule(),
            new StyleSelectableRule(),
            new LayoutScalerRule(),
            new PerformanceRaycastTargetRule(),
            new PerformanceLayoutDepthRule(),
            new PerformanceAnimatorRule(),
            new ContractDriftRule(),
            new ContractReferenceRule(),
            new ContractTextDoubleWriteRule(),
        };

        /// <summary>工程根（Assets 之上）。</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>跑全部规则（构造默认上下文——源码面 + 资产面）。</summary>
        public static UiReport Run()
        {
            return Run(BuildDefaultContext());
        }

        /// <summary>跑全部规则（显式上下文——测试可喂合成输入）；结果应用例外登记并稳定排序。</summary>
        public static UiReport Run(UiValidationContext context)
        {
            var report = new UiReport();
            foreach (var family in Families) report.RegisterFamily(family);
            foreach (var rule in Rules) report.AddRange(rule.Check(context));

            UiRuleExceptionSet.Load(context.RuleExceptionsPath ?? UiRuleExceptionSet.DefaultPath).Apply(report);
            report.SortIssues();
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
