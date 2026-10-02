# Server/

服务端可执行宿主（.NET 8）。两端共享的服务器内核源码在 `Assets/RoomServer/{Runtime,Application}`
（Unity 与 dotnet 双编，asmdef/csproj 成对），不在本目录。

| 工程 | 职责 |
|---|---|
| `RoomServer/` | 房间服宿主：`ServerHost.cs`（连接/房间表/排空装配）、`RoomInstance.cs`、`RoomWorkerPool.cs`（有界 Mailbox/Worker）、`SettlementOutbox.cs`、`Application/`（`ServerLoop.cs` 60Hz 节拍 / SessionManager / ReconnectService / RoomMailbox 等带 IO 半边） |
| `MetaServer/` | Meta 服务宿主（Generic Host + AspNetCore）：Auth/Lobby 骨架、Join 票据验签、持久化契约与 Mongo 实存储 |

引用关系：两工程都 ProjectReference `Assets/` 下的共享工程（`LiteNet`、`LiteSim.Core`、
`Assets/RoomServer/{Runtime,Application}`）；`RoomServer.sln` 是服务端侧的本地解决方案。
测试从根 `Tests/Tests.slnx` 走（L1/L3 车道），纪律扫描的宿主定位见
`Assets/Tools/DisciplineScanner/Scripts/ScanTargets.cs`（`Server/RoomServer/RoomServer.csproj` 等）。
