# 测试计划：UI 系统 P2-3 B4：存量样式收敛与收口

> 运行 ui-p2-style-closure · 创建于 2026-10-26

## 测试策略

- 层级：EditMode 为主（令牌/解析/组件/迁移/规则/夹具），PlayMode 覆盖页面装配与 HUD 注入；真实面由 `UiValidationEditModeTests` 与 `UiStyleTool.ReportDrift` 钉基线；L1/doc/toolchain 回归。
- 关键风险：嵌套 prefab 继承与重复 override、真实面 Warning 基线被掩盖、同值令牌 tie-break、8bit 量化误判、HUD 注入无消费者、config 未登记导致假绿。
- 真实面数据：309 Graphic / 157 未命中 / 44 色值；141 文本 / 37 精确匹配（14 嵌套、4 源 prefab 7 源节点）；46 Selectable（33 直挂 + 13 继承、5 源 prefab 7 源 Selectable）；4 个列表文本实例有意的 Left 对齐覆盖。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UT-1 令牌源 69/44 逐项 | EditMode | T1/AC1 | 读真实 JSON，按附录 A 44 项比对；SO 读回 69 | 69 唯一 ID；44 值逐项一致；5 Selectable 值一致 |
| UT-2 生成幂等 | EditMode | T1/AC1 | `ui.style.tokens` 连跑两次 | 第二次 changed=False，资产字节不变 |
| UT-3 Resolve | EditMode | T3/AC5 | 已知/空/未知/null resolver | 已知取现值，其余 fallback |
| UT-4 Selectable 组件 | EditMode | T2/AC3 | Button + 5 令牌；multiplier/fade；非 ColorTint；未知令牌；null | 5 色写入、保留 multiplier/fade、仅 ColorTint、未知保持现值、null 不抛 |
| UT-5 Selectable 迁移 | EditMode | T2/AC3 | 直挂 + 嵌套源模板；二次跑 | Added 只直挂、嵌套继承、二次 0、不改 ColorBlock |
| UT-6 Binder | EditMode | T3/AC3/AC6 | refs/IThemeBound/writer 祖先/SelectableTheme 节点/未知令牌/真 HUD | refs 应用、writer 祖先跳过、SelectableTheme 基色 ref 可用、未知告警一次、HUD Button_Settings 变色且 _Slider 不抛 |
| UT-7 规则 | EditMode | T5/AC7 | 正例/缺组件/未知令牌/漂移/非 ColorTint/writer 祖先/SelectableTheme 不误报 | 级别与计数符合判据 |
| UT-8 文本迁移 | EditMode | T4/AC4 | 直挂 + 嵌套 + UGUI + 36px + 实例覆盖 + 二次 | 23 组件/37 覆盖/不匹配留债/实例覆盖计未标记/幂等 |
| UT-9 基线 | EditMode | T7/AC8 | 真实面 `UiValidator.Run` | Error 0；颜色 0；文本 104（104 未标记 + 0 Mismatch）；selectable 0 |
| IT-1 页面装配 | PlayMode | T7/AC3 | Page_Login 打开；修改 selectable token；null 资产开/关/重开 | ColorBlock 按令牌变化；null 只告警一次；不阻断 |
| IT-2 HUD prefab | EditMode | T6/AC3 | 真 Hud_Battle prefab + binder | Button_Settings 变色；_Slider targetGraphic=null 不抛 |
| IT-3 门禁 | 全量 | T7/AC8 | `wf verify` | 57 条命令全绿；新套件在输出中 |
| IT-4 视觉零变化 | 静态 | T7/AC3 | `git diff -- Assets/ShareResource/UI` | 仅组件/字段新增，无颜色/ColorBlock/文本属性改动 |

## 预期失败证据

- 命令：`python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p2-style-closure verify --red`
- 观察到的失败（2026-10-26，`evidence/red.json`）：
  - `ui-selectable-theme-red` exit=1：5 色未写入（骨架/备份恢复前的实现缺失）。
  - `ui-theme-binder-red` exit=1：真 HUD `Button_Settings` 未挂/未注入（迁移/实现缺失期）。
  - `build-player-platform-red` exit=1：**并行线** `scripts/tests/test_build_player.py` 既有失败，不属 B4；如实保留。
  - 其余 red 命令 exit=0（令牌/文本/规则在红测时已先行落地或实现完成）。
- 为什么这是正确的失败：失败为断言级（颜色值/组件为 null/ColorBlock 未变），不是编译/环境错误；与 AC3/AC6 的行为缺口对应。`UiSelectableTheme` 的 5 色用例在实现前实测 2/4 失败（Expected RGBA(0.1,0.2,0.3) But was (0,0,0)）；Binder 真 HUD 用例在迁移前实测 1/6 失败（组件为 null）。
- 限制（如实）：T1 令牌与 T4 文本迁移在红测命令执行前已先行落地（生成物/迁移依赖），其失败未在 `red.json` 中再现；由实现前骨架断言审读、迁移报告与真实面盘点补证，不声称这些套件曾整体红。

## 运行方式

- 全量：`python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p2-style-closure verify`（2026-10-26T20:04 57/57 PASS；并行线两个 test_player_smoke/test_build_player 文件临时 stash 隔离后恢复，见 build-log 口径）
- 仅新增/定向：
  - `python scripts/gate/unity-tests.py --mode editor --filter UiSelectableThemeEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiSelectableThemeApplierEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiThemeBinderEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiStyleSelectableRuleEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiTextStyleEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiValidationEditModeTests --timeout 600`
  - `python scripts/gate/unity-tests.py --mode playmode --filter UiThemeApplyPlayModeTests --timeout 600`

## 豁免与说明

- `ui.style.colors-refresh` 不作为 B4 验证步骤：补 44 令牌后其裸值段会把 154 处浮点收敛到 8bit 令牌值（不可见，但“逐位不变”不成立）；B4 零可见变化按 `Color32` 8bit 判据 + 迁移只加组件不写颜色。
- 4 个列表文本实例的 `Left` 对齐是合法实例覆盖：`UiTextStyleRef.UseInstanceOverrides=true`，工具不应用/不判 Mismatch，但计入未标记债（104 不变）。
- HUD `_Slider` `targetGraphic=null`：组件继承但无 tint 视觉，断言只要求不抛；有效可观察目标是 `Button_Settings`。
