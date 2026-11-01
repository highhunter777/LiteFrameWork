using System.Runtime.CompilerServices;

// 构建期事实件（DeployBake/BridgeCapabilitySet）从装配程序集迁入本域后，保留测试可读的 internal 接缝：
// DeployBake 的平台读取分叉（ReadDesktopFile/ReadZipEntry/AndroidAssetsEntry）是纯函数，用例按真实
// deploy.args 形态直测它，不经过装配层。
[assembly: InternalsVisibleTo("LiteGame.EditModeTests")]
