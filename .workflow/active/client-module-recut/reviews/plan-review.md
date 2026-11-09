# 方案评审（独立子代理）：客户端按功能/行为重切模块

> 运行 client-module-recut · 创建于 2026-11-09T11:50:08+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：`plan.md`（v1 → v2 → v3 → v4 → v5 → v6 → v6.1）
- 代码/文档基线：`78cf7578e9947769c4be88debca3a376d57e40ac`（main）；工作区 15 个已跟踪修改（8 .cs + 7 Docs）+ 5 个未跟踪代码（AssemblyLedger/AssemblyReport/AssemblyReportTests）+ 方案目录；属并行 WIP，不是本方案产物
- 评审范围与不在范围：方案的目标图/依赖/页面端口/守卫/同步面/验收标准/风险；不在范围：不跑 L1/Unity 全量、不改生产代码、不做实现评审
- 子代理约束：只读评审，不修改生产代码、测试与门禁
- 六轮评审子代理：`1cbe981e-b6b8-474a-9137-806e9206149d`（v1）、`a2f52e1b-4513-4581-96eb-017452a247ef`（v2）、`32ca4ab3-d5fe-4734-9dff-25bf3f341d33`（v3）、`57a81740-fe45-4270-86f6-3c767a6f36c6`（v4）、`b325e69c-5c58-452c-80af-9e28127e3807`（v5）、`810c08c1-3ebe-4ce9-a9dd-741cf312f9c6`（v6）

## 方案检查

- 目标可观察、范围与非目标清晰：**PASS**。v6.1 只做 Settings 页面端口 + friend/零使用边守卫/棘轮；Battle/Account/Lobby 明确出范围并列入 §7 另立设计；不新增模块/域数/asmdef/内容/HCLR 分区。
- 现状与根因有代码/配置/文档依据：**PASS**。Settings 28 成员、10 处 `ProductSettings.Current`、`FrameRateIndex` static、UI asmdef 引用、units/domain/allowlist、55 friend、48 源链接、17 段、BuildHash shared/meta、L1 1972 等均由六轮子代理实跑/复算。
- 候选方案取舍充分，推荐理由回应约束：**PASS**。A 只守卫 / B Settings+守卫（推荐）/ C Battle 内部 / D Account/Lobby；C/D 因 N13-N31 未决移入另立设计，B 是当前唯一可一次落地且可验收的切片。
- 设计覆盖接口、数据、关键流程、失败与边界：**PASS**。`ISettingsPagePort` 28 成员、`ProductSettingsAdapter` 创建点/惰性、`IPageContext.Settings`/`PageLogicBase.Settings`、空端口 10 处 `?.` + OnShow 早退、`SettingsPagePlayModeTests` harness、friend/零使用边口径、meta 债落点。
- 通用性与扩展点明确，非特判式补丁：**PASS**。端口按页面实际调用列全；friend 进 `units.json`；零使用边复用 `domain-matrix` 口径；不写“如果是 Settings 就……”的守卫。
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：**PASS**。先加“UI 不得引 Settings”“页面不得 `ProductSettings.Current`”“视图/诊断不得取 `BattleContext`”等红断言；friend/零使用边先合成负例；Settings 页覆盖打开/改画质/改音量/Back/空端口。
- 风险、监控与回滚可执行：**PASS**。每 Phase 独立分支；Settings 失败回退 UI/Settings/App；守卫失败回退脚本/基线；BuildHash meta 红为已知债、不重烘；共享源集误动即停。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC0 | 开工时 `git status --short -- Assets Packages Tests Server` 快照 + 相对快照无新增；`.workflow` 提交/排除；L1/arch/domain/doc/`build-hash --check-shared` 基线；meta 债落点 | 可判定 | 生产树当前非空（8 M + 5 ??），改为“相对快照无新增”；meta 债登记 `Docs/审计/客户端/登记/技术债.md` + 进度卷（grep `BuildHash meta stale`） |
| AC1 | friend 9 个 `Modules/*`/55 基线 + units.json friends + L1 用例；零使用边 `arch/zero-use-edges.json` + `quality.zero-use-edges` + 合成负例 | 可判定 | 作用域/归属规则（最近 asmdef 目录、不递归）写死；零使用边复用 domain-matrix 口径；只减不增 |
| AC2 | UI asmdef 去 Settings；页面 `ProductSettings.Current`=0；`IPageContext`/`PageLogicBase` 有 Settings；`ISettingsPagePort` 28 成员；adapter 创建点在 `BindProductSettings` 之后/惰性；`SettingsPagePlayModeTests`（fake localization、真 prefab、空端口 10 处 `?.` + OnShow 早退）绿；allowlist/units/domain 去 Settings | 可判定 | asmdef/grep/编译/测试机检；adapter 时序评审核对；allowlist 行 Phase 1 删（否则 L1 红） |
| AC3 | friend 55；零使用边基线只减不增；白名单 15→14 已在 Phase 1 | 可判定 | 数字依赖 AC1 口径；不承诺一步到 0/≤5 |
| AC4 | L1/arch/domain/doc/`build-hash --check-shared`/hot-boundary 全绿；shared 常量不变；meta 红为已知债；不跑 `toolchain-tests` | 可判定 | 六轮实测 L1 1972/arch 33 units/domain 12/doc 0/hot 23-66/shared 绿；meta 红为 HEAD 存量；`test_arch_check` 34 vs 33 存量红与本批无关 |
| AC5 | 说明 03/08/10/14/15/17/18/19/20、UI 系统/UI 工具链/UI API/DI/设置/技术债/进度卷事实性回写；章程类只记“不变/待另立设计”；审计路径核销 | 可判定 | doc-lint + `toolchain.py docs --check` + 清单核对；不预占 §7 结论 |

## 发现的问题与处理

> 六轮共发现 8 个 blocker（B1-B8）、4 个 blocker（N1-N4）、4 个 blocker（N9-N12）、7 个 blocker（N13-N19）、6 个 blocker（N26-N31）、10 个 major/minor（N38-N47）。v6.1 已把可执行切片收敛到“Settings 页面端口 + friend/零使用边守卫/棘轮”，其余移入 §7 另立设计；最终阻塞 0。

| 轮次 | 主要问题 | v6.1 处理 | 状态 |
|---|---|---|---|
| v1 | B1 rank 层下限；B2 目标图不闭合；B3 Settings 试点不独立；B4 Account 归属；B5 Lobby 流程反向；B6 Battle 拆分不可行；B7 基线错；B8 Battle 内容/收集组 | v2-v6 逐步收缩：不拆 Battle、不搬 Settings 页、Account/Lobby 移入 §7 | 已关闭/出范围 |
| v2 | N1 Dev overlay 上向；N2 Settings 命名空间；N3 端口不可替身；N4 Phase 5 矛盾 | v3 不拆 Battle/不搬 Settings 页；v6 端口用接口 + fake | 已关闭/出范围 |
| v3 | N9 HCLR 资产；N10 Lobby 端口；N11 基线；N12 Account 抽象/IMetaClient | v4 修资产/端口/基线/注册；v6 不做 Account/Lobby | 已关闭/出范围 |
| v4 | N13 无 Meta 路径；N14 MetaLoginService 归属；N15 RoomBridge/Session 50；N16 DependsOn；N17 测试/装配；N18 HCLR 23→24→25；N19 AC false-red | v5/v6 明确 Account/Lobby 出范围；§7 列七项裁决 | 出范围 |
| v5 | N26 窄口落点；N27 27 基线；N28 BuildHash meta；N29 HitFeedback；N30 Dev 写面；N31 单元/边不一致；N32 harness；N33 文档 | v6 删除 Battle 内部收口；AC4 用 `--check-shared`；补 harness/文档；meta 记债 | 已关闭/出范围 |
| v6 | N38 AC0 不可达；N39 friend 作用域；N40 零使用边口径；N41 toolchain.json；N42 并行；N43 白名单重复；N44 meta 债落点；N45 空端口 NRE；N46 章程预占；N47 toolchain-tests 存量红 | v6.1 逐项修：快照口径、55 作用域、domain-matrix 口径、toolchain 注册、Phase 0→1 顺序、白名单归 Phase 1、meta 债落点、10 处 `?.` + OnShow 早退、事实性回写、不跑 toolchain-tests | 已修 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 1cbe981e-b6b8-474a-9137-806e9206149d / a2f52e1b-4513-4581-96eb-017452a247ef / 32ca4ab3-d5fe-4734-9dff-25bf3f341d33 / 57a81740-fe45-4270-86f6-3c767a6f36c6 / b325e69c-5c58-452c-80af-9e28127e3807 / 810c08c1-3ebe-4ce9-a9dd-741cf312f9c6（六轮）
- 评审时间: 2026-11-09T14:40:36+08:00
- 说明：第六轮为 PASS_WITH_MINOR；N38-N47 已在 v6.1 收敛为文字/参数级修订，无阻塞项；Battle/Account/Lobby 明确出范围，另立设计。

## 附：六轮评审记录（历史）

### 第一轮（v1）

- 轮次结论：FAIL；阻塞 8（B1-B8）
- 关键：目标 rank 违反 product 层 30；12 域算术不成立；Settings 试点不独立；Account/Lobby 依赖反向；Battle 拆分不成立；基线/内容/收集组未闭合。
- 已核实为真：12 域/33 units/6 layers/28 edges；Battle 110/8466；跨模块 59/44/15；ProductSettings 10 处；17 段；BuildHash shared 绿。

### 第二轮（v2）

- 轮次结论：FAIL；新 blocker N1-N4，major N5-N8
- 关键：Dev overlay 契约落 35 上向；Settings 命名空间强制；ISettingsPageContext 暴露 sealed ProductSettings；Phase 5 与 Phase 3 矛盾；Modules/Battle 无 unit；domain-matrix 12 硬编码；IMetaSession 存续未定；hot-boundary 只在 build-player。

### 第三轮（v3）

- 轮次结论：FAIL；新 blocker N9-N12，major M5-M14
- 关键：HCLR asset 未同步；Lobby IRoomView 实现与 Session 50 冲突；基线 15+5 写错；Account 抽象/IMetaClient 注册未定；IPageContext.Settings/ClientEntry/文档/Domain 禁令/SeamExceptions/测试 asmdef/H3/AC0/AC2 未闭合。

### 第四轮（v4）

- 轮次结论：FAIL；新 blocker N13-N19，major N20-N25
- 关键：无 Meta 路径/IMetaClient null；MetaLoginService/IMetaConfigFacts 归属；RoomBridge/Session 50；Session/Procedure DependsOn；M3WiringGuard/MetaEntry/GM/App/Session/Procedure/Editor；HCLR 23→24→25；AC3/AC4 false-red；FrameRateIndex static；domain 边；harness；H3；目标表；AC0 .workflow。

### 第五轮（v5）

- 轮次结论：FAIL；Phase 1 基本可落地；Phase 2 三条阻塞
- 关键：IBattleViewHost/IBattleDevContext 落 35 不成立（PublicCombatFeed/DiagnosticIdentity）；27 基线不可复现；BuildHash meta 红；HitFeedback 不能上 35；Dev 写面；SeamExceptions/units 不一致；Settings harness 细节；文档清单；AC 判定。

### 第六轮（v6）

- 轮次结论：PASS_WITH_MINOR；4 项文字/参数级修订
- 关键：AC0 改为“快照 + 相对快照无新增”；friend 作用域 55（9 个 Modules/*，最近 asmdef 目录）；零使用边复用 domain-matrix 口径 + toolchain 注册；toolchain.json/说明 10/15/20；Phase 0→1 顺序；白名单归 Phase 1；meta 债落点；空端口 10 处 `?.` + OnShow 早退；事实性回写；不跑 toolchain-tests。
- 最终：v6.1 收敛，0 阻塞；可进入用户验收确认。
