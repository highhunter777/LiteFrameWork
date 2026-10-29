using System.Runtime.CompilerServices;

// 产品程序集 internal 接缝对测试程序集可见（先例：LiteGame.App、LiteSim、RoomServer）——
// BakeTargets 的烘焙常量/emit 原语供 LiteGame.EditModeTests 直读（禁测试侧复制字面量）。
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
