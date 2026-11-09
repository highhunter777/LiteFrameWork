已完成 C1 的 4 文件接线。**未编译、未运行 Unity、不称 green**。另需主代理注意一个并发写入异常（见文末）。

## 变更清单（精确）

### 1. `Assets/LiteGame/App/PageServices/MetaEntrySession.cs`
- 新增字段 `private readonly IRoomFlow _rooms;`（第 21 行）。
- 构造改为（第 25 行）：
```csharp
public MetaEntrySession(MetaEntryService meta, IRoomFlow rooms, Func<cfg.Tables> tables, IAccountReentry reentry = null)
```
并 `_rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));`（第 28 行）；无旧构造兼容层。
- `QuickEnterAsync`：`await _rooms.EnterRoomAsync("", ct)`（第 76 行）。
- `EnterRoomAsync`：`await _rooms.EnterRoomAsync(roomId ?? string.Empty, ct)`（第 90 行）。
- `CreateRoomAsync`：`await _rooms.CreateRoomAsync(mapId, expectedPlayers, ct)`（第 83 行）。
- 这 3 处均删除 await 后的 `ct.ThrowIfCancellationRequested()`；`MetaResult` 映射原样保留。
- `LeaveMatch() => _rooms.LeaveMatch();`（第 114 行，`bool` 结果被丢弃，void 表达式体合法）。
- 登录/列表/战绩仍走 `_meta`，其 `ThrowIfCancellationRequested` 未动；`ReenterIfAuthLostAsync`、地图名、时间映射未动。
- 源文件保持 UTF-8 无 BOM / LF。

### 2. `Assets/LiteGame/App/AppComposition.cs`
- `ComposeAfter`：删除 `BindMetaEntry(context);`（现第 90-98 行只剩 PageServices/Identity/Modals/InputGate/ProductSettings/SimParams/DevDecoration/FrameOrder/Seal）。
- `BindPageServices`（第 107-109 行）：
```csharp
_metaSession = new MetaEntrySession(
    context.Require<MetaEntryService>(), context.Require<IRoomFlow>(),
    () => config.Tables, context.Get<IAccountReentry>());
```
- `BindMetaEntry` 方法（含 `meta.BindFlow(...)`）已整体删除；现 `BindInputGate` 后直接 `BindSimParams`（第 159-170 行），全文件无 `BindMetaEntry`。

### 3. `Assets/LiteGame/Editor/Gm/GMTestModePage.cs`
- `CreateTestRoomAsync` 在保留 Meta 配置/登录/地图表/`room.gmkey` 判据后新增（第 162-163 行）：
```csharp
IRoomFlow rooms = GMEditorResolve.ResolveFromContainer<IRoomFlow>();
if (rooms == null) { _testRoomStatus = "IRoomFlow 未装配"; return; }
```
- 建房调用改为（第 173-174 行）：
```csharp
MetaCall<JoinTicketInfo> call = await rooms.CreateRoomAsync(
    mapId, 0, MetaEntryService.TestRoomTemplate, deploy.RoomGmKey);
```
- 房号回填 `_gmRoom`、`_testRoomBusy` finally、错误/异常文案均未动；未新增任何密钥输出。

### 4. `Assets/LiteGame/Modules/UI/Pages/Screens/Hub/Page_LobbyLogic.cs`
- `EnterAsync`（第 225-237 行）：OCE 仅当 `Form.IsDisplayCurrent(generation)` 才 `_entering = false`；一般完成先 `if (!Form.IsDisplayCurrent(generation)) return;`，再 `_entering = false` 与按钮恢复。
- `CreateRoomAsync`（第 267-279 行）同口径。
- 其余方法、`OnButtonClick` 并发改动、轮询/泡泡/列表逻辑未动。源文件保持 UTF-8 无 BOM / LF。

## 实际接口交接（已按真实签名核对）

`Assets/LiteGame/Modules/Meta/Abstractions/IRoomFlow.cs`（`namespace LiteGame`）：
```csharp
UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default);
UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0, CancellationToken ct = default);
UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers, string template, string gmKey, CancellationToken ct = default);
bool LeaveMatch();
```
因此 `MetaEntrySession` 新签名可直接被测试侧 `new MetaEntrySession(Meta, Flow, () => null)` 命中；`IMetaSession.LeaveMatch()` 仍是 void，页面侧 `Meta?.LeaveMatch()` 不需改。

## 未验项 / 待其他片与 T6

1. **未编译、未跑 Unity**：C# 可编译性、`IRoomFlow` 注册顺序、`context.Require<IRoomFlow>()` 是否在 `ComposeAfter` 前已由 ProcedureSegment 就绪，均由宿主验证。
2. **装配同实例/无环**：我只改了 `BindPageServices` 取件；`ProcedureSegment` 是否同窗 `Put<IRoomFlow>` 与 `<IAccountSessionControl>` 同一 flow、是否单所有者 Dispose、AccountReentry 构造顺序，未在本片验证（不属这 4 文件）。
3. **旧 `IAppFlow`**：`BindMetaEntry` 消费者已移除，但 `ProcedureFlow` 仍声明实现 `IAppFlow`；生产侧是否已零引用、以及经 Unity AssetDatabase 退役，留给 T6/T2 收口。我未动其文件。
4. **T3 清理/恢复**：`PageFlow`、`ProcedureMatch/Battle` 的 operation 接管、`BeginCleanup/NotifyCleaned`、场景卸载 await、统一恢复、队列窗口回收、登录代次，均未实现。
5. **T6 调用迁移**：全仓 `new MetaEntrySession` 只剩 `Tests/EditMode/Flow/RoomFlow/RoomFlowRig.cs:43`，已使用 3 参（reentry 默认）；`M3WiringGuardTests` 已断言无 `BindMetaEntry`。是否还有其它测试用旧 2 参构造，需 T6 全量确认。
6. **本地直入/GM**：`GMTestModePage` 现依赖容器内 `IRoomFlow`；Play 中若 ProcedureSegment 未就绪，GM 状态行会显式显示“IRoomFlow 未装配”，不会回退旧 `meta.CreateRoomAsync` 副作用。
7. **页面 OCE 语义**：若取消时 display 已非当前代次，本片有意不清 `_entering`（避免旧 A 清新 B），依赖 `OnPageHide/OnShow` 复位；T3 引入统一 PageFlow 时需复核该依赖是否仍成立。

## 需主代理注意：并发写入冲突

编辑期间检测到有另一写入方在改同一文件：
- `MetaEntrySession.cs` 在我读取后一度出现 `_flow.EnterRoomAsync(...)`（字段名不一致，短暂不可编译），随后又变回 `_rooms`；
- `AppComposition.cs` 的 `MetaEntrySession(...)` 构造与 `BindMetaEntry` 删除在我 round-1 编辑同时由外部写成。

当前落盘状态已收敛为 `_rooms`，且 4 文件 mtime 已稳定（>1 分钟无外部变更），全文件无 `_flow.`、无 `BindMetaEntry`。但请编译前确认没有其他 C 会话仍在运行，避免再次被覆盖。