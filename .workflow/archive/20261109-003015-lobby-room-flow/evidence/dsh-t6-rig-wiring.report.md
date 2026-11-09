dsh 会话 ID：`session-1ae08d74-641c-4dab-beae-c439e3030248`

## 修改文件清单

1. `Assets/Tests/EditMode/Flow/RoomFlow/RoomFlowRig.cs`
2. `Assets/Tests/EditMode/Flow/RoomFlow/RoomFlowEntryTests.cs`（仅 1 行：`new AccountReentry(rig.Meta, surface)` → `rig.Flow`）
3. `Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs`
4. `Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj`

未改生产、`.workflow`、门禁、其他测试、Entry/Lifecycle 的刺激与断言；未运行 Unity。

## Rig 实际构造参数

```csharp
public readonly RoomFlowLifetime Lifetime = new();

Match = new ProcedureMatch(Accounts, Lifetime);

Battle = new ProcedureBattle(
    null, null, new ControlledBattleCommands(), Views, RoomBridge,
    Lifetime, default, false, Runs, null, Scenes,
    new ControlledMapProvider(), null, null);

Flow = new ProcedureFlow(Machine, Meta, Lifetime);
Session = new MetaEntrySession(Meta, Flow, () => null);
```

顺序为：共享 `Lifetime` → Match/Battle → Machine → Flow → Session；已删除 `Meta.BindFlow(...)`。`AccountReentry` 测试只机械改为 `new AccountReentry(rig.Flow, surface)`，未改任何断言。

## 9 个既有用例断言保持情况

Entry/Lifecycle 六个 + 三个用例的场景、断言、完成源、计数与预期 red 均未改：

1. `取票前已取消_零网络请求零RequestMatch`
2. `在途取消_晚到票据成功_零流程推进零建连接`
3. `重复请求_只发一次取票只建一次连接`
4. `旧A取消后新B成功_A晚到不得推进流程`
5. `Main到Match排队后离房_零建连接`
6. `账号重入_在途取票晚到不得RequestMatch`（仅控制实例改为 `rig.Flow`）
7. `JoinAck后Battle排队被Main覆盖_未接管会话恰好回收一次`
8. `必要卸载未结束_再次入场零取票_完成后放行`
9. `账号重入后旧房间清理完成_不覆盖登录页`

`RoomFlowTicketTests` 第五文件未改。

## M3 新装配守卫

`账号重入_401到回登录页单点接线` 已改为验证最终契约：

- `ProcedureFlow` 类声明同时实现 `IRoomFlow` 与 `IAccountSessionControl`，且保留 `public void Logout(`。
- `MetaEntryService` 不再实现 `IAccountSessionControl`、不再有 `BindFlow`、不再引用 `IAppFlow`。
- 从 `ProcedureSegment` 捕获 `new ProcedureFlow(...)` 的变量名，不写死变量名，断言：
  - `context.Put<IRoomFlow>(同一变量)`
  - `context.Put<IAccountSessionControl>(同一变量)`
  - `new AccountReentry(同一变量, ...)`
  - 保留 `Put<IAccountReentry>`
  - 不再有 `Put<IAppFlow>`
- 捕获共享 `RoomFlowLifetime` 变量，断言 `ProcedureMatch`、`ProcedureBattle`、`ProcedureFlow` 三处使用同一实例。
- `PageFlow : ILoginSurface` / `ShowLogin(` 原约束保留。
- `AppComposition` 断言 `Require<IRoomFlow>()`、无 `BindMetaEntry`、`new MetaEntrySession(context.Require<MetaEntryService>(), context.Require<IRoomFlow>(), ...)`，不会退回旧后绑定。

## csproj 增量修改

移除了已退役/仅旧接线使用的源链接：

- `Assets/LiteGame/Core/Flow/IAppFlow.cs`
- `Assets/LiteGame/Core/Flow/MetaJoin.cs`

保留 `IAccountSessionControl.cs` 源链接。未新增 `IRoomFlow`/MetaWire/MonoBehaviour 实现源，Core 守卫仍只读源码。

## 预期未接线的失败/依赖与风险

1. **Rig 现在按最终 internal 构造写入，但观测时生产 `ProcedureMatch`/`ProcedureBattle` 仍是旧 public 构造、`ProcedureSegment` 仍是旧 `IAppFlow` 注册**。因此 C 片落地前宿主编译会按预期报构造参数不匹配（`ProcedureMatch`/`ProcedureBattle`），这是本片“先按最终契约写测试让接线真实失败”，不是应删除的测试错误。
2. M3 在 C 片落地前预期红在：`ProcedureSegment` 尚无 `Put<IAccountSessionControl>(flow)`、`AccountReentry(flow, ...)`、共享 `RoomFlowLifetime` 变量，且仍有 `Put<IAppFlow>`。C 片落地后应转绿；若生产选择不同变量名但同实例，捕获变量正则可覆盖。
3. `MetaEntrySession` 新签名与 `AppComposition` 的 `IRoomFlow` 读取在观测时已落地，相关守卫部分当前应已满足。
4. csproj 移除 `IAppFlow/MetaJoin` 源链接后，若其他 Core.Tests 源码仍以标识符引用它们会编译失败；本次只按检索到的唯一引用（字符串）判断安全。若 C 片保留旧类型到下一批，可再评估。
5. 未跑 Unity/`dotnet test`；9 用例与 M3 的宿主结果需主代理在 C 落地后实测。不宣告 T6 done。