using System.Runtime.CompilerServices;

// 产品组合根与测试读表现件：视图装配/读模型保持 internal，经 friend 程序集消费。
[assembly: InternalsVisibleTo("LiteGame.App")]
[assembly: InternalsVisibleTo("LiteGame.Modules")]
[assembly: InternalsVisibleTo("LiteGame.Adapters.Dev")]
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
