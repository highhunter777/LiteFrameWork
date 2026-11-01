using System.Runtime.CompilerServices;

// friend 表：沿用拆分前根程序集的授权面（装配根/开发层/编辑器三件/两套测试），
// 拆分后仍按域收口——**不再有"根程序集全量放行"**，新增授权必须写在本文件。
[assembly: InternalsVisibleTo("LiteGame.App")]
[assembly: InternalsVisibleTo("LiteGame.Adapters.Dev")]
[assembly: InternalsVisibleTo("LiteGame.Editor")]
[assembly: InternalsVisibleTo("LiteGame.Editor.Core")]
[assembly: InternalsVisibleTo("LiteGame.Editor.UiTools")]
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
[assembly: InternalsVisibleTo("LiteGame.UI.PlayModeTests")]
