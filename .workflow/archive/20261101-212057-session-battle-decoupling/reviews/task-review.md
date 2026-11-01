# 任务评审：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00
>
> 本文件由主代理根据独立子代理返回整理落盘；不是主代理的自评。

## 评审输入

- 方案：`plan.md`
- 任务清单：`tasks.md`
- 用户已确认验收标准：`acceptance.md`
- 代码/文档基线：`git rev-parse HEAD` = `48ccda6179c9e7369d2ebe6966af1e9167094db4`；工作区含并行改动（不计入本方案）
- 子代理标识：Qoder Agent（general-purpose，独立上下文）
- 子代理约束：只读评审，不修改生产代码、测试与门禁
- 评审范围：`plan.md`、`acceptance.md`、`tasks.md`；静态核对 `Assets/LiteGame/Modules/Session/**`、`Modules/Battle/**`、`Assets/Tests/**`、`Tests/LiteNet.Tests/**`、asmdef、`domain-matrix.py`、`unity-tests.py`
- 评审时间：2026-11-01

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 | T2/T3/T5 | 部分 | T2 verify 的全局 grep 与“只加端口”自相矛盾 |
| AC2 | T1/T7 | 部分 | T1 白名单口径混用（漏 `RoomSessionFace`） |
| AC3 | T2/T6 | 部分 | T6 未跑 L1（asmdef 允许表） |
| AC4 | T6 | 缺口 | 无目录清单检查命令 |
| AC5 | T7 | 通过 | L1 的 `ProcedureBoundaryTests` 可验 |
| AC6 | T7 | 部分 | 无代码区扫描命令 |
| AC7 | T4/T8 | 部分 | 无代码区扫描命令 |
| AC8 | T8 | 通过 | `domain-matrix --check` |
| AC9 | T4/T7/T8/T9 | 缺口 | PlayMode `BattleSmokePlayModeTests` 无任务承接 |
| AC10 | T9 | 通过 | 文档清单完整 |

## 检查项

- 任务粒度可独立执行与验证：**不通过**。T2/T3 产生不可编译中间态；T4 实际依赖 T5、T6 实际依赖 T7；T6/T7 过大。
- 依赖关系无循环，顺序合理：形式线性，但实际依赖图不成立（见 B3/B5）。
- 每项任务有明确验收标准与验证方式：部分不通过。T2 grep、T5/T6 的 `domain-matrix` 时机、多 filter 命令不可执行。
- 测试策略足以覆盖验收标准：部分通过。T1 可给 A1 红证据，但 A2 红证据未写入 T7 验收；PlayMode 未承接。
- 方案中的通用性与扩展性要求已落到任务：通过。
- 不存在硬编码、特判式补丁或范围外改动：未发现拆程序集/改共享源集/协议/收口 `IBattleClient.Client`；但 T8 修改门禁脚本与施工子代理边界冲突。
- 回滚与风险处理已安排：部分通过。A1/A2 边界在方案中，任务未设检查点；T6 仅声明 move_asset/六面核对。

## 发现的问题与处理

| ID | 严重度 | 任务 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B1 | blocker | T2 | T2 要求“`new BattleClient(` 仅工厂”，但描述明确“只加端口/工厂、不接线”；`AccountSessionFactory.cs:91` 仍会 new，T2 verify 的全局 grep 必红 | T2 verify 改为只检查 `BattleClientFactory.cs` 内恰 1 处；全局 AC1 grep 移到 T3/T5 | 已修待复评 |
| B2 | blocker | T1/T3 | T1 白名单只有 `MatchSessionFactory`/`BattleSession`，但 `RoomSessionFace.cs:10` 仍持 `BattleClient`；T3 执行全量 L1 时 T1 守卫必红 | T1 白名单扩为 A1 过渡四文件（含 `AccountSessionFactory`/`RoomSessionFace`，带批次号）；T5 收窄到最终 2 文件；T3 不再跑全量 L1 | 已修待复评 |
| B3 | blocker | T4/T5 | T3 改 `AccountSessionFactory` 构造、T4 改 `MatchSessionFactory`（去 `IMapProvider`），`SessionComposition` 到 T5 才改，T3/T4 的 Unity 0 错不成立；T4 文件清单缺 `IBattleRunFactory`/`IMatchSessionFactory` 签名更新 | `BattleComposition`/`SessionComposition` 接线并入 T3；T4 同步 `SessionComposition` 与两个端口签名；T5 只做守卫收窄与 A1 全量验证 | 已修待复评 |
| B4 | blocker | T5/T6 | T2 新增 3 个 Battle 顶层端口后，`domain-matrix --check` 在 T8 `--write` 前必报“矩阵过期”，T5/T6 verify 均含该命令 | T5/T6 移除 `domain-matrix`；只在 T8 `--write` 后 `--check` | 已修待复评 |
| B5 | blocker | T6/T7 | T6 把 `BattleSession` 搬入 Battle，但其仍调 `DevHooks.Diagnostics`（Modules/Dev）；Battle asmdef 不引 `Modules`，T6 Unity 0 错不成立 | 诊断注入/DevHooks 移除并入 T6（中间态可传 null），`GameEntry` 生产注入留在 T7 | 已修待复评 |
| B6 | blocker | T4/T7 | `unity-tests.py --filter 'A\|B\|C\|D'` 要求完整 filter 出现在单个 FullName，四组用例必判“filter not reflected” | T4/T7 verify 拆成 4 条独立 `--filter` 命令 | 已修待复评 |
| M1 | major | T6/T7/T8 | AC4 目录清单、AC3 allowlist、AC6 DevHooks、AC7 Map/Dev 域均无真实检查命令 | T6 补 L1（含 `ClientAsmdefAllowlistTests`）、目录 `find`、DevHooks grep；T8 补 AC7 代码区 grep；T7 补 L1 | 已修待复评 |
| M2 | major | T7/T8 | AC9 的 PlayMode `BattleSmokePlayModeTests` 无任务承接 | T8 增加 `unity-tests.py --mode playmode --filter BattleSmokePlayModeTests` | 已修待复评 |
| M3 | major | T8 | T8 计划修改 `scripts/gate/domain-matrix.py`（门禁脚本），与“施工子代理不改门禁脚本”冲突 | T8 明确：`domain-matrix.py`/`18-域间引用矩阵.md` 由主代理执行；施工子代理只做代码注释与测试收口 | 已修待复评 |

## 评审结论（首轮）

- 评审方式: 独立子代理
- 评审轮次: 第 1 轮
- 复评范围与结果: 待第二轮复评
- 首轮结论: FAIL
- 首轮阻塞问题: 6 blocker + 3 major（B1～B6、M1～M3）
- 评审人: Qoder Agent（general-purpose，独立上下文）
- 评审时间: 2026-11-01

## 第二轮复评（2026-11-01）

- 评审方式: 独立子代理（Qoder Agent，general-purpose，独立上下文）
- 评审输入: 修订后的 `tasks.md`、`plan.md`、`acceptance.md`、首轮本文件、代码基线 `48ccda61`；静态核对
- 第二轮结论: **FAIL**（B1/B4/B5/B6 已修；B2 引入新问题、B3 部分修；新增 RB1～RB3、RM1～RM2）

### 阻塞问题

| ID | 严重度 | 任务 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| RB1 | blocker | T1 | T1 白名单扩为 4 文件后，当前 Session 具体引用恰在这 4 文件内，扫描会直接绿，无法产生红证据；且与 AC2/方案“A1 白名单 2 文件”冲突 | T1 恢复 2 文件白名单，另加 `AccountSessionFactory.cs` 不含 `new BattleClient(` 的独立断言；当前因 AccountSessionFactory/RoomSessionFace 而红，T3/T4 清除后 T5 转绿 | 已修待第三轮复评 |
| RB2 | blocker | T3 | T3 改 `AccountSessionFactory` 为 7 参，但 `MatchLoopLeakEditModeTests.cs:83` 仍 4 参且不在 T3 文件清单，Unity 重编译必失败 | `MatchLoopLeakEditModeTests.cs` 加入 T3 并同步 7 参构造（IBattleClientFactory/地图委托替身） | 已修待第三轮复评 |
| RB3 | blocker | T4 | T4 将 `MatchSessionFactory` 改为吃 `IAccountRuntime`，但任务未写明用 `account.CreateMatchServices()`（原 `account.Services.CreateScope` 会编译失败） | T4 描述补“经 `account.CreateMatchServices()` 建未 Seal Match 子域（注册后 Seal）” | 已修待第三轮复评 |
| RM1 | major | T3/T5 | AC1 全局 grep 未落到 T3/T5 | T3/T5 增加 `grep -rn 'new BattleClient(' Assets/LiteGame --include=*.cs`（仅 BattleClientFactory） | 已修待第三轮复评 |
| RM2 | major | T6/T8 | T6 的 DevHooks grep 会命中文档注释（`IBattleDevDiagnostics.cs:7`）；T8 的 IMapProvider grep 会命中 `BattleContext.cs:20/85/154` 注释与 `:168` 错误串 | T6 清理 `IBattleDevDiagnostics` 注释中的 DevHooks 字样并纳入文件表；T8 明确同步 `BattleContext.cs:20/85/154` 注释与 `:168` 错误文案后 grep 0 | 已修待第三轮复评 |

### 非阻塞问题（second round，均已吸收）

- T6 补 `Modules/Session` 剩余清单的 `find` 核对（AC4 第二半）。
- 新增 T10 `[主代理] 域矩阵脚本与生成物`，由主代理执行 `domain-matrix.py`/`18-域间引用矩阵.md`；T8 文件表移除门禁脚本，只做注释/守卫/PlayMode。
- T7 的 A2 红证据移至 T6 开始前采集；T7 只做装配移交与保持绿。
- T9 补 `Docs/施工进度/README.md` 卷内章节数 19→20。
- T6 的 `find` 标注 Git Bash 执行。

### 未验证/限制

未运行 dotnet/Unity/门禁；结论基于 HEAD 源码与任务清单静态核对；迁移目标路径当前不存在符合计划；工作区并行改动可能影响行号。


## 用户豁免（2026-11-01）

用户明确指示：“好慢，任务默认情况下不用评审。”据此跳过第三轮独立任务复评，保留首轮/第二轮记录；第二轮阻塞项（RB1～RB3、RM1～RM2）已由主代理按评审意见修订入任务清单，未再开独立复评。后续任务变更仍按施工子代理边界与主代理核验执行。

## 评审结论

- 评审方式: 用户豁免（跳过独立任务评审；保留前两轮独立评审记录）
- 结论: 通过
- 阻塞问题: 0
- 评审人: 用户裁决（highhunter777）
- 评审时间: 2026-11-01
- 豁免说明: 按用户 2026-11-01 指示跳过任务评审；第二轮阻塞项已修订，未再独立复评。
