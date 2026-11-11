# 施工记录：客户端架构重写（不继承旧设计）

> 运行 client-rewrite-architecture · 更新于 2026-11-11

## 施工记录

### T1 架构设计文档与旧文档处置（完成）

- 新增 `Docs/设计文档/架构/客户端重写架构总设计.md`（现行总设计，1.0）；设计索引已登记。
- 旧文档顶部标注取代关系：`客户端架构重构演进路线图.md`、`客户端依赖注入专项设计.md`、`商业级通用客户端框架总设计.md`、`UI系统总设计.md`。
- 证据：`python scripts/gate/test.py --suite Doc` → `doc-lint OK 0 violation/0 warning`、`comment-lint OK`、`PASS`。

### T2 Next 子树 + AOT/hot 程序集 + BootNext（完成）

- 新增 `arch/client-roots.json`（路径单源：`nextRoot/nextRuntime/nextBootstrap/frameworkNextRoot/finalRoot`）。
- 新增 `Packages/com.litegame.framework/Next/`：`LiteFrameworkNext.asmdef`、`Log`、`Lifetime`、`IClientApp`、`Platform`。
- 新增 `Assets/LiteGame/Next/Bootstrap/`：`LiteGameNext.Bootstrap.asmdef`、`GameEntry`（反射装载 `LiteGameNext.ClientApp`）。
- 新增 `Assets/LiteGame/Next/Runtime/`：`LiteGameNext.asmdef`、`ClientApp`、`UiRuntime` 占位、`AccountFeature` 占位。
- 新增 `Assets/Scenes/BootNext.unity`（Main Camera + 新 GameEntry；未复制旧场景组件）；已加入 Build Settings（`Boot.unity` 仍 index 0）。
- 架构登记：`arch/units.json` +3 单元；`ScanTargets.Generated.cs` 重生成；`hot-boundary-check.py` PARTITION 加 Next AOT/hot 路径与程序集（AOT 优先于 `Assets/LiteGame/`）。
- 证据：
  - `dotnet test ... --filter ClientNextArchitectureTests` → 9/9 通过；加 `ClientAsmdefAllowlistTests` → 14/14。
  - Unity `console_status.groundTruth.compilationFailed=false`、`consoleErrors=0`；`hot-boundary --strict` OK。
  - `BootNext` 实跑日志：`[LiteGameNext] 平台初始化` → `ClientApp 初始化` → `ClientApp 进入顶层流程` → `UiRuntime 占位页已打开`。
  - `python scripts/gate/test.py --suite L1 --profile PullRequest` → 1895/0（修复 `GameEntry` 裸 `.Forget()` 两条 TaskObservationGuard 后）。
- 2026-11-12T00:33:01+08:00 · 完成任务 T3 · 证据: UiRuntime/PageBase/PageSpec/PageHandle/BindName 实现；Placeholder/Login/Error 三页 prefab + EditorPageFactory；BootNext 实跑打开真实 PlaceholderPage（日志/截图）；ClientNextUiRun…
- 2026-11-12T00:33:08+08:00 · 开始任务 T4「ClientApp/AppFlow/Lifetime + 最小 Platform + 假账号登录」
- 2026-11-12T00:46:30+08:00 · 完成任务 T4 · 证据: AppFlow/ClientApp/AccountFeature 接线；BootNext EventSystem；ClientNextAppFlowTests 8/8、ClientNextBootTests EditMode 2/2 + PlayMode 1/1、ClientNextUiRuntimeTests 16…
- 2026-11-12T00:46:30+08:00 · 开始任务 T5「架构守卫 + 短链路测量 + 冷启动」
- 2026-11-12T00:55:59+08:00 · 完成任务 T5 · 证据: Next 路径单源豁免 AR1/AR2；dir-flat OK/namespace 893 baseline no growth/change-unit 404 no new unit；client-next-change-budget --check 1 feature/3 pages/3 prefabs/12 c…
- 2026-11-12T00:55:59+08:00 · 开始任务 T6「P0 验证与证据」
- 2026-11-12T00:59:18+08:00 · 完成任务 T6 · 证据: Unity compile 0 error；EditMode ClientNext 26/26；PlayMode Boot 1/1；L1 1895/0；Doc PASS；dir-flat/namespace/change-unit/client-next-budget/hot-boundary/arch-check …

## 关键决定

- 迁移形态按用户 2026-11-11 裁决改为**原地 strangler**：`Assets/LiteGame/Next` + `Packages/com.litegame.framework/Next`，旧目录原位保留；P3b1 删旧、P3b2 提升。
- 共享源迁出目标定为 `Packages/com.litegame.shared`（P1），迁出前不删旧目录。
- 路径单源 `arch/client-roots.json`；G2/G3/AC2 扫描、hot-boundary、ScanTargets 共读。
- Next asmdef `autoReferenced=false`，避免与旧 `LiteFramework` 同名类型冲突；AOT 反射入口名为 `LiteGameNext.ClientApp`。

## 偏差与处理

- `ClientNextArchitectureTests` 用 xunit（仓库 `Tests/**` 统一 xunit），未按最初 NUnit 字面写；不引入 NUnit 依赖。
- T2a/T2b 拆两半执行，避免单子代理范围过大；T2b 发现 `EditorBuildSettings` 需 `SaveAssets()` 才落盘，已复核。
- `GameEntry` 的裸 `.Forget()` 触发 `TaskObservationGuardTests`，已改为带日志回调的 `.Forget(ex => ...)`。

## 问题与修复

- `GameEntry.ClientAppTypeName` 与 `ClientApp` 命名空间不一致（`LiteGameNext.App` vs `LiteGameNext`）：T2a 已把 `ClientApp` 改回 `namespace LiteGameNext`，反射串一致。
- `Page_Login` 等旧 prefab 的 `BindNode` 仍属旧树；P1 复制页面时改用新 `BindName` 组件，P3b 删除旧 `LiteCodeGen`。

### T3 UiRuntime/PageBase/页面（完成）

- 新增 `Platform/Ui/{PageSpec,BindName,PageHandle,PageBase,UiRuntime}.cs`；`UiRuntime` 覆盖栈/Replace/Back/模态与输入阻断/Lru/Resident/DestroyOnClose/展示代次/转场窗口。
- 新增 `PlaceholderPage`/`ErrorPage`/`LoginPage` 三页 prefab + 类 + `EditorPageFactory`（P0 编辑器 AssetDatabase 装载）。
- `BootNext` 实跑打开真实 `PlaceholderPage`（日志 + 截图）。
- 修复：缓存页关闭只 `Hide()` 不 `SetActive(false)`，会留在屏幕；已在 `CloseEntry` 补 `SetActive(false)` 并加断言。
- 证据：`ClientNextUiRuntimeTests` 16/16；`ClientNext` EditMode 26/26。

### T4 AppFlow/启动集成（完成）

- 新增 `AppFlow`：登录循环 → PlaceholderPage → 等待取消；顶层取消静默、异常开 ErrorPage；无阶段机/页面服务定位器。
- `ClientApp` 接线 `AppFlow`；`AccountFeature.ShowLoginAsync` + 假游客登录；`Lifetime` 登记顺序 ui→account。
- `BootNext` 增加 EventSystem + StandaloneInputModule；PlayMode 用例真实点击 `Btn_GuestLogin`。
- 证据：`ClientNextAppFlowTests` 8/8；`ClientNextBootTests` EditMode 2/2、PlayMode 1/1（真实指针点击）；`ClientNext` 26/26。

### T5 守卫/触碰面/冷启动（完成，S3/S4 待后续）

- `dir-flat-lint.py`、`namespace-path.py`、`change-unit.py` 读 `arch/client-roots.json`，对 `nextRoot/frameworkNextRoot/nextTests` 跳过旧 AR1/AR2（新架构已废除这两条）；旧目录基线不变。
- 新增 `scripts/gate/client-next-change-budget.py`：1 feature / 3 pages / 3 prefabs / 12 code / 0 问题；`--check` 通过。
- 冷启动子代理：第一次需求不完整（4 个产品问题），补充“由 AccountFeature.ShowRecordsAsync 打开、Screen/Lru、无新入口按钮”后 → 2 个代码文件（1 新 + 1 改）+ 1 prefab，0 阻塞问题。
- S1 新增页面（实际）：`PlaceholderPage.cs` + `ClientApp.cs`（打开点）+ prefab = 2 代码 + 1 prefab，0 生成、0 中央登记；S2 改按钮 = 1 代码文件。
- **S3 战斗 HUD / S4 设置项尚未在 P0 实测**：它们依赖 P2 战斗与 P1 设置页；本轮只完成架构与页面/功能链路。AC3 的这两项应在 P1/P2 验收时补测，不能在本轮宣称全量通过。
- 证据：`dir-flat` OK 418 dirs/19 baselined；`namespace-path` 1153/893 no growth；`change-unit` 404/403 no new unit；L1 1895/0。

### T6 验证与证据（完成）

- Unity：`console_status` → `compilationFailed=false, compiling=false, consoleErrors=0, consoleWarnings=0`。
- EditMode：`ClientNext` 26/26（UiRuntime 16 + AppFlow 8 + Boot 2）。
- PlayMode：`ClientNextBootTests` 1/1（真实 BootNext 场景 + EventSystem 射线 + 真实点击登录 → PlaceholderPage）。
- L1：`test.py --suite L1 --profile PullRequest` → 1895/0。
- Doc：`test.py --suite Doc` → doc-lint/comment-lint OK。
- 守卫：`dir-flat` 418 dirs/19 baselined no growth；`namespace-path` 1153/893 no growth；`change-unit` 404/403 no new unit；`client-next-change-budget --check` 1 feature/3 pages/3 prefabs/12 code/0 problems；`hot-boundary --strict` OK；`arch-check --check` 36 units/6 layers/9 groups。
- 触碰面：S1 页面 = `PlaceholderPage.cs` + `ClientApp.cs` 打开点 + 1 prefab = 2 代码 + 1 prefab，0 中央登记/0 生成；S2 按钮 = 1 代码文件；冷启动补充需求后 2 代码 + 1 prefab、0 阻塞问题。
- **未闭环**：S3 战斗 HUD、S4 设置项依赖 P2/P1，未在 P0 实测；AC3 这两项不能宣称通过。P1/P2 必须补测。

## AC 状态（截至本轮）

| AC | 状态 | 证据/缺口 |
|---|---|---|
| AC1 | 通过 | 设计文档 + 索引 + 旧文档处置 + Doc 门禁 |
| AC2 | 通过 | BootNext 真实页面 + 26/26 EditMode + 1/1 PlayMode + 静态 0 旧 token |
| AC3 | **部分通过** | S1/S2/功能/冷启动达标；S3（HUD）/S4（设置项）待 P1/P2 实测 |

## 待办

- P1 共享源迁出、页面接入、S4 设置项触碰面；P2 BattleFeature、S3 HUD 触碰面；P3a/P3b 收口切换。
- AC3 的 S3/S4 补测后才能宣称 AC3 全部通过。

## 阶段流转
- 2026-11-12T00:59:26+08:00 · 完成「子代理正式施工」→ 「测试代码」
- 2026-11-12T01:01:27+08:00 · 完成「测试代码」→ 「主代理评审代码」
- 2026-11-12T01:01:27+08:00 · 完成「主代理评审代码」→ 「回写与收尾」
- 2026-11-12T01:01:46+08:00 · 完成「回写与收尾」→ 完成

## 验证结果
- 2026-11-12T01:00:35+08:00 · 验证 PASS · 7/7 · 证据 evidence/verify.json

## 交付摘要

- 客户端新架构总设计落地（`Docs/设计文档/架构/客户端重写架构总设计.md`），旧架构文档标注取代。
- P0 骨架：`Assets/LiteGame/Next` + `Packages/com.litegame.framework/Next`；AOT/hot 三件、BootNext 场景、ClientApp/AppFlow、UiRuntime/PageBase、Placeholder/Login/Error 三页、EditorPageFactory、EventSystem。
- 可运行证据：BootNext 实跑打开真实 PlaceholderPage；EditMode 26/26；PlayMode 真实点击 1/1；L1 1895/0。
- 旧 `Boot.unity`/旧客户端未被替换，仍可回滚。

## 验证结果

- `python scripts/gate/test.py --suite L1 --profile PullRequest` → 1895/0。
- `python scripts/gate/unity-tests.py --mode editor --filter ClientNext` → 26/26。
- `python scripts/gate/unity-tests.py --mode playmode --filter ClientNextBootTests` → 1/1。
- `python scripts/gate/test.py --suite Doc` → PASS；`comment-lint` 0 violation。
- `hot-boundary --strict` → OK；`arch-check --check` → 36 units/6 layers/9 groups。
- `dir-flat` 418 dirs/19 baselined；`namespace-path` 1153/893 no growth；`change-unit` 404/403 no new unit；`client-next-change-budget --check` 1 feature/3 pages/3 prefabs/12 code/0 problems。
- Unity `console_status`：`compilationFailed=false, compiling=false, consoleErrors=0, consoleWarnings=0`。

## 未决事项

1. **AC3 的 S3（战斗 HUD）/S4（设置项）未在 P0 实测**：依赖 P2/P1；P1/P2 必须补测后才能宣称 AC3 全量通过。
2. `PageSpec.Address` 当前是 AssetDatabase 资源路径；P1 接 YooAsset `AddressByFileName` 时需统一地址口径（或明确收集组用路径寻址）。
3. `AppFlow` 错误页的重试/回登录 P0 只记录；P1 接真实重进流程。
4. Player/非编辑器页面工厂仍抛 `NotSupportedException`；P1 接 YooAsset。
5. 共享源迁出（`Modules/Config/Table/*`、`Meta/WireSubModule/**`）未做；P3b 删旧目录前必须完成。
6. `LiteGameNext` 尚未加入 `HybridCLRSettings.hotUpdateAssemblies`；Player 冒烟前补。
7. 全量 L2 仍有历史失败（2026-11-07 32 例）；本轮只保证新增/相关用例与编译，不宣称全量绿。

## 文件清单

- 设计：`Docs/设计文档/架构/客户端重写架构总设计.md`、`Docs/设计文档/README.md`、4 份旧设计文档状态行。
- 架构/守卫：`arch/client-roots.json`、`arch/units.json`、`scripts/gate/{dir-flat-lint,namespace-path,change-unit,hot-boundary-check}.py`、`scripts/gate/client-next-change-budget.py`、`Assets/Tools/DisciplineScanner/Scripts/ScanTargets.Generated.cs`。
- Next：`Assets/LiteGame/Next/**`、`Packages/com.litegame.framework/Next/**`、`Assets/Scenes/BootNext.unity`、`ProjectSettings/EditorBuildSettings.asset`。
- 测试：`Tests/LiteFramework.Core.Tests/ClientNextArchitectureTests.cs`、`Assets/Tests/EditMode/ClientNext/**`、`Assets/Tests/UI/PlayMode/ClientNext/**`、测试 asmdef。
- 工作流：`.workflow/active/client-rewrite-architecture/**`、`.workflow/config.json`（红测命令）。

## 后续建议

- 先做 P1：共享源迁出 + Account/Matchmaking/Settings/Records/Result 页面接入 + Status 页；补 S4 设置项触碰面。
- P2：BattleFeature 边界与 S3 HUD 触碰面；本地服一局 + 重连用例。
- P3a：热更/错误恢复/Dev/GM；P3b1 删旧、P3b2 提升；删旧前先完成共享源迁出与 BuildHash 双端发布。
