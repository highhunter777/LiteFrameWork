# 方案评审（独立子代理）：客户端架构重写（不继承旧设计）

> 运行 client-rewrite-architecture · 创建于 2026-11-11
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：`.workflow/active/client-rewrite-architecture/plan.md`（方案与验收标准）
- 代码/文档基线：git HEAD `8ca025b3774fe30b4663e570cd4fe58778127098`（分支 `main`）；关键路径 `Assets/LiteGame`、`Packages/com.litegame.framework`、`Packages/com.litegame.net`、`scripts/codegen/gen-build-hash.py`、`scripts/gate/hot-boundary-check.py`、`ProjectSettings/HybridCLRSettings.asset`
- 评审范围与不在范围：评审方案的可行性、范围、根因证据、设计契约、迁移、测试与验收可判定性；不评审具体代码实现，不修改任何文件
- 子代理标识：首轮 `4e60b666-4dad-40bb-a354-9f48764214b9`；复评 `a6fb21f9-12c2-4e07-bad5-a24479d9d587`
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 方案检查

- 目标可观察、范围与非目标清晰：**部分通过**。目标（功能内聚、短链路、4 概念、可运行迁移）可观察；但首轮发现 P0/P1/P2/P3 的功能边界与 AC4/AC5 存在矛盾，已修订为 P0/P1/P2/P3a/P3b，并把本轮验收收缩为 AC1–AC3。
- 现状与根因有代码/配置/文档依据：**通过（修订后）**。17 段/ComposeAfter/PageServices/生成链/五层 Scope 均有代码与子代理实测触碰面证据；首轮指出的 asmdef/引用/页面计数已按实测修正（20 个运行时 asmdef、App references 28 项、Pages 13 文件约 1.1k 行、Abstractions 14 目录）。
- 候选方案取舍充分，推荐理由回应约束：**通过**。A 新树并行 + 功能优先 vs B 原地 strangler vs C 收敛重构；A 能同时满足“不保留旧设计/迁移期可运行/可度量短链路”，B/C 的代价已写明。
- 设计覆盖接口、数据、关键流程、失败与边界：**部分通过→修订后通过**。首轮指出 ErrorPage 归属、AppFlow 终态、Battle Tick 所有权、暂停/失焦、重连 AC 缺失；已补系统页归属、终态表、Tick 所有权、AC4c。
- 通用性与扩展点明确，非特判式补丁：**通过**。新增功能/页面/设置/HUD/平台能力各有扩展点；明确禁止全局注册/生成回潮。
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：**部分通过→修订后通过**。首轮指出“新树存在”静态测试是同义反复；已改为 P0 静态契约 + UiRuntime fake loader 行为测试 + 真实 LoginPage 验证口径；L2 基线不绿、HybridCLR 未安装已诚实写入。
- 风险、监控与回滚可执行：**通过**。已补共享源误删、旧测试/asmdef/生成物未同批处置、BindNode 依赖、L2 口径、删树/切 Boot 分提交与 tag。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 架构设计成立 | 新设计文档 + 独立子代理评审 | 可判定 | 文档路径、废除清单、评审 PASS 均可检查；索引/Doc 门禁同批 |
| AC2 新树可运行 | `BootNext` 进占位页/LoginPage/ErrorPage + 新 EditMode 用例 | 可判定 | 需要先建新测试 asmdef 与旧测试隔离；静态禁止 token 可扫描 |
| AC3 短链路达标 | 记录新增页面/改按钮/新增功能/新增设置的文件清单 + 冷启动子代理 | 可判定（修订后） | 口径改为 2 代码文件 + 1 prefab、0 中央登记、允许 1 次本地化生成；页面/组件类 public 例外 |
| AC4a 核心闭环 | PlayMode 端到端 + Player 冒烟 | 可判定（后续运行） | 依赖 P1/P2 完成与 Player 构建接线 |
| AC4b 旁路页面 | PlayMode 设置/战绩页 | 可判定（后续运行） | 不打断 AppFlow |
| AC4c 恢复 | 断线/重连/401/取消用例 | 可判定（后续运行） | 至少一条假传输/本地服用例 |
| AC4d Dev/GM 平价 | F1/F2/F3 + 本地服 + GM 进入/退出/传送 + 发布剔除 | 可判定（后续运行） | 不要求编辑器工作台 |
| AC5 切换完成 | 删树清单 + 共享源迁出 + 门禁 + 文档 | 可判定（后续运行） | 删树与切 Boot 分提交；L2 口径已统一 |

## 发现的问题与处理

### 首轮阻塞问题（已处理）

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| B1 | blocker | 删旧树会删服务端共享源（`Modules/Config/Table/*`、`Meta/WireSubModule/**`） | 新增“共享源（删旧树前必须先迁出）”章节；P1 共享源迁出任务；未迁出不得删树；同批改两份 csproj + `SHARED_LINK_FILES`/`META_EXTRA_FILES` + BuildHash | 已解决 |
| B2 | blocker | AOT/hot 分区自相矛盾（LiteNet 同时写成 AOT/hot；漏 `LiteCodeGen.Runtime`） | 以 `hot-boundary-check.py` 为单源重写：AOT=`LiteFramework`/`LiteNet`/`LiteGame.Bootstrap`/`LiteCodeGen.Runtime`；hot=`LiteGame`/`LiteSim`/`LiteSync`/`Luban.Tables`；明确 LiteCodeGen 剥 BindNode 后删除/吸收 | 已解决 |
| B3 | blocker | AC4 自相矛盾、ErrorPage/Dev/GM/重连无 AC | 拆 AC4a 核心闭环/AC4b 旁路页面/AC4c 恢复/AC4d Dev-GM 平价；ErrorPage 归 `Platform/Ui/Content`；P0 加 LoginPage/ErrorPage | 已解决 |
| B4 | blocker | AC5 不可执行（旧测试/asmdef/生成物/收集组/构建/场景/热更未纳入；“全绿”冲突） | 增加“删树前检查清单”5 项；P3 拆 P3a/P3b；L2 口径分切换前/后；删树与切 Boot 分提交 + tag | 已解决 |
| B5 | blocker | AC3 与 Unity/本地化事实冲突（1 文件、页面类 public、文案表生成、资产漏算） | AC3 改为 2 代码文件 + 1 prefab、0 中央登记、允许 1 次本地化生成；页面/组件/数据类 public 例外；资产/收集组/测试/构建同批记录 | 已解决 |

### 首轮非阻塞问题（已处理）

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| M1 | major | 现状量化数据错误 | 按实测改为 20 个运行时 asmdef/App 28 references/Pages 13 文件约 1.1k 行/Abstractions 14 目录；删除不准确的接口计数 | 已解决 |
| M2 | major | “不保留旧设计”未定界 | 增加“复用实现、换新契约/复用行为、重写/不复用随旧树删除/保留不动/删除吸收”五类表 | 已解决 |
| M3 | major | 迁移资产路径与构建/热更接线缺失 | 增加迁移接线清单（Build Settings、HybridCLR hotUpdateAssemblies、link.xml、YooAsset 收集组、Player 冒烟、Tests.slnx、arch 模型）；P1 共享源迁出 | 已解决 |
| m1 | minor | AppFlow 缺终态/取消分支 | 增加终态处理表与 Tick 所有权/暂停说明 | 已解决 |
| m2 | minor | Battle RunAsync/Tick 所有权未定义 | `ClientApp.Tick` 驱动，RunAsync 只等 TCS；五段顺序/重连 AC 补入 | 已解决 |
| m3 | minor | Platform 聚合注入像服务定位器 | 改为功能只拿 1–3 个具体服务切片，`Platform` 只住 `ClientApp` | 已解决 |
| m4 | minor | UiRuntime parity 清单不足 | 增加 P1 parity 清单（列表/打字机/动效/CanvasScaler/InputSystem/SafeArea/缓存/模态） | 已解决 |
| m5 | minor | 风险表重复 | 去重并补共享源/旧测试/BindNode 风险 | 已解决 |
| m6 | minor | R11 与客户端重写无关 / “OA 模板”笔误 | 标注 R11 仅共享内核/服务端；改为“预制体模板” | 已解决 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 首轮 `4e60b666-4dad-40bb-a354-9f48764214b9`；二轮 `a6fb21f9-12c2-4e07-bad5-a24479d9d587`（超时中断）；文本复评 `cb04e0e3-7297-4bea-9c8c-0c02f1cc5ead`
- 评审时间: 2026-11-11

## 复评记录

- 首轮 FAIL（5 个 blocker：B1 共享源、B2 AOT/hot 分区、B3 AC4、B4 AC5、B5 AC3）；修订后由 `cb04e0e3-...` 做文本复评，结论 FAIL，剩余 R1（共享源根未定/未登记）与 R2（AC5 删树与切换基线定序冲突）。
- 主代理按 R1/R2 修订：共享源根定为 `Packages/com.litegame.shared/`（`LiteGame.MetaWire`/`LiteGame.SimConfig` 两个 hot asmdef），同步 csproj、`SHARED_LINK_FILES`/`META_EXTRA_FILES`、`build-hash`、服务端构建、`hot-boundary` 分区、`arch/units.json`/`ScanTargets`，发布顺序先服后客户端；AC5 改为 P3a（旧树/旧测试仍在）→ 切 Boot（旧树/旧测试仍在）→ P3b 同一提交删旧树/旧测试/旧 asmdef/生成物/收集组/场景引用，删树与切 Boot 两提交 + tag。
- 最终复评：**PASS，剩余阻塞 0**；R1/R2 均解决。非阻塞项已顺手修正：现役计数改为 **10→6+Dev**；AC4b 拆“设置 P1 / 战绩 P2”；切 Boot 时 Build Settings 只留新入口、P3b 摘旧收集组并做地址重复检查。
- 复评输入：修订后的 `plan.md`；评审方式为独立子代理文本复评（不修改生产代码/测试/门禁）。
- **迁移形态 delta**：用户 2026-11-11 选择“原地 strangler”（`Assets/LiteGame/Next`，旧目录原位保留）。delta 复评第一轮发现 R3：P3b 提升后 G2/G3/AC2 的 Next 路径会空扫、AOT Bootstrap 落回 hot 前缀；主代理新增 `arch/client-roots.json` 路径单源、P3b1 删旧/P3b2 提升、G2/G3 非空扫描 + AOT 优先级复验、G3 白名单（允许 `LiteFrameworkNext` 平台类型，禁旧契约）。最终 delta 复评 **PASS，剩余阻塞 0**。
- 非阻塞项已顺手修正：`LiteFrameworkNext` 进 §10 程序集表与 AOT 计数（现役 AOT 9→目标 6+Dev）；`arch/client-roots.json` 登记进 `arch/units.json` 与 AC5 第 5 项。
