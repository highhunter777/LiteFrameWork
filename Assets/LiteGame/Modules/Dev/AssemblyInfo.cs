using System.Runtime.CompilerServices;

// friend 表：沿用拆分前根程序集的授权面（装配根/开发层/编辑器三件/两套测试），外加会话域——
// 会话域读本地测试钩子的空实现（NullLocalTestHooks，弱网/测试规则播种路径）。
[assembly: InternalsVisibleTo("LiteGame.App")]
[assembly: InternalsVisibleTo("LiteGame.Adapters.Dev")]
[assembly: InternalsVisibleTo("LiteGame.Editor")]
[assembly: InternalsVisibleTo("LiteGame.Editor.Core")]
[assembly: InternalsVisibleTo("LiteGame.Editor.UiTools")]
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
[assembly: InternalsVisibleTo("LiteGame.UI.PlayModeTests")]
[assembly: InternalsVisibleTo("LiteGame.Modules.Session")]
