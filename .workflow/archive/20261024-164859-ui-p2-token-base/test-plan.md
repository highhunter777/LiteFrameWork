# 测试计划：UI 系统 P2-3a 令牌源与主题资产

> 运行 ui-p2-token-base · 创建于 2026-10-24T12:10:40+08:00

## 测试策略

- **测试层级**：全部为编辑器内 EditMode（`Assets/Tests/EditMode/`，Category=`Contract`）。本批只改编辑器工具与生成物，无运行期/协议面，故不新增 PlayMode；真实面回归由既有 `UiValidationEditModeTests`（302/161/141 钉）承担。
- **关键风险与覆盖重点**：
  1. **规范文本/指纹确定性**（令牌顺序、LF、末尾换行、未知字段拒绝）——`UiThemeTokensEditModeTests` 的解析/校验/规范文本三组。
  2. **生成幂等与失败零写盘**（重复生成 `changed=false` 且字节不变、非法源不建资产、路径守卫）——`UiThemeAssetGeneratorEditModeTests`。
  3. **值表退役后判定不漂**（12 色映射与量化等价、5 档字号字重、对账命中/不命中、规则红绿）——`UiStyleRetirementEditModeTests`。
  4. **生成物被手改/忘了重生成**——`ui.style.token-drift` 两例（一致＝无 Error；改 JSON 未重生成＝1 条 Error 且提示 `ui.style.tokens`）。
- **不在本批的测试面**：引用组件/样式解析（B2）、设备档/截图矩阵（B3）、存量 302 收敛（B4）、Player 性能。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| TT-1 七组齐全按组计数 | EditMode | T1/T2 · AC1 | 合法 JSON（1 色 + 1 文字 + 五空组） | 解析成功、各组计数正确、七组都在 |
| TT-2 合法源零问题 | EditMode | T2 · AC1 | 同上 | `Validate` 空 |
| TT-3 ID 越组 | EditMode | T2 · AC1 | color 组内放 `font.wrong` | 问题含该 ID |
| TT-4 ID 文法非法 | EditMode | T2 · AC1 | `color.Bad_Name` | 问题含该 ID |
| TT-5 颜色格式非法 | EditMode | T2 · AC1 | `#GGGGGG` | 问题含令牌 ID |
| TT-6 字号越界 | EditMode | T2 · AC1 | `fontSize=97` | 问题含 `fontSize` |
| TT-7 行高越界 | EditMode | T2 · AC1 | `lineHeight=3.0` | 问题含 `lineHeight` |
| TT-8 未知字重 | EditMode | T2 · AC1 | `weight=Heavy` | 问题含 `weight` |
| TT-9 缺组 | EditMode | T2 · AC1 | 删掉 `icon` 组 | 问题含 `icon` |
| TT-10 meta 缺字段 | EditMode | T2 · AC1 | `meta:{}` | 问题含 `meta` |
| TT-11 未知字段拒绝 | EditMode | T2 · AC1 | 颜色令牌多一个 `valueTypo` | 反序列化抛异常 |
| TT-12 规范文本排序/稳定 | EditMode | T2 · AC1 | 两色乱序写入 | 两次序列化逐字一致且按 ID 排序、LF 结尾 |
| TT-13 指纹 | EditMode | T2 · AC1 | 同源/改值各算一次 | 64 位小写 hex 且随内容变 |
| TG-1 首次生成 | EditMode | T3 · AC2 | 临时 JSON→临时 SO | 成功、计数 2/1、资产读回一致 |
| TG-2 二次生成幂等 | EditMode | T3 · AC1 | 连跑两次 | `changed=false` 且资产字节不变 |
| TG-3 非法源零写盘 | EditMode | T3 · AC1 | `#GGGGGGGG` | 失败且不建资产 |
| TG-4 路径守卫（非 Assets） | EditMode | T3 · AC1 | `Temp/UiTheme.asset` | 失败且错误含 `Assets` |
| TG-5 路径守卫（非 .asset） | EditMode | T3 · AC1 | `.../UiTheme.txt` | 失败且错误含 `.asset` |
| TG-6 源缺失 | EditMode | T3 · AC1 | 不存在的 JSON | 失败且错误带路径 |
| TG-7 手改后覆盖 | EditMode | T3 · AC2 | 改资产颜色再生成 | `changed=true`、值被覆盖回来 |
| TR-1 12 色映射与量化等价 | EditMode | T5 · AC4 | 金标准表（旧浮点 + 8bit） | ID/值一致、距离 ≤1e-4 |
| TR-2 无颜色字面量 | EditMode | T5 · AC4 | 扫 `UiStyle.cs` 源码 | 不含 `new Color(` / 旧值 |
| TR-3 五档字号字重 | EditMode | T5 · AC4 | `UiTextStyle.All` | 24/20/14/20/20、仅 Emphasis 加粗 |
| TR-4 无字号字面量 | EditMode | T5 · AC4 | 扫 `UiTextStyle.cs` | 不含 `24f`/`14f` |
| TR-5 缺资产显式失败 | EditMode | T5 · AC4 | `Load(不存在)` | 抛 `FileNotFoundException` |
| TR-6 对账判定不变 | EditMode | T5 · AC4 | 合成 prefab（命中 + 不命中） | 只报不命中 1 条 |
| TR-7 漂移规则红绿 | EditMode | T4/T5 · AC3 | 生成后改 JSON 副本 | 一致＝0 条；改动＝1 条 Error 且 FixHint 含 `ui.style.tokens`；重生成后＝0 条 |

## 预期失败证据

- **命令**：`wf verify --red --run ui-p2-token-base`（三条：`unity-tests --mode editor --filter UiThemeTokensEditModeTests` / `UiThemeAssetGeneratorEditModeTests` / `UiStyleRetirementEditModeTests`）
- **观察到的失败**（2026-10-24，`evidence/red.json`）：三套分别 **12/13、6/7、6/7 失败**，全部为断言失败（`Expected: True / But was: False`、`Expected less than -1 / But was -1`、`Expected an exception to be thrown`），无编译/环境错误（前一版编译错误已修：测试程序集不引 Newtonsoft，改用 `Throws.Exception` 约束）。
- **为什么这是正确的失败**：骨架阶段新类型只保留签名与空实现（校验返回空问题表、序列化返回空串、生成器返回"未实现"、catalog 返回空目录、规则返回空集），失败点正是本批要实现的行为；没有一条失败来自"文件不存在/模块未加载"。

## 运行方式

- **仅运行新增测试**：
  - `python scripts/gate/unity-tests.py --mode editor --filter UiThemeTokensEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiThemeAssetGeneratorEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiStyleRetirementEditModeTests --timeout 600`
- **回归（本批必跑）**：`UiStyleRuleEditModeTests`、`UiTextStyleEditModeTests`、`UiValidationEditModeTests`（302/161/141 钉）、`EditorToolCatalogEditModeTests`、`UiToolchainWiringEditModeTests`。
- **全量门禁**：`wf verify --run ui-p2-token-base`（L1 / doc / l0 / toolchain-check / docs-check / l2-meta + 三套新夹具）。

## 豁免与说明

- 无豁免。三套用例都能在实现前写出并观察到断言失败；生成器/规则的"临时资产 + 临时 JSON"路径均落在 `Assets/Temp*` 临时目录，`TearDown` 删除（含 `.meta`），不触碰真实生成物。
