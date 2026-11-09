T2-A 纯 Meta 票据边界已落地，仅修改授权的 4 个文件；未执行 Unity 编译/测试（宿主编译由主代理接续）。

## 实际修改文件

1. `Assets/LiteGame/Modules/Meta/Abstractions/IRoomTicketSource.cs`（新建）
2. `Assets/LiteGame/Modules/Meta/Abstractions/IRoomFlow.cs`（新建）
3. `Assets/LiteGame/Modules/Meta/Client/MetaEntryService.cs`
4. `Assets/LiteGame/Modules/Meta/MetaSegment.cs`

新增两个 .cs 未手写 .meta，由 Unity 导入生成；Abstractions 目录现有 3 个接口 + 2 个新接口 = 直接 .cs 5 个，未超目录预算。

## 新 API 精确签名

```csharp
public interface IRoomTicketSource
{
    bool IsConfigured { get; }
    string AccountId { get; }
    bool IsLoggedIn { get; }
    void Logout(string reason);

    UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(
        string roomId, CancellationToken ct = default);
    UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(
        int mapId = 0, int expectedPlayers = 0, CancellationToken ct = default);
    UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(
        int mapId, int expectedPlayers, string template, string gmKey, CancellationToken ct = default);
}

public interface IRoomFlow
{
    UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(
        string roomId, CancellationToken ct = default);
    UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(
        int mapId = 0, int expectedPlayers = 0, CancellationToken ct = default);
    UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(
        int mapId, int expectedPlayers, string template, string gmKey, CancellationToken ct = default);
    bool LeaveMatch();
}
```

`MetaEntryService : IRoomTicketSource, IMetaConfigFacts`，保留原有登录/列表/战绩/配置业务与错误码（`entry.not-configured`/`entry.not-logged-in`）。

## 移除/退役的旧 API（本片后旧调用方会临时编不过，属预期）

- `MetaEntryService.EnterRoomAsync(string)` → `RequestJoinTicketAsync(string, CancellationToken)`
- `MetaEntryService.CreateRoomAsync(int,int)` → `CreateRoomTicketAsync(int,int,CancellationToken)`
- `MetaEntryService.CreateRoomAsync(int,int,string,string)` → `CreateRoomTicketAsync(int,int,string,string,CancellationToken)`
- `MetaEntryService.BindFlow(IAppFlow)` 删除
- `MetaEntryService.LeaveMatch()` 删除（离场归 `IRoomFlow`）
- `MetaEntryService.JoinWith`/`FlowUnbound` 删除
- 实现面不再实现 `IAccountSessionControl`

## 关键契约

- 取票方法在发请求前检查 ct 并传入 `IMetaClient`，await 返回后再检查取消；不解析票据地址、不 `RequestMatch`、不产生阶段副作用。
- 配置未注入 → `IsConfigured=false`；未登录 → `entry.not-logged-in`；默认模板/口令继续来自 `ConfigureTestRoom` 注入，显式重载原样透传 template/gmKey/mapId/expectedPlayers。
- `MetaSegment` 改为 `Put<IRoomTicketSource>(metaEntry)` + `Put<IMetaConfigFacts>(metaEntry)`；不再 `Put<IAccountSessionControl>`，不登记 `IRoomFlow`。
- 访问令牌仍只存 `MetaEntryService` 内部，端口不暴露；凭据不落日志。

## 未执行/后续

- 未执行 Unity 编译、未跑测试；未修改测试/Tests.csproj/.workflow/门禁/meta。
- 后续 T2-B/C 需迁移：`MetaEntrySession`、`AccountReentry`/`ProcedureSegment`（`IAccountSessionControl` 生产实现改由 `ProcedureFlow`）、`GMTestModePage`、`PageFlow`/`Page_LobbyLogic` 接线；旧 `IAppFlow` 文件暂留，待测试迁移经 Unity AssetDatabase 管线退役。
- 当前宿主编译预计会报旧调用方（`MetaEntrySession`、`GMTestModePage`、`AppComposition.BindMetaEntry`、`RoomFlowRig` 等测试）签名错误，需由 T2 后续片与 T6 独立迁移，生产侧未加兼容副作用。