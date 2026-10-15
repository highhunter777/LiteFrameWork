using System.Runtime.CompilerServices;

// 安全信封在 Unity Mono（无平台 AesGcm）下的托管回退（ManagedAesGcmCipher）需要**逐位对账**平台实现——
// 该对账是 L1 用例（Tests/LiteNet.Tests）。与 LiteSim.Core → LiteSim.Core.Tests、RoomServer → LiteNet.Tests 同款先例。
[assembly: InternalsVisibleTo("LiteNet.Tests")]
