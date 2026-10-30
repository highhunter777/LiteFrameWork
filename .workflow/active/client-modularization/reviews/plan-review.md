# 方案评审（独立子代理）：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 创建于 2026-10-30
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：`.workflow/active/client-modularization/plan.md`（方案与 A1–A31 验收标准）
- 代码/文档基线：git `HEAD=9176b409` + 工作区未提交 WIP（2026-10-30 17:33–17:39，Unity 编译 0 error 0 warning）；证据母本 `.workflow/active/module-boundaries/plan.md`（147 文件域审计 / 403 文件机械扫描 / 程序集环实测）
- 评审范围与不在范围：范围＝交付 A（批 1~5：装配段演化 / 业务域垂直化 / `IConfirmDialog` / 组合根 / 外层域边界）；不在范围＝交付 B（LiteSync、LiteSim 参数化）、交付 C（生成器收口）
- 子代理标识：`plan` 子代理（独立上下文，共 13 轮；末轮标识 = 第十三轮独立复评）
- 子代理约束：只读评审，不修改生产代码、测试与门禁；不得提交 git

## 方案检查

- 目标可观察、范围与非目标清晰：**通过**。目标为"业务域垂直自持 + 装配就地 + 边界守卫化"，非目标显式列出（不搬进框架、不新增横向表现程序集、不动 Probes/Fallbacks、交付 B/C 排除），并有"允许的框架改动白名单"（`FrameworkInfrastructure.Create`/`Clocks.Create`/`Events.Create`/`ClientHost.ComposeRoot`（含非模块装配窗口）/`VfxService.ActiveCount`）。
- 现状与根因有代码/配置/文档依据：**通过**。子代理逐轮实测核对：13 个 `*Module.cs`、`Modules/` 下 15 个域目录、`*Composition.cs` = 0、`Core/Battle` 三端口、4 处 `is 具体适配器`、7 个守卫文件引用 `Bootstrap/Modules`、`Adapters.Unity → Modules.Input/Camera` 与 `Adapters.Luban → Modules.Config` 两条环、`Presentation` 必带 `Adapters.Unity`。
- 候选方案取舍充分，推荐理由回应约束：**通过**。V1/M1/C1/B1/F1/G1/S1 与 V2/V3/M2/M3/M4 的否决理由均落在实测约束（程序集环、HUD 并入 `Ui` 成环、Core 硬边）。
- 设计覆盖接口、数据、关键流程、失败与边界：**通过**。18 段全序按"谁 `Require` 谁"排定并经子代理逐段核对无倒挂；能力端口（`IRenderCameraSource`/`IAimCameraSink`/`ILookDevice`）沿既有 `IAimWorldSink` 范式；`ContainerModule` 拆解归属表覆盖产物键 / Tick 序（13 项单点声明）/ 释放 / 开发装饰 / 传输工厂端口 / `ProductSettings` 与 `LookPreferences` 单实例；失败语义（未装载即抛、端口未实现跳过、`ComposeRoot` 回滚）逐条写明。
- 通用性与扩展点明确，非特判式补丁：**通过**。A30 给出"新增业务域步骤"（目录 + 按需 asmdef + 段 + 一行注册 + 仅经 `Abstractions/`），并写明"不改共享能力域代码、但会改两处守卫表"与"跨程序集门面三处同步"。
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：**通过**。每批独立取红（`CompositionBudgetTests` T2 前、`DomainBoundaryTests` 批 2 开头、`Baseline` 清空批 3），批末全量绿；`red_commands` 三条 L1 过滤器；A12 由仓内 `BattleSmokePlayModeTests` 承载并接入 `commands`。
- 风险、监控与回滚可执行：**通过**。8 条风险（含批 2 asmdef 合并/`.meta` 迁移/能力端口退化/`ComposeRoot` 时序/批 2 `CS0104`），监控为每子步编译 0 error + 每批 L1 + Play 冒烟，回滚以批/子步提交为界并说明 asmdef 删除后必须整批 revert。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| A1 / A15 / A15a | 目录与 `*Composition.cs` 计数、注册序、`Require` 全可用 + Tick 序回读 + 释放探针 | 是 | 段数账 14 → 15 → 18 自洽；Tick 13 项与 `ContainerModule` 原文逐位一致 |
| A2 / A5 / A7 / A8 / A8b / A9 / A10 / A10a | 目录存在性、asmdef 引用集与允许表、硬边 case、friend 集合 | 是 | 含"`Modules.Battle` 仅放行 `Adapters.Unity`"与"改前为绿、合并后必红"的取红说明 |
| A3 / A4 | `new` 白名单与能力端口 grep；工厂委托调用链；`Camera == null → Camera.main` | 是 | 委托注入避免 `Modules.*→App` 环；判据有判别力（首采样前为 null） |
| A6 | 6 文件 7 处路径 + 3 个预算键 + 扫描面 + 身份解析 | 是 | 锚点以符号为准（行号仅参考）；拆方法后删 152 行键 |
| A11 / A13 / A14 / A19 / A20 / A21 / A22 / A24 / A25 / A26 / A27 / A28 | 守卫断言、类型→文件反查、路径断言、反射用例、矩阵 `--check` | 是 | A19 用例含真实依赖（`BattleScenes`/`IMapProvider`）；A28 为 12×12 并接 `commands` |
| A12 | PlayMode 用例计数与几何容差 | 是 | harness/存取口/观测面/离场口径四段全部写死，越肩几何带容差 |
| A16 / A17 | 预算（类 ≤400、方法 ≤80、冻结只降）与手测全链 | 是 | `ClientHost` 454/83/86 与 `AppComposition` 预算先定 |
| A23 | 文件级三处同批（asmdef 引用 + 允许表 + friend） | 是 | 走后向无环方案（`BindGlobals` 留 LuaBridge） |
| A29 / A30 / A31 | `commands` 全绿、步骤文档、文档回写清单 | 是 | 门禁含新增 `battle-smoke-playmode` 与 `domain-matrix` |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R1-1 | blocker | A1 要求批 1 结束即 16 段，与 A13/A15/A18 矛盾 | 段数按批重算（批 1 = 14、批 2 = 15、批 4 = 18），A1/A15 重写 | 已修复 |
| R1-2 | blocker | A4 禁 `is` 具体适配器但无端口设计 | 新增 `IRenderCameraSource`/`IAimCameraSink`/`ILookDevice` 三端口（沿 `IAimWorldSink` 范式） | 已修复 |
| R1-3~7 | major | 守卫实为 6 文件 7 处；"一域一程序集"与目标结构不符；合并后必带 `Adapters.Unity`；预算取红时机未定义；acceptance 空模板 | 逐条改方案（后三项落到 A6/A10a/测试策略/验收表） | 已修复 |
| R2-1~4 | major | A1 分解式凑不出 13；批表"13 段就地"自相矛盾；A12 依赖仓外工具；A28 维度与门禁空转 | 改写 A1/批表；A12 改仓内 PlayMode 用例；A28 改 12 域并接 `commands` | 已修复 |
| R3-1~3 | major | 批 4 注册序倒挂（Procedure/Meta、Session/Input）；归属表漏 Tick 序/释放/`IAccountReentry`；身份解析按文件名 | 重排段序并逐步收敛为 18 段；补归属表逐行；A6 增"类声明提取身份" | 已修复 |
| R4-1 | blocker | `ContainerModule` 拆三段后 `SceneService`/`IMapProvider`/`CommandCenter` 无归属，段序倒挂 | 场景/缓存归 Content、派发器/命令中心归 PI、地图独立成段、跨域装订归 `ComposeAfter` | 已修复 |
| R4-2~6 | major | Tick 序分散会改全局序；A6 与 A16 的 152 行冲突；A18 位次错；A12 观测成员名不存在；A4 缺构造签名/asmdef 变更 | Tick 单点声明（13 项）；改"拆方法后删键"；A18 改 ⑰；A12 成员写实；A4 写委托调用链 | 已修复 |
| R5-1 | blocker | `ILookPreferences` 与 `ProductSettings` 双实例 → 设置改灵敏度对局不生效 | 改为单实例（Settings 建、ProductSettings 注入、Battle 只读） | 已修复 |
| R5-2~4 | major | 预算键数写错（实为 3）；重复段落；A19 判据可假绿 | 改 3 键；删重复；A19 改"引用类型集不含 `Modules/Procedure/**`" | 已修复 |
| R6-1 | blocker | `ILookPreferences` 无 `Refresh()`，构造注入不可编译 | 保持接口只读、`Refresh` 留具体类、Settings 同时 `Put` 具体键与接口键 | 已修复 |
| R6-2~3 | major | A12 观测面不可达；A23 跨程序集通路会成环 | A12 补 `LastCreated`+访问器；A23 定"`BindGlobals` 留 LuaBridge + friend" | 已修复 |
| R7-1 | major | A12 缺"真对局 rig 实例"通路与进局 harness | 写死 harness（`Boot.unity` + `Started && Current == Main` + `TestModeRuntime`）与 `LastCreated` 装饰器 | 已修复 |
| R7-2 | major | A23 friend 通路不闭合（缺 asmdef 引用与允许表） | A23 补三处同批 | 已修复 |
| R8-1 | major | A10a 与 `硬边_反向与跨层引用一律失败` 冲突 | 同批改 case：Battle 仅放行 `Adapters.Unity`，注释登记例外 | 已修复 |
| R8-2 | major | 合并后 friend 集合漏既有 4 条 | `Modules/Battle/AssemblyInfo.cs` = 4 条 + PlayMode | 已修复 |
| R9-1 | major | 工厂接口键注册落点放 `BattleComposition` 在批 2 不可执行 | 批 1–3 = `ContainerModule.CreateBattleViews` 处注册；批 4 = `BattleComposition` | 已修复 |
| R9-2 | major | 批 1 无 `ComposeAfter` 调用方 | 批 1–3 过渡落点 = `ContainerModule.Assemble` 在 `Seal()` 前调用 | 已修复 |
| R10-1 | major | A4 前置工厂取不到 `IDevSession`（`matchRules` 读口丢失） | 改"工厂委托注入 + 段内调用"，避免 `Modules.*→App` 环 | 已修复 |
| R10-2 | major | A4 判据改造前即真（无判别力） | 改"首采样前 `Camera == null`，`ComposeAfter` 后 `== Camera.main`" | 已修复 |
| R13-m1~m5 | minor | 哨兵冗余；`Modules.Input` 需加 `LiteGame.Core` 引用；越肩几何缺容差；行号锚点易漂移；硬边 case 取红说明 | 逐条并入 A4/A1/A12/A6/A10a | 已修复 |
| （记录） | 流程 | 第十三轮复评子代理首次调用因连接错误中断，已重开并在新上下文中完成判定 | 无 | 已处理 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: `plan` 子代理（独立上下文；共 13 轮，前 12 轮分别给出 FAIL/blocker，逐条修复后复评）
- 评审时间: 2026-10-30
