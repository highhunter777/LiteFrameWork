using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Naming
{
    /// <summary>
    /// UI 命名词表单源（《UI编辑器工具专项设计》§2.1.6："词表数据文件化——《制作规范》§9 的类别前缀/
    /// 类型段/语义词条住单一数据文件，Inspector 预设与校验核心共读"）。
    ///
    /// **内容源**＝《UI命名词表与控件清单》（文档）；本类是其**机器可读副本（权威）**——改词表须同步改此处 + 文档。
    /// 规则本体（判据/同文/私有件/模板层等）以《UI制作规范》§9 为权威，本类只枚举与提供查询口。
    /// </summary>
    public static class UiNamingWords
    {
        /// <summary>全名文法（《UI制作规范》§9.2 单源）：<c>^[A-Za-z]+_[A-Z][A-Za-z0-9]*$</c>。
        /// 前缀/语义编码于此，供规则与工具共读（不再各处手写正则）；语义段必须是 PascalCase。</summary>
        public const string GrammarPattern = "^[A-Za-z]+_[A-Z][A-Za-z0-9]*$";

        /// <summary>角色词（展示/结构角色，§9.3 词表）。</summary>
        public static readonly string[] RolePrefixes =
        {
            "Page_",   // 页面根（= prefab 文件名）
            "Hud_",    // 对局 HUD 根（= prefab 文件名；表现域，不走 tbuiform/导航）
            "Cot_",    // 容器/分组（含列表容器、SafeArea）
            "Bg_",     // 背景/底板
            "Txt_",    // 文本
            "Image_",  // 图像
            "Icon_",   // 图标
            "Clone_",  // 克隆条目模板（非激活）
        };

        /// <summary>交互控件段（UGUI 全称；与同名模板段共用同一个词，§9.3）。</summary>
        public static readonly string[] ControlSegments =
        {
            "Button_", "Toggle_", "Slider_", "Dropdown_", "InputField_", "ScrollRect_", "Scrollbar_",
        };

        /// <summary>控件模板使用段（去 <c>Tmp_</c>；§9.3 表）。SafeArea/EventRelay/Baseline 不占类型段，故不在列。</summary>
        public static readonly string[] TemplateSegments =
        {
            "Dialog_", "Toast_", "Loading_", "FlyText_", "Bubble_", "GuideHighlight_",
            "RedDot_", "Countdown_", "CountText_", "HpBar_", "ProgressBar_", "AvatarFrame_",
            "AnimatedImage_", "RotatingImage_", "StateButton_", "Stepper_", "StarRating_", "TabGroup_",
            "BottomNav_", "VirtualList_", "SimpleList_",
        };

        /// <summary>模板本体前缀（§9.3：原型 `Tmp_<控件类型>`／特化 `Tmp_<控件类型>_<语义>`）。
        /// 仅供模板层资产——业务节点不得使用（§9.3）。</summary>
        public static readonly string[] TemplatePrefixes =
        {
            "Tmp_",
        };

        /// <summary>高频语义词（§4；规则"同页面同语义同词"用，本步未接入一致性校验）。</summary>
        public static readonly string[] SemanticTerms =
        {
            // 动作
            "Close", "Confirm", "Cancel", "Back", "Submit", "Save", "Reset", "Retry", "Refresh", "Buy", "Equip", "Use",
            // 角色
            "Title", "Message", "Name", "Value", "Count", "Level", "Time", "Desc",
        };

        private static readonly HashSet<string> KnownPrefixes = BuildKnownPrefixes();

        private static HashSet<string> BuildKnownPrefixes()
        {
            var set = new HashSet<string>();
            foreach (var p in RolePrefixes) set.Add(p);
            foreach (var p in ControlSegments) set.Add(p);
            foreach (var p in TemplateSegments) set.Add(p);
            foreach (var p in TemplatePrefixes) set.Add(p);
            return set;
        }

        /// <summary>该前缀段是否登记在词表（角色词 ∪ 控件段 ∪ 模板段）。</summary>
        public static bool IsKnownPrefix(string prefix)
            => !string.IsNullOrEmpty(prefix) && KnownPrefixes.Contains(prefix);

        /// <summary>按首个下划线切分（§9.2「全名恰一个下划线」）；无法切分返回 false。</summary>
        public static bool TryParse(string name, out string prefix, out string semantic)
        {
            prefix = null;
            semantic = null;
            if (string.IsNullOrEmpty(name)) return false;
            int i = name.IndexOf('_');
            if (i <= 0) return false;
            prefix = name.Substring(0, i + 1);       // 含下划线（"Cot_"）
            semantic = name.Substring(i + 1);
            return semantic.Length > 0;
        }

        /// <summary>全名是否恰一个下划线。</summary>
        public static bool HasExactlyOneUnderscore(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int count = 0;
            for (int i = 0; i < name.Length; i++) if (name[i] == '_') count++;
            return count == 1;
        }
    }
}
