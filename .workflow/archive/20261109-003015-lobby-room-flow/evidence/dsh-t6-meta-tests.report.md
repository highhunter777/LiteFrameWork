dsh 会话 ID：`session-1ae08d74-641c-4dab-beae-c439e3030248`

## 修改文件

1. `Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs`（重写为纯 Meta 测试）
2. `Assets/Tests/EditMode/Flow/RoomFlow/RoomFlowTicketTests.cs`（新增；该目录现为 5 个源码文件，到上限）

未改 `RoomFlowRig/Entry/Lifecycle/Doubles`、M3、Tests.csproj、生产、`.workflow`、门禁。未运行 Unity。

## 旧用例 → 新落点逐条映射

| 旧 `MetaEntryEditModeTests` | 新落点 |
|---|---|
| 配置测试房_建房带模板与口令_未配置则请求体不变 | 纯 Meta 同名用例，改调 `CreateRoomTicketAsync`；模板/口令/不带默认模板断言原样保留 |
| 显式模板建房_不依赖部署注入_携带地图与口令 | 纯 Meta 同名，改调显式 `CreateRoomTicketAsync(mapId, players, template, gmKey)` |
| 未配置_登录进房查询全显性失败_零Request | 纯 Meta `未配置_登录取票查询全显性失败_零客户端调用`（登录/取票/建房/战绩错误码 + `TicketCalls/CreateRoomCalls==0`）；流程侧新增 `RoomFlowTicketTests.未配置_取票失败_零Match排队` 保留“零 Request”语义 |
| 登录成功_缓存账号_设备标识直达客户端 | 纯 Meta 同名，去掉 FakeAppFlow |
| 未登录_换票与查询被拒_零票据调用 | 纯 Meta `未登录_取票与建房取票被拒_零票据调用`，保留错误码与零客户端调用 |
| 换票成功_地址解析并精确移交Match | 纯 Meta `换票成功_返回票据事实_版本与房号原样`（BuildHash/房号/票据事实）+ 流程 `RoomFlowTicketTests.换票成功_地址解析并精确移交Match`（真实 ProcedureFlow/StageMachine，Host/Port/RoomId/Ticket 精确断言） |
| 票据地址不可解析_确定性失败_零Request | 流程 `票据地址不可解析_确定性失败_零Match排队`，断言 `InvalidResponse`、无 Pending Match、Match 未入场 |
| 流程端口未绑定_换票显性失败 | 语义替代为 `流程构造_缺必要端口_当场fail_fast`：BindFlow 后绑定机制已删除，不再恢复旧 API；改为断言 `ProcedureFlow` 缺 Machine/TicketSource/Lifetime 任一构造即抛 `ArgumentNullException`，三端口齐备可构造。真实装配守卫待另派 M3/组合根测试 |
| 创建房间_服务端出号_精确移交Match_与进房同管道 | 纯 Meta `创建房间_服务端出号_透传版本与默认参数` + 流程 `创建房间_服务端出号_精确移交Match`（精确 Host/Port/Room/ticket 与建房参数） |
| 创建房间_带地图行键_原样透传给客户端 | 纯 Meta 同名，保留 `SeenMapId` |
| 创建房间_带期望人数_原样透传给客户端 | 纯 Meta 同名，保留 `SeenExpectedPlayers` |
| 创建房间_地址不可解析_确定性失败_零Request | 流程 `建房票据地址不可解析_确定性失败_零Match排队` |
| 未登录_创建房间被拒_零create调用 | 纯 Meta 同名，改调 `CreateRoomTicketAsync` |
| 传输工厂_envelope注入优先_非法键fail_fast抛 | 纯 Meta 原样保留 |

新增纯 Meta 取消测试：
- `预取消_取票与建房取票均零HTTP`：caller token 已取消时，取票/建房均抛 `OperationCanceledException`，`IMetaClient` 两次调用计数均为 0。
- `取票与建房取票_传递caller的token给IMetaClient`：断言传给 `IMetaClient` 的 `CancellationToken` 与 caller token 相等且 `CanBeCanceled`。

## 新流程测试的实际调用方式

`RoomFlowTicketTests` 使用真实 `MetaEntryService`（作为 `IRoomTicketSource`）+ 真实 `ProcedureFlow` + 真实 `StageMachine`，只注册记录用 noop `Main/Match`：
- 通过 `Machine.PendingId` 观察 Match 排队；
- 通过 noop Match 的 `OnEnter(in ProcedureArgs)` 记录 `MetaJoin`，断言精确 Host/Port/RoomId/Ticket；
- 非法地址断言不排队、Match 零入场；
- 构造缺端口断言 `ArgumentNullException`。

未复制生产校验/取消/所有权算法。

## 预期未接线的编译依赖与风险

1. **当前测试程序集仍会被旧 `RoomFlowRig.cs` 阻断编译**：它仍调用已删除的 `MetaEntryService.BindFlow(...)` 和旧 `new ProcedureFlow(Machine)` 签名。本片按要求未改该文件；主代理另派 rig/接线迁移后才会整体 0 错。这不是本片两个文件的错误，但宿主编译会先暴露它。
2. 新 fixture 依赖 `LiteGame.EditModeTests` 对 `ProcedureFlow`/`RoomFlowLifetime` 的 `InternalsVisibleTo` 以及当前构造签名 `(machine, IRoomTicketSource, RoomFlowLifetime, CancellationToken=default)`；生产若改签名，本 fixture 需在 T6 映射轮同步。
3. 纯 Meta 测试不再覆盖“未绑定流程失败”的旧错误码/文案；这是删除后绑定机制的语义替代，不是遗漏。真实组合根“缺注册/缺端口 fail-fast”仍需 M3 或独立装配守卫用例。
4. 页面层（`MetaEntrySession` 调用方）是否仍有旧 `EnterRoomAsync/CreateRoomAsync` 直调，不在本片写集；若存在，宿主编译/运行会在其他文件暴露，需要后续 T6 小片迁移。
5. 未运行 Unity；9+ 个 Meta/RoomFlowTicket 用例的宿主结果、编译 0 错需主代理实测。不宣告 T6 完成。