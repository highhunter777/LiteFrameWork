# 测试计划：客户端架构重写（不继承旧设计）

> 运行 client-rewrite-architecture · 更新于 2026-11-11
> 当前状态：**红**（L1 静态契约 9 例：8 失败 / 1 通过）——`Assets/LiteGame/Next` 子树尚未落地，失败即本轮预期。
> 角色：施工子代理（测试角色）。本计划与测试代码是**唯一**改动面；未写生产代码、未改 `.workflow` 其它文件、未改门禁脚本或构建配置。

## 测试策略

### 1.1 分层与先后

| 层 | 目标 | 落点 | 本轮状态 |
|---|---|---|---|
| **L1 静态契约**（dotnet/xunit） | 根路径单源、必需入口文件、AOT/hot 程序集分件与方向、功能目录边界、旧机制 token=0、旧根引用=0 | `Tests/LiteFramework.Core.Tests/ClientNextArchitectureTests.cs` | **已落地并跑红**（§3） |
| **L2 EditMode 行为**（Unity） | `UiRuntime` 打开/关闭/栈/模态/Back/缓存/输入阻断；`PageBase` 绑定与解绑 | `Assets/Tests/EditMode/ClientNext/**` | 待 T2 骨架编译后补写（§5.2） |
| **L2 EditMode 集成**（Boot/AppFlow） | `BootNext → GameEntry → ClientApp → 占位页/LoginPage/ErrorPage`；取消与关闭顺序 | 同上 | 待 T2/T4（§5.2） |
| **L2 PlayMode 边界** | 真实帧驱动 `ClientApp.Tick`、页面展示代次、Back 链、场景装卸 | 后续运行（P2 前不承诺） | 未开始 |
| **Player 冒烟** | `BootNext` IL2CPP 启动 + hot 程序集装载 | `hybridclr.py install` + 构建链 | 非本轮（无 hot 产物时是静默跳过，不能当证据） |

设计原则：**先写静态契约并让它红**，把"Next 树应当长什么样"变成可执行判据；行为测试随后在骨架可编译后再补，避免用静态测试冒充行为测试（方案 §测试策略）。

### 1.2 覆盖重点与风险

- **根路径单源**：`arch/client-roots.json` 是唯一路径来源（`nextRoot`/`nextRuntime`/`nextBootstrap`/`frameworkNextRoot`/`finalRoot`）。测试、后续守卫脚本都从这里取路径；P3b 切换只改该模型，不改测试。
- **AOT/hot 分件**：不只查名字存在，还查 AOT 侧 asmdef 的 `references` 不指向 hot 程序集（G1 唯一硬边界；与 `scripts/gate/hot-boundary-check.py` 互补：Python 侧按路径分区，本用例按 asmdef 名/引用边）。
- **功能目录边界（G2）**：`Features/<Name>/` 内禁 `Abstractions/`/`SubModules/`/`Services/`/`Impl/`，且必须有 `<Name>Feature.cs` 唯一公开入口；`Account` 目录必须存在（登录页归 `AccountFeature`）。
- **旧机制回潮（G3/AC2）**：`PageIds`/`UIPageCatalog`/`UIBindIndex`/`BindGenerator`/`IClientSegment`/`ServiceContainer`/`PageServices`/`ProcedureId` 在 Next 文本中 0 处；`Assets/LiteGameNext` 旧根 0 处。
- **不做空扫描假绿**：所有扫描类用例先断言目标目录存在，目录缺失 → 失败并说明缺哪个目录；文件缺失 → 失败并说明缺哪个文件（这是本轮红测的正式形态）。
- **静态测试不等于可运行**：文件存在 ≠ 能编译 ≠ `BootNext` 能开页面。行为与启动链由 §2 的 Unity 用例（待补写）与 T6 的 PlayMode 覆盖。

### 1.3 token 判定口径

- 扫描范围：`nextRoot` 下全部文本文件（`.cs`/`.asmdef`/`.json`/`.prefab`/`.unity`/`.asset`/`.mat`/`.controller`/`.txt`/`.xml`/`.uss`/`.uxml`/`.md`），`.meta` 与二进制不扫。
- `.cs` **剔注释后**比对（与既有 `PageDefinitionGuardTests` 同口径）：注释里引用"废除清单"不算违规；任何代码引用即违规。
- 路径类 token（`Assets/LiteGameNext`）先做 `\` → `/` 归一，避免 Windows 写法漏检。

## 用例清单

### 2.1 已落地（L1 静态契约，`Tests/LiteFramework.Core.Tests/ClientNextArchitectureTests.cs`）

| 编号 | 用例（xunit 方法） | 断言要点 | 任务/验收 | 当前 |
|---|---|---|---|---|
| CN-01 | `ClientRoots模型必须自洽`（:44） | `nextRoot` 在 `finalRoot` 内；`nextRuntime`/`nextBootstrap` 在 `nextRoot` 内；`frameworkNextRoot` 与 `nextRoot` 不同 | T5 / G4 路径单源 | **通过** |
| CN-02 | `Next骨架必需文件必须存在(nextBootstrap, GameEntry.cs)`（:58） | `nextBootstrap/GameEntry.cs` 存在 | T2 / AC2 | 失败（文件缺失） |
| CN-03 | `Next骨架必需文件必须存在(nextRuntime, App/ClientApp.cs)`（:58） | `nextRuntime/App/ClientApp.cs` 存在 | T2 / AC2 | 失败（文件缺失） |
| CN-04 | `Next骨架必需文件必须存在(nextRuntime, Platform/Ui/UiRuntime.cs)`（:58） | `nextRuntime/Platform/Ui/UiRuntime.cs` 存在 | T3 / AC2 | 失败（文件缺失） |
| CN-05 | `BootNext场景必须存在`（:67） | `Assets/Scenes/BootNext.unity` 存在 | T2 / AC2 | 失败（文件缺失） |
| CN-06 | `Next子树必须有AOT与hot程序集且AOT不引用hot`（:74） | Next 树存在；AOT 框架件（`LiteFrameworkNext` 等价命名）1 个、AOT 引导件（`LiteGameNext.Bootstrap` 等价命名）1 个且在 `nextBootstrap/` 下、hot 应用件（`LiteGameNext` 等价命名）1 个且在 `nextRuntime/` 下；AOT `references` 不含 hot 名 | T2 / G1+AC2 | 失败（Next 树缺失） |
| CN-07 | `Next功能目录必须内聚且不含技术层子目录`（:120） | `nextRuntime/Features` 存在且非空；`Account` 存在；功能目录内无 `Abstractions/SubModules/Services/Impl`；每个功能有 `<Name>Feature.cs` | T3+T4 / G2 | 失败（功能根缺失） |
| CN-08 | `Next文本不得出现旧机制token`（:154） | 8 个旧 token 命中数 = 0 | T2/T3/T5 / G3+AC2 | 失败（Next 树缺失） |
| CN-09 | `Next文本不得引用旧根Assets_LiteGameNext`（:162） | 旧根引用 = 0 | T2/T5 / A5 | 失败（Next 树缺失） |

### 2.2 待补写（Unity EditMode，T2 骨架编译后）

| 编号 | 用例 | 覆盖 | 任务 | 补写条件 |
|---|---|---|---|---|
| UX-01 | `UiRuntime` 打开页：prefab 非激活实例化 → `bind(page)` → 激活 → 入栈 → `OnShow`（顺序可断言） | 页面生命周期硬约束 | T3 | `UiRuntime` 可编译 |
| UX-02 | 栈语义：`OpenAsync`/`CloseAsync`/`ReplaceAsync`/`BackAsync`；Back 优先级 Loading → 模态 → 页面 | 导航与返回 | T3 | 同上 |
| UX-03 | 缓存与租约：`Lru`/`Resident`/`DestroyOnClose` 行为；关闭归还 prefab 租约，销毁释放 | 缓存预算 | T3 | 同上 |
| UX-04 | 模态：射线遮蔽、`IsInputBlocked` 计数（模态/全屏页）随开关增减 | 输入阻断 | T3 | 同上 |
| UX-05 | `PageBase` 绑定：缺控件/重名 `BindName` 抛错；关闭解绑按钮；`Bind<T>` 命中一次扫描字典 | 无全局 `UIBindIndex` 的替代面 | T3 | 同上 |
| UX-06 | 展示代次：旧页异步回调在 `handle.IsCurrent(generation)` 失效后不得覆盖新页 | 页面展示代次 | T3 | 同上 |
| UX-07 | `SafeAreaFitter` 与转场超时兜底（由 `UiRuntime.Tick` 驱动，不依赖全局时钟） | P1 parity 前置 | T3 | 同上 |
| UX-08 | 启动链：`BootNext` 场景含 `GameEntry`，运行后 60s 内打开占位页/LoginPage/ErrorPage（filter `ClientNextBootTests`） | AC2 主判据 | T2 | 场景 + 入口可编译 |
| UX-09 | `AppFlow`：假账号登录进占位页；App token 取消 → 静默关闭；页面取消 → 回流程（filter `ClientNextAppFlowTests`） | 顶层流程 | T4 | `ClientApp`/`AppFlow` 可编译 |
| UX-10 | `Lifetime`：逆序 `Dispose`、异常聚合、已关闭 `Add` 抛 `ObjectDisposedException` | 关闭契约 | T4 | 同上 |

> 数量与命名以 `tasks.md` 的 filter 为准：T2=`ClientNextBootTests`、T3=`ClientNextUiRuntimeTests`、T4=`ClientNextAppFlowTests`、T6=`ClientNext`（合集）。

## 预期失败证据

命令：

```powershell
dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~ClientNextArchitectureTests --nologo
```

观察到（2026-11-11，退出码 1，**断言失败**，非编译/环境错误；编译 0 error）：

```text
失败!  - 失败:     8，通过:     1，已跳过:     0，总计:     9，持续时间: 23 ms - LiteFramework.Core.Tests.dll (net8.0)
```

| 失败用例 | 失败消息（原文摘要） |
|---|---|
| `Next骨架必需文件必须存在(nextBootstrap, GameEntry.cs)` | `P0 骨架必需文件缺失：Assets/LiteGame/Next/Bootstrap/GameEntry.cs` |
| `Next骨架必需文件必须存在(nextRuntime, App/ClientApp.cs)` | `P0 骨架必需文件缺失：Assets/LiteGame/Next/Runtime/App/ClientApp.cs` |
| `Next骨架必需文件必须存在(nextRuntime, Platform/Ui/UiRuntime.cs)` | `P0 骨架必需文件缺失：Assets/LiteGame/Next/Runtime/Platform/Ui/UiRuntime.cs` |
| `BootNext场景必须存在` | `新启动场景缺失：Assets/Scenes/BootNext.unity` |
| `Next子树必须有AOT与hot程序集且AOT不引用hot` | `Next 子树不存在：Assets/LiteGame/Next（原地 strangler 尚未落地）` |
| `Next功能目录必须内聚且不含技术层子目录` | `功能根不存在：Assets/LiteGame/Next/Runtime/Features` |
| `Next文本不得出现旧机制token` | `Next 子树不存在，静态扫描无对象：Assets/LiteGame/Next` |
| `Next文本不得引用旧根Assets_LiteGameNext` | `Next 子树不存在，静态扫描无对象：Assets/LiteGame/Next` |

**为什么这是正确的失败**：

1. 判据与 AC2 直接对应——`Next` 子树、`BootNext.unity`、三个入口文件都还不存在；测试红的是"交付物缺失"，不是测试自身缺陷。
2. 唯一通过的是 `ClientRoots模型必须自洽`：证明路径单源已接通（`arch/client-roots.json` 可解析、字段自洽），扫描用的根确实来自该模型，不是硬编码后恰好落空。
3. 失败全部落在**断言**层（`Assert.True` 消息），没有 `CS` 编译错误，没有找不到仓库根的环境错误——T2 落地后应逐条转绿，而不是"改测试让它过"。

**不污染存量基线**（同一工程、排除本文件用例）：

```powershell
dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName!~ClientNextArchitectureTests" --nologo
# 已通过! - 失败: 0，通过: 734，已跳过: 0，总计: 734，持续时间: 10 s
```

构建面：本文件 0 warning / 0 error（`dotnet build` 报"0 个警告 0 个错误"）。

**对后续任务的两个提示**（不改测试，避免 T2 落地时误判为本文件回归）：

1. `ClientAsmdefAllowlistTests.客户端根下所有asmdef必须登记在允许表` 递归扫描 `Assets/LiteGame`：T2 一加 `Next/**/*.asmdef` 就会红，必须同批登记 `arch/units.json`（T2 文件清单已含该文件）。
2. `scripts/gate/hot-boundary-check.py` 的分区表要加 `Assets/LiteGame/Next/Bootstrap/`（优先于 hot 的 `Assets/LiteGame/`）、`Assets/LiteGame/Next/Runtime/`、`Packages/com.litegame.framework/Next/`（T2 文件清单已含该脚本）。

## 运行方式

```powershell
# 本文件的红/绿判据（只跑静态契约）
dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~ClientNextArchitectureTests --nologo

# 失败消息全文（需要贴证据时）
dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~ClientNextArchitectureTests --nologo --logger "console;verbosity=normal"

# L1 全量（收尾/门禁，含本文件新增用例）
python scripts/gate/test.py --suite L1 --profile PullRequest

# Unity EditMode（T2 骨架编译后；按 T2/T3/T4/T6 的 filter）
python scripts/gate/unity-tests.py --mode editor --filter ClientNext
```

工程接线：`Tests/LiteFramework.Core.Tests.csproj` 是 `Microsoft.NET.Sdk` 默认通配（未显式 `<Compile Remove/Include>`），新增 `.cs` 自动纳入编译；xunit 包已引用。**本轮未改 csproj、未加包、未改构建/门禁配置。** 新增的 `arch/client-roots.json` 是数据模型文件（`scripts/gate/arch-check.py` 复跑仍 OK：33 units / 6 layers / 9 collect groups）。工作流已把红测命令登记为 `.workflow/config.json` 的 `client-next-architecture-red`（非测试角色改动）。

## 豁免与说明

### 5.1 测试框架：NUnit → xunit（需主代理知悉/裁决）

任务书写的是 NUnit，但本仓库 `Tests/**` 六个测试工程统一用 **xunit 2.9.2**，未引用 NUnit；若按字面引入 NUnit：

1. 必须改 `LiteFramework.Core.Tests.csproj` 增加 `PackageReference`（属配置改动，超出测试角色权限）；
2. 在改 csproj 之前，`[Test]`/`TestContext` 会让整个工程**编译失败**——而本任务的硬要求是"红测必须是断言失败，不是编译错误"。

因此本轮按仓库现行 `xunit` 落地：等价能力为 `[Fact]`/`[Theory]`/`Assert.*`，仓库根定位由 `TestContext.CurrentContext.TestDirectory` 换成 `AppContext.BaseDirectory`（xunit 下同语义：测试输出目录向上找 `AGENTS.md` + `Assets/`）。若主代理坚持 NUnit，需要先批准 csproj 改动并在独立提交里做框架迁移（不建议在红测批次里混入）。

### 5.2 Unity EditMode 用例延后补写（编译全有全无）

C# 编译是**全有全无**的：`ClientNextBootTests`/`ClientNextUiRuntimeTests`/`ClientNextAppFlowTests` 必然引用 `GameEntry`、`ClientApp`、`UiRuntime`、`PageBase` 等类型；这些类型在 T2/T3 之前不存在，先写会直接把 `Assets/Tests` 编译打断——`dotnet test`/Unity Test Runner 都会变成**编译错误**，红测证据失真（也是任务禁止的失败形态）。

替代验证（本轮已执行）：

1. 用静态契约把"骨架必须出现什么"钉成可执行判据（CN-01～CN-09，§3 已红）；
2. 把行为用例清单与断言点先写进本计划（UX-01～UX-10），T2 编译通过后按同一批文件补写，不改变判据口径；
3. `BootNext` 启动链的最终判据仍归 T6 的 EditMode/PlayMode 运行，静态测试**不得**被当作"可运行"证据。

### 5.3 未验证项（本轮明确不覆盖）

- `UiRuntime` 真实行为（栈/模态/Back/缓存/输入阻断/转场/焦点）——未实现，无法测。
- `PageBase` 绑定与序列化挂载——未实现。
- `BootNext` 60s 启动、`ClientApp` 组合与取消、`AppFlow` 登录→大厅链路——未实现。
- PlayMode 端到端、Player/IL2CPP 冒烟、HybridCLR hot 装载——无产物，且 `Library/HybridCLR` 缺失时构建会静默跳过（基线风险，不能当绿）。
- 短链路触碰面 G4（新增页面 ≤2 文件/0 中央登记）——需实测改动样本，属 T5 的脚本侧，不在本文件的静态扫描范围。

## 变更记录

- 2026-11-11：初版（测试角色）。落地 `ClientNextArchitectureTests.cs` 9 例（8 红 1 绿）；新增 `arch/client-roots.json` 作为根路径单源；填入策略、用例/任务映射、红测证据、运行方式与豁免（NUnit→xunit、Unity 用例延后）。


