# 代码评审（独立子代理）：UI 系统 P2-3a 令牌源与主题资产

> 运行 ui-p2-token-base · 创建于 2026-10-24T12:10:40+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- **评审对象**：P2-3a 全部改动（方案 `plan.md`、任务 `tasks.md`、验收 AC1–AC6、测试计划 `test-plan.md`）。
- **代码基线**：`main` @ `0574da90` + 本批工作区改动（`git status --short` / `git diff HEAD --stat`）。
- **评审范围**：令牌源/校验/规范文本/生成器/读面退役/漂移规则/工具登记/三套夹具/文档回写；**不在范围**：并行批次的改动（`Assets/LiteNet/Diagnostics/WeakNet`、`Tests/LiteNet.Tests.csproj`、`Docs/项目说明/05|11`、`.workflow/active/build-preflight|weaknet-injection`、`scripts/build/build-plan.py`）。
- **子代理标识**（两轮，互相独立、只读）：
  - 第 1 轮 `dd97e8d7-cb02-4234-9af7-1f18a9629210`：结论 **FAIL**（1 blocker + 2 major；测试充分性 FAIL、通用性 FAIL）
  - 第 2 轮 `04d9d824-cc13-4127-a09e-2b15f6c2035b`（复评）：结论 **PASS / 阻塞 0**；上轮 3 条全部闭环，无新 blocker/major
- **子代理约束**：只读；不修改生产代码/测试/门禁；可跑 `git`/读取/`unity command eval` 只读探针。

## 变更范围

- **在本批改写面内**：`Assets/LiteGame/Editor/Style/`（4 改 + 8 新）、`Editor/UiTools/Validation/`（IUiRule/UiRuleIds/UiValidator + 新规则）、`Editor/Toolchain/EditorToolHandlers.cs`、`Assets/LiteClient/UI/Style/UiThemeAsset.cs`、`Assets/GameData/Config/UI/UiTheme.asset`、`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`、三套夹具、`scripts/gate/toolchain.json`（+1 条目）、工具索引、设计/进度文档、`.workflow/config.json`。
- **未越界**：无 prefab/场景/字体/图集改动；`Assets/ShareResource/` 零变更；未碰 BuildHash 源集/Luban 表/协议。
- **批外**：`scripts/gate/unity-tests.py` 复用 `_common.repo_root()`——复评确认"与旧 `parents[2]` 等价、无害但非必要"；它同时修复了 P2-2 留下的 `test_single_source_helpers` 基线红（当时全量门禁 1/33 失败的原因），保留并登记。
- **评审后修正**：`scripts/gate/toolchain.json` 首版被整文件重排（+1280/−275），已改为最小 diff（+4/−1，保留并行线新增的 `build.plan` 条目）——复评看到的仍是重排版本，此项由主代理在复评后修掉并在复测记录登记。

## 正确性与边界

- **规范文本/指纹**：评审者用已编译程序集复算 canonical SHA-256 = `78f288ad37507bef02f8781571e45f450c69e2c2718bc0a180664815776a1219`，与 `UiTheme.asset` 的 `SourceHash` 一致；资产 12/5/0×5、ID 排序、`Color32` 读回正确。
- **生成器**：无差异不写盘、失败零写盘、手改后覆盖并报告变更——路径正确；路径守卫初版只查前缀/后缀（可被 `Assets/../..` 穿越），**已改 `Path.GetFullPath` 归一后校验**并补穿越用例。
- **读面**：`UiTokenCatalog` 无静态缓存、缺资产显式失败；`UiStyle`/`UiTextStyle` 已无颜色/字号字面量（夹具扫描源码断言）。
- **规则**：`ui.style.token-drift` 判据与生成器共用 `UiThemeSnapshot`；初版文案只说"不一致/禁手改"，**已改为"SO 过期或手改"**并让夹具断言该文案。
- **评审发现的校验缺口（已修）**：`value`/`offsetX`/`offsetY`/`blur`/`spread`/`durationMs` 缺字段时默认 0 而 0 合法 → 加 `Required.Always`；JSON 重复键被静默覆盖 → 反序列化前 `DuplicatePropertyNameHandling.Error`；七组校验改单表遍历 + 类型分派。
- **`UiStyle` 双处映射（已修）**：12 个 getter 由硬编码 ID 改为 `Get(nameof(X))` 经 `LegacyTokenIds`/`TokenId()` 单一映射；新增"12 个读口 == catalog[TokenId(name)]"夹具。
- **已知非阻塞**：重复键预扫描把任何 `JsonException` 都改写成"重复键"（普通语法错误会被误诊）；`typography` 四字段与 `elevation.color` 未加 `Required.Always`（校验器以空值/范围/枚举兜住）；绝对路径入参会被 AssetDatabase 拒绝而非返回 Fail。

## 测试充分性

- 第 1 轮 **FAIL**：三套新夹具未进 `wf verify` 的 `commands`，最终绿证据缺失；缺重复 ID/必填缺失/清理断言等。
- 修正：`commands` 加入 `ui-theme-tokens-editmode` / `ui-theme-generator-editmode` / `ui-style-retirement-editmode`（36 条）；新增重复键、缺 `value`、缺 `offsetY`、缺 `durationMs`、路径穿越 5 例；两套夹具 `TearDown` 断言临时目录已删；规则用例断言真实 JSON/SO 字节未变；新增 12 读口同源用例与"SO 过期"文案断言。
- 第 2 轮 **PASS**，遗留缺口（非阻塞）：4 个层级必填字段仅以 `offsetY` 代表；"五组非空 + 0 值合法"无正例（真实源五组为空）。

## 通用性与扩展性

- 第 1 轮 **通用性 FAIL**：七组校验是 7 个调用点 + 前缀散落，新增组要改多处；`UiStyle` 映射双处维护。
- 修正：`UiThemeGroups.Enumerate()` 成为组清单单源（组名/前缀/组对象），配 `IUiThemeGroup` 非泛型视图；校验器一个循环 + 类型分派；排序也走同一枚举；`UiStyle` 只保留一张名字→ID 表。
- 第 2 轮 **通用性 PASS / 扩展性 PASS**：新增组只需"组属性 + Enumerate 一行"（校验/排序面），生成面仍需扩快照与结果计数字段——已如实登记为非阻塞。

## 安全与性能

- 路径穿越已堵（归一后归属校验）；`jsonPath` 无守卫但只读。
- 性能：JSON 预解析 ×2 仅 KB 级编辑器文件；工具每次操作只 `Load()` 一次并下传；`UiStyle` getter 每次访问重装 catalog（当前调用点仅测试与 `Distance`，可接受；逐节点调用会退化）。
- 无静态可变状态、无容器注册、无运行期消费者（B1 边界）。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| CR-1 | blocker | 三套新夹具未进 `wf verify` 的 `commands`，AC6 的绿证据缺失 | `commands` 增三条并全量重跑 | 已修复（复评确认登记） |
| CR-2 | major | 校验器未对齐 schema：数值 `value`/层级偏移/动效时长缺字段默认 0 不报；重复键静默覆盖 | `Required.Always` + `DuplicatePropertyNameHandling.Error` + 5 个负例 | 已修复（复评确认） |
| CR-3 | major | `UiStyle` 12 个 getter 与 `LegacyTokenIds` 双处维护 ID | getter 改 `Get(nameof(X))` 单一映射 + 12 读口同源夹具 | 已修复（复评确认） |
| CR-4 | major（测试） | 缺必填/重复键/清理/真实字节不变断言 | 补 5 例 + 3 处断言 | 已修复（复评 PASS） |
| CR-5 | major（通用性） | 七组校验非表驱动 | `Enumerate()` 单表 + `IUiThemeGroup` + 类型分派 | 已修复（复评 PASS） |
| CR-6 | minor | 路径守卫可被 `Assets/../..` 穿越 | `Path.GetFullPath` 归一 + 穿越用例 | 已修复 |
| CR-7 | minor | 规则文案未直说"SO 过期" | 文案改"SO 过期或手改" + 夹具断言 | 已修复 |
| CR-8 | minor | `Serialize` 依赖调用方先反序列化排序 | `Serialize` 内自排序（幂等） | 已修复 |
| CR-9 | minor | `toolchain.json` 整文件重排、混入并行条目 | 重建为最小 diff（+4/−1），保留 `build.plan` | 已修复 |
| CR-10 | minor | 重复键诊断吞掉普通语法错误；`typography`/`elevation.color` 未加 Required；绝对路径异常而非 Fail | 记录为非阻塞后续项 | 遗留（非阻塞） |

## 复测记录

| 项 | 结果 |
|---|---|
| 复评子代理 | `04d9d824-cc13-4127-a09e-2b15f6c2035b`（只读，未改文件） |
| 三套新夹具 | `UiThemeTokensEditModeTests` **17/17** · `UiThemeAssetGeneratorEditModeTests` **8/8** · `UiStyleRetirementEditModeTests` **8/8** |
| 既有回归 | `UiStyleRuleEditModeTests` 4/4 · `UiValidationEditModeTests` 14/14（302/161/141 未漂）· `UiTextStyleEditModeTests` 8/8 |
| 真实资产一致性 | `ui.style.tokens` eval 复跑：`变更=否`、`sourceHash` 不变 |
| 全量门禁 | `wf verify` **PASS 40/40**（2026-10-24T16:52；含本批三套 + 并行线四条命令）；前几次在并行批次占用同一 Editor/工作区时出现陈旧结果/DLL 占用/临时 meta 写锁，均属环境竞争，非本批回归 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 子代理 `04d9d824-cc13-4127-a09e-2b15f6c2035b`（复评）；首轮 `dd97e8d7-cb02-4234-9af7-1f18a9629210`
- 评审时间: 2026-10-24T14:48+08:00（复评收口）
- 备注：复评时 `evidence/verify.json` 仍是加命令前的版本；本复评以 `commands` 登记为准判定 blocker 闭环，运行期证据由主代理随后补齐。
