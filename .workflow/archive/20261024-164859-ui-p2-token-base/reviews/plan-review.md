# 方案评审（独立子代理）：UI 系统 P2-3a 令牌源与主题资产

> 运行 ui-p2-token-base · 创建于 2026-10-24T12:10:40+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- **评审对象**：`plan.md`（方案与验收标准 AC1–AC6）
- **代码/文档基线**：`main` @ `0574da90`；工作区仅 `.workflow/config.json`（本批前置：补 `review-plan`/`confirm-acceptance` 两阶段）与本运行目录。核对文件：`Assets/LiteGame/Editor/Style/{UiStyle,UiStyleTool,UiTextStyle,UiTextStyleTool}.cs`、`Assets/LiteGame/Editor/UiTools/Validation/{UiValidator,UiRuleIds,IUiRule,Rules/StyleColorDriftRule,Rules/StyleTextDriftRule}.cs`、`Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`、`Assets/Tests/EditMode/{UiStyleRule,UiTextStyle,UiValidation}EditModeTests.cs`、`Docs/设计文档/客户端/UI系统/UiStyleTokens.schema.json`、`Assets/BundleCollectorSetting.asset`。
- **评审范围**：方案的目标/范围/现状/取舍/设计/测试/验收/风险与回滚；不在范围：实现代码、资产变更、性能与视觉验收。
- **子代理标识**（四轮，互相独立，全部只读）：
  - 第 1 轮 `0e0585b0-9014-40ca-aa5f-51e06dc8d1ee`（超时被终止，无结论，不采信）
  - 第 2 轮 `65ec1c9b-af08-4ecc-b072-015e376a1818`（首轮有效评审：FAIL，1 blocker/2 major/2 minor）
  - 第 3 轮 `eca70f3e-51db-4019-a580-9a01a990f4cd`（复评 1：FAIL，0/2/2）
  - 第 4 轮 `33c0b1a7-523f-40ea-9761-8d7f410a34c6`（复评 2/收口：PASS，0 阻塞）
- **子代理约束**：只读评审，不修改生产代码、测试与门禁；每轮限步，不扩大抽样面。

## 方案检查

- **目标可观察、范围与非目标清晰**：通过。目标落在"令牌单源 + 生成物可复算 + 值表退役 + 基线不漂"，非目标明确列出 prefab/引用组件/预览/存量收敛五类不做事。
- **现状与根因有依据**：通过（第 2 轮逐项核对正确：12 色在 `UiStyle.cs:20-37`、5 档在 `UiTextStyle.cs:45-63`、阈值 `1e-4` 在 `UiStyleTool.cs:36`、302=161+141 在 `UiValidationEditModeTests.cs:220-222`、`Assets/GameData/Config` 为收集路径见 `BundleCollectorSetting.asset:48`、五组空集在 schema 下合法、12 个量化 hex 全部复算正确）。
- **候选方案取舍充分**：通过。A（JSON→SO→catalog）/B（C# 常量镜像）/C（TextJson 直读）三案对比，A 的理由回应了红线 5 与设计 §8.3。
- **设计覆盖接口/数据/流程/失败边界**：通过。含 JSON 模型、SO 字段、路径单源、catalog 取值路径、`With(catalog)` 注入缝、失败零写盘、路径守卫、手改覆盖与规则检出。
- **通用性与扩展点、非特判**：通过（最终结论）。ID 前缀/组归属表驱动；无 `if (id == ...)`；新增组/消费者/规则均走既有扩展点。
- **测试策略可先红后绿**：通过。三套 EditMode（令牌校验/生成器/退役）+ 5 套回归；骨架先落保证红为断言失败；`wf verify --red` 留证。
- **风险、监控与回滚可执行**：通过。风险 6 条含"字节漂移停工回方案""量化翻转停工提审"两个止损点；回滚面逐文件列明且无 BuildHash/表/场景影响。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 | `ui.style.tokens` 连跑两次：第二次 `changed=false`，`UiTheme.asset` 逐字节不变 | 是 | 唯一判据，拒绝"字段级相等"替代 |
| AC2 | 从 SO 读回主题名/版本/`sourceHash` + 七组计数 12/5/0/0/0/0/0，颜色 8bit 逐位一致 | 是 | 生成器与读回夹具覆盖 |
| AC3 | 改 JSON 不重生成 → `ui.style.token-drift` Error；跑 `ui.style.tokens` 后消失 | 是 | 命令链可复现 |
| AC4 | 既有 `UiValidationEditModeTests`（`:220/221/222` = 161/141/302、`:203` = Error 0）保持全绿；翻转即停工 | 是 | 用 HEAD 已入库的钉，不新增基线测试 |
| AC5 | `git status --porcelain Assets/` 仅白名单；`Assets/TempUiThemeTests/` 不存在；`ShareResource/` 零变更 | 是 | 白名单 + 临时清理双断言 |
| AC6 | `toolchain.py list` 含 `ui.style.tokens`；`docs --check`、`wf verify`、`doc-lint` 通过 | 是 | 工具索引重生成 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | blocker | 改写面不完整：`StyleColorDriftRule.cs:33`、`StyleTextDriftRule.cs:36-37`、`EditorToolHandlers.cs:181/188/197/203` 与两套夹具都依赖公开静态 API，原方案称"只改 4 文件" | A6 改为"公开重载保留 + 新增 `With(catalog)` 重载"，改写面清单逐文件列明；夹具"可能随动（仅期望值来源）"计入改写面 | 已消除（第 3/4 轮核验） |
| P-2 | major | `UiTextStyleDef` 字段去留与静态缓存风险：既说值由 catalog 物化又说签名不变，可能双源或触红线 6 | 明确 5 个档位静态字段删除、`All` 改只读属性（每次物化不缓存）、`FontSize`/`Bold` 为物化快照；构造器签名与可见性都不改（附 `new UiTextStyleDef(` 全仓 5 处证据） | 已消除 |
| P-3 | major | D1"对账判定不变"只是算术断言，阈值边界可能翻转 | 删除一次性 T0 探针路线，改为"既有 302/161/141 钉即判据"，翻转即停工提审；施工首步先跑钉留"改前"证据 | 已消除 |
| P-4 | major | T0 基线测试无法注入旧浮点 token（catalog 只有量化值），"同一口径"只能复制实现 | 整体删除 T0/新基线测试；AC4 锚 HEAD 既有钉（`:220-222`），不再要求新旧对比 | 已消除 |
| P-5 | major | `UiTextStyleDef` 构造器改 `internal` 属公开 API 删除，且 `default` 仍可构造，守卫无效 | 改为构造器可见性不变；"唯一构造路径"表述删除，改为"唯一生产读口是 `All`/`TryGet`" | 已消除 |
| P-6 | minor | AC1 与风险 1 自相矛盾（一个要求字节不变、一个预许改判据） | AC1 固定逐字节判据；风险 1 改为"停工回方案重审"，不预许替代判据 | 已消除 |
| P-7 | minor | AC5 只查 `Assets/ShareResource/`，覆盖不到新增 SO 与临时资产 | AC5 改 `git status --porcelain Assets/` 白名单 + 临时目录不存在断言 + ShareResource 零变更 | 已消除 |
| P-8 | minor | A1"无消费者不写值"与 typography 必填字段零消费者矛盾 | A1 改为"必填字段最小集"，`lineHeight`/`fontFamily` 明示为 schema 必填的预留字段 | 已消除 |
| P-9 | minor | `ui.style.token-drift` 红绿用例会改真实 JSON/SO，无注入缝 | 规则从 `UiValidationContext` 取 `ThemeJsonPath`/`ThemeAssetPath`；测试用 `Assets/TempUiThemeTests/` 副本并在 `finally` 删除（含 `.meta`） | 已消除 |
| P-10 | minor | "三套/四套"数字不一致、"静态读口改为实例传参"旧措辞、与 A6 冲突 | 统一为"三套夹具"；措辞改为"只读属性/调用点源兼容"；改写面与回滚清单补 `UiValidationContext.cs` | 已消除（第 4 轮仅余措辞债，已同批改） |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 子代理 `33c0b1a7-523f-40ea-9761-8d7f410a34c6`（第 4 轮收口）；前序 `65ec1c9b…`（第 2 轮）、`eca70f3e…`（第 3 轮）
- 评审时间: 2026-10-24T13:0x+08:00（第 4 轮收口）
- 备注：第 4 轮为只读核查，未执行 Unity 测试；AC4 的"当前全绿"由 P1 收口记录与 `UiValidationEditModeTests` 断言在 HEAD 存在佐证，施工首步会实跑一次作为"改前"证据。
