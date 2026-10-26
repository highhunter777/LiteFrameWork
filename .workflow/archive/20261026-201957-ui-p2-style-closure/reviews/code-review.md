# 代码评审（独立子代理）：UI 系统 P2-3 B4：存量样式收敛与收口

> 运行 ui-p2-style-closure · 创建于 2026-10-26T16:58:34+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 代码基线：HEAD c8adb9df；工作区并行线改动（GM/DevTool/Boot/ClientHost 等）不属于本批。
- 本批范围：令牌 20→69；UiSelectableTheme/UiSelectableThemeApplier；UiThemeBinder/IThemeGraphicWriter；UiStyleResolver.Resolve；UIForm/UIService；三 widget；UiStyleTool/UiStyleRefApplier/UiTextStyleTool（含 UseInstanceOverrides）；StyleSelectableRule/StyleRefRule/StyleTextDriftRule/UiValidator/UiRuleIds；HUD 注入（ProcedureBattle/BattleViewRig/ContainerModule）；4 套新增 + 8 套扩展测试；24 个 prefab 仅新增组件/字段。
- 子代理标识：392785b6-12fc-469b-afe8-11b7b8059921（首轮 FAIL → 修复后复评 PASS）
- 子代理约束：只读评审，不修改生产代码、测试与门禁，不跑 Unity/门禁。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 验证有效性: PASS（全量 wf verify 57/57，见 evidence/verify.json；定向 4 套新增 + 8 套扩展全绿）

## 变更范围

- 对照 AC1–AC9 与本批 plan：令牌补集、Selectable 接缝、文字档迁移、应用核心、HUD 注入、widget 收口、规则、夹具、文档。
- 新增 5 个生产文件（UiSelectableTheme/UiThemeBinder/IThemeGraphicWriter/UiSelectableThemeApplier/StyleSelectableRule）与 4 套测试；修改 13 个生产/测试文件、24 个 prefab（仅新增组件/字段）、2 个生成/配置资产、9 份文档。无范围外生产改动；ProcedureBattle 的并行线场景还原改动不属于本批。

## 正确性与边界

- 主路径：令牌 JSON→SO→Resolver→Binder→IThemeBound/UiStyleRef→widget；HUD 经 UIService.ThemeAssetProvider→ProcedureBattle→BattleViewRig 单点注入。
- 边界：null/未注入资产保持烘焙值并 Warning 一次；未知令牌不写、规则 Error、Binder Warning 一次；非 ColorTint 跳过；targetGraphic=null 不抛；嵌套 prefab 由源模板继承、迁移与规则同判据；直挂 UseInstanceOverrides 报 OverrideMisuse；Color32 8bit 判据；克隆体/主题切换/colors-refresh 明确留给后续批。
- 无生产代码新增 public static 可变状态；无硬编码颜色新增（迁移工具只写令牌值）。

## 测试充分性

- 行为级断言为主：五色/multiplier/fade、ColorTint、未知/null、幂等、只增组件不改属性、writer 认领目标/子标签不越权、规则正负例、实例覆盖合法/滥用、真实面 104/0/0/46、PlayMode 页面/HUD。
- 覆盖错误路径：无 Selectable、未知令牌、缺组件、ColorBlock 漂移、非 ColorTint、直挂滥用、嵌套容器不重复报、null provider。未覆盖：HUD provider 抛异常路径（SafeCall 已静态保证）；嵌套实例 ColorBlock override（B4 接受边界）。
- 真实面基线可判：UiValidationEditModeTests 14/14、StyleInventory 309/309、104 未标记/0 Mismatch/46 Selectable；全量 verify 57/57。

## 通用性与扩展性

- 通用性：令牌数据驱动；Binder 为页面/HUD/克隆体唯一应用入口；IThemeGraphicWriter.Owns(Graphic) 把写权从子树收窄到具体目标；规则/迁移/应用共用 IsGraphicOwnedByWidget；文本角色单源 UiTextStyle。
- 扩展性：新增颜色只改 JSON；新增 writer 只实现 Owns；新增文本角色只改 UiTextStyle 表；Selectable 令牌按前缀扩展；未引入无消费者 API；HUD/页面共用 Binder。

## 安全与性能

- 无敏感信息/密钥/网络输入；无反射取私有服务。
- HUD 每局一次遍历/告警；Binder 单次 GetComponentsInChildren，未知令牌按 token 去重；无每帧分配/热路径 Warning；无新增静态可变状态。
- 规则/迁移仅编辑器；运行时组件只读令牌并写 Graphic/ColorBlock。

## 发现的问题与处理

- 首轮 3 blocker（无 Selectable 夹具/裸值段判据/规则与迁移件不互逆）已修复并复评 PASS。
- 复评中项（OverrideMisuse 语义）已新增独立 kind；低项（Owns 取 Image、HUD SafeCall/孤儿、重复 using/注释/JSON 描述）已修；扫描面重复/默认面、widget 默认透明黑守卫列入后续建议。
- 无未处理阻塞。

## 复测记录

- 2026-10-26T20:15：wf verify 57/57 PASS（evidence/verify.json；并行线两个 test 文件临时 stash 隔离后恢复）。
- 定向：Tokens 18/18、State 3/3、Resolver 11/11、SelectableTheme 4/4、Applier 3/3、Binder 7/7、SelectableRule 7/7、TextStyle 13/13、StyleRef 9/9、StyleRefRule 9/9、WidgetTheme 7/7、Validation 14/14、ThemeApply 4/4。
- doc-lint 0/0；真实面 309/309 色命中、104 文本、46/46 Selectable、0 Mismatch。

## 首轮阻塞与修复（复核）

| ID | 首轮问题 | 修复 | 复核 |
|---|---|---|---|
| B1 | ui.style.selectable「无 Selectable」分支无夹具、不可达 | 移除 UiSelectableTheme 的 [RequireComponent(typeof(Selectable))]（改由规则显式报 Error）；新增 无Selectable_Error（DestroyImmediate button）；规则 7/7 | PASS |
| B2 | 裸值收敛段仍按同节点 IThemeBound 跳过，33 个 UiSelectableTheme 节点基色不再收敛；文档/代码不符 | 改用 UiThemeBinder.IsGraphicOwnedByWidget(graphic)；新增 ApplyColorsWith_UiSelectableTheme节点基色仍收敛；UiStyleRef 9/9 | PASS |
| B3 | StyleSelectableRule 遍历嵌套实例、Applier 跳过，重复报/修复提示无效 | 规则跳过 IsPartOfPrefabInstance(selectable)（与 Applier 同判据）；新增 嵌套实例_由源模板检查_容器不重复报；规则 7/7 | PASS |

## 复评后的中/低问题与处理

| 严重度 | 问题 | 处理 |
|---|---|---|
| 中 | 直挂 UseInstanceOverrides 复用 BrokenRef，Error 文案/FixHint 语义错位 | 新增 UiTextIssueKind.OverrideMisuse；StyleTextDriftRule 专用 kind/hint「仅限嵌套 prefab 实例」；TextStyle 13/13 |
| 中 | 页面对嵌套实例 ColorBlock 的 override 无人检查 | 接受为 B4 边界：源模板检查 + 真实面 0 override；列入后续扫描面收敛批（不隐藏当前 0 漂移事实） |
| 低 | Owns 取 GetComponent<Graphic>，写入端取 Image | StateButton/TabGroup 统一取 Image；StarRating 用 Image[]；Binder 7/7 |
| 低 | HUD 注入未走 SafeCall；catch 后实例孤儿 | ApplyHudTheme 包 SafeCall；catch 内 Destroy(_hudInstance)；PlayMode 4/4 |
| 低 | 重复 using/双 summary/死 fallback/UIForm 注释/JSON meta 描述 | 已清理；UiTheme.asset 重新生成 sourceHash=4481b757… |
| 低 | PrefabPaths/目录清单多处重复；colors/text 默认扫描面不含 Hud | 迁移件已加三面默认（Widgets/Screens/Hud）；其余抽 shared helper 列入后续建议（不影响本批判据） |
| 低 | 三 widget 默认透明黑无作者面守卫 | A4 已知边界：生产 prefab 序列化值为权威，无运行时 AddComponent 点；列入后续建议 |

## 测试与验证

- 行为级覆盖：5 色/multiplier/fade 保留、仅 ColorTint、未知令牌保持现值、null 不抛；直挂/嵌套迁移、幂等、只增组件不改颜色；refs/writer 认领目标/子标签不越权/未知令牌一次；selectable 规则正负例；文本 23/37/14/104/0 Mismatch/实例覆盖/滥用 Error；真实面 104/0/0/46；PlayMode 页面 selectable 变色 + null 只告警一次。
- 全量：wf verify 57/57 PASS（2026-10-26T20:15，evidence/verify.json）。口径：并行线 scripts/tests/test_build_player.py/test_player_smoke.py 临时 stash 隔离后恢复（其 Android 契约未完成，非 B4），B4 代码/文档未变；并行线合入前需其自绿。
- 视觉：24 prefab 773 insertions / 0 deletions，无颜色/ColorBlock/文本属性改动。

## 复评记录

- 首轮（2026-10-26）：392785b6… 返回 FAIL（3 blocker，通用性 FAIL）。主代理修复后请求复评。
- 复评（2026-10-26）：392785b6… 返回 PASS（0 blocker，通用性/扩展性 PASS），条件为全量 verify 复核真实面 104/0/0；该条件已由 2026-10-26T20:15 的 wf verify 57/57 PASS 满足。
- 复评后仅修中/低项（OverrideMisuse/Owns），未再开子代理；不改变 PASS 结论。

## 评审结论补充

- 范围提示：ProcedureBattle.cs 混入并行线「活动场景还原」改动（_sceneBeforeBattle/SetActiveScene），B4 在该文件只加 themeProvider 形参与转发；批次边界靠归属判定。
- 未决：并行线测试未修；HUD 克隆体/主题切换、104 文本角色扩展、扫描面收敛见 build-log「后续建议」。
