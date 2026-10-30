using System.Runtime.CompilerServices;

// Battle 域装配段与表现内核的 friend 表：既有 4 条随 Presentation 一并迁入，另加 PlayMode 冒烟程序集。
[assembly: InternalsVisibleTo("LiteGame.App")]
[assembly: InternalsVisibleTo("LiteGame.Modules")]
[assembly: InternalsVisibleTo("LiteGame.Adapters.Dev")]
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
[assembly: InternalsVisibleTo("LiteGame.UI.PlayModeTests")]
