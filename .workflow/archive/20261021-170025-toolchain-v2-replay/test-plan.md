# 测试计划：工具链 v2：回放落盘与现场包

## 测试策略

- **先红后绿**：先落三组用例，当前编译期缺类型（预期失败）；实现后同一筛选命令必须全绿。
- **单一执行链验证**：重放用例直接构造 `RoomRuntime`，不 mock `StepFrame`/延迟补偿；以真实权威链路为准。
- **可复现优先**：断言“同记录同结果”“篡改定位准确帧”“截断/身份不符不得伪装成功”，而不是只断言文件存在。
- **纯逻辑优先**：服务端记录/编码/重放/客户端 bundle 写入均为纯 .NET，进 L1；Unity 接入（BattleContext/DevHUD）用 EditMode 或人工验证兜底。
- **纪律门禁**：R11 扫描、L1 全量、doc-lint、BuildHash 复算；新增 Assets 文件走 unity-pipeline 导入与 L2 meta 检查。

## 用例清单

| 编号 | 用例 | 覆盖 | 断言要点 |
|---|---|---|---|
| R1 | 录制并重放_逐帧checksum一致 | AC1/AC2 | 120 步；Seed/MatchId/RoomId 保真；`Matches=true`；`ReplayedSteps=120` |
| R2 | 篡改某帧输入_重放到准确分歧帧 | AC3 | 改第 42 帧输入；首分歧帧 = 42；给出命令序号 |
| R3 | 截断记录_重放只覆盖已记录段且显式报告 | AC4 | `Truncated=true`；重放步数 = 记录步数；消息含“截断” |
| R4 | 身份不符_重放拒绝而不是跑出假分歧 | AC6 | buildHash 不符 → `Ok=false`；消息含 buildHash |
| R5 | 测试规则事件_重放同帧生效 | AC2/AC6 | `SetTestRules` 事件存在；重放全一致 |
| R6 | 编解码往返_命令输入checksum与规则事件保真 | AC4 | 命令/输入/checksum/规则事件逐项保留 |
| R7 | 存储_原子写与保留上限 | AC4 | 4 写 → 保留 2；无 `.tmp` 残留；文件可读 |
| R8 | 写出_mismatch现场包_含manifest与日志 | AC5 | manifest/日志尾/Player.log 尾存在且内容正确 |
| R9 | 同局限频与保留上限_生效 | AC5 | 第 3 份返回 null；不抛 |

## 预期失败证据

- 命令：`dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter "FullyQualifiedName~RoomRunReplay|FullyQualifiedName~RoomRunCodec|FullyQualifiedName~ClientBundle"`
- 证据：`evidence/red.json`，exit=1，11.2s。
- 失败原因（预期）：`RoomServer.Diagnostics`、`RoomServer.Runtime.Diagnostics`、`LiteFramework.Diagnostics` 命名空间与 `RoomRunRecorder/RoomRunRecord/RoomRunEnvelope/DiagnosticsBundleRequest` 类型尚不存在（CS0234/CS0246）。
- 判定：这是“接口尚未实现”的预期红，不是环境/拼写/网络错误；实现阶段补齐类型后必须转绿。

## 运行方式

- 红测/定向：
  - `dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter "FullyQualifiedName~RoomRunReplay|FullyQualifiedName~RoomRunCodec|FullyQualifiedName~ClientBundle"`
- 全量 L1：
  - `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`
- 文档门禁：
  - `python scripts/gate/doc-lint.py`
- 共享源复核（若触碰共享源集）：
  - `python scripts/codegen/gen-build-hash.py --check-shared`
- Unity 资产：
  - 新增 `Assets/**/.cs` 经 unity-pipeline 导入；L2 检查 meta/GUID 完整。
