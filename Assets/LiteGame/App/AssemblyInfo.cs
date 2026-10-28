using System.Runtime.CompilerServices;

// 产品程序集 internal 接缝对测试程序集可见（先例：LiteSim.Core→LiteSim.Core.Tests、RoomServer→LiteNet.Tests）。
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
// 开发装配层是产品层的伴生程序集（只在开发构建编译）：调试投影/GM 发现面要读产品的 internal 表现件。
[assembly: InternalsVisibleTo("LiteGame.Dev")]
