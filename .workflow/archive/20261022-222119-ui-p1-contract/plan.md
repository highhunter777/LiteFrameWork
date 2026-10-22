# 方案：UI 系统 P1-0/P1-1：口径冻结 + 静态文案 + 页面契约内核

> 运行 ui-p1-contract · 创建于 2026-10-22T21:01:41+08:00

## 背景与目标

- 现状：UI 系统总设计把 P1 定为「契约与校验」；工具链专项设计 T1 要求 `WidgetSpec`、清单、Page Contract、schema、diff。代码面已有校验核心、命名词表、绑定 hash、`UiPageScaffold` 与 `UiTextStyleRef`，但 `LTextKeyRef`、`UiPageContract` 生成器/diff、`sourceHash` 口径仍未落地。
- 要解决的问题：美术无法从 prefab 生成只读契约；工程没有可消费的 `controls[]`；静态文案没有统一的 key 标记；契约与绑定 hash 若各写一份遍历会长期漂移。
- 目标（可观察）：在不动任何 prefab/场景的前提下，新增可编译的 `LTextKeyRef` 与静态文案运行时绑定；新增确定性的页面契约模型、生成器与 diff；把绑定面遍历抽成单源；P1-0 的生成口径写回设计文档；定向 Unity 测试与项目门禁通过。

## 需求澄清与假设

- 需要确认的问题：无（用户已明确执行 P1-0/P1-1）。
- 已确认答复：按上一轮列出的 P1 批次方案执行，先做 P1-0/P1-1。
- 采用假设：
  - `WidgetSpec`、`UiPageManifest`、契约校验规则与 CI 接线属于 P1-2～P1-5，本批不实现，只保留扩展点。
  - 本批不修改 prefab/场景，因此 `LTextKeyRef` 不批量挂到现有页面；P1-6 试点再挂。
  - `tbuiform` 复用 `UiPageScaffold.ListPages()` 的 Python 助手读取链，不新建表访问实现。
- 假设影响与风险：现有页面没有 `LTextKeyRef`，契约 `text[]` 在试点前为空是正常基线；`cacheStrategy` 在表列落地前默认 `Lru`，需在文档中显式登记。

## 范围与非目标

- 范围内：
  - P1-0：冻结 `sourceHash` 输入、`controls[].kind` 推断、模板边界、`cacheStrategy` 默认、无时间戳；修正 schema 与设计样例的 `generatedAt` 漂移；回写工具链专项设计 §5 与协作规范 §3。
  - P1-1：`LTextKeyRef` 运行时标记 + 编辑器 key 下拉 + `UiStaticTextBinder` + `UIForm` 接缝；`UiBindSurfaceWalker` 单源遍历；`UiPageContract` 模型/JSON 读写/生成器/diff；绑定 hash 改为消费同一遍历器。
- 非目标：WidgetSpec/UiWidgetManifest（P1-2）、UiPageManifest（P1-3）、契约校验规则与 `UiRuleExceptions`（P1-4）、Hub/CLI/CI 接线（P1-5）、试点页面挂 key（P1-6）、现有页面迁移（P4）。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteClient/UI/UIForm.cs`（语言注入时机与展示生命周期）、`Assets/LiteClient/UI/Bind/UIBindIndex.cs`（既有 LText 链）、`Assets/LiteClient/UI/Style/UiTextStyleRef.cs`（样式声明来源）。
  - `Assets/LiteGame/Editor/UiTools/Binding/UiBindGenerationHash.cs`（现有绑定面遍历与 hash）、`UiBindingRename.cs`（路径工具）。
  - `Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（tbuiform 读取）。
  - `Docs/设计文档/客户端/UI系统/UiPageContract.schema.json`（v2.0 schema）、`UI编辑器工具链专项设计.md` §5、`UI美术协作与交接规范.md` §3。
- 约束：
  - 保留基座公共契约只允许增量扩展；`UIForm` 新增静态文案接缝需在设计中登记。
  - 运行时不得构建视觉；`LTextKeyRef` 只做文本写入，不新建/补建 Graphic/Canvas。
  - 契约生成必须确定性：无时间戳、稳定排序、重复生成零 diff。
  - 禁止第二套绑定面遍历/类型解析；绑定 hash 的既有值不能因重构改变。
  - 资源写入走现有编辑器文件 API；本批不写 prefab。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | `LTextKeyRef` 只存 Key；`UIForm` 持有 `UiStaticTextBinder`，页面打开/语言变更时扫描并写入；绑定面遍历抽 `UiBindSurfaceWalker` | 页面级所有权清晰；无逻辑页也能刷新；遍历/hash/契约单源 | 需改保留基座 `UIForm`（增量接缝）；binder 需处理关闭退订 | 推荐 |
| B | 把静态文案绑进 `UIBindIndex`，只覆盖有逻辑页 | 改动集中在索引 | 无逻辑页失效；索引语义从“名字→控件”扩成“节点扫描”，职责变宽 | 不采用 |
| C | 契约生成器自己写遍历，不抽公共 walker | 初期代码少 | 与绑定 hash/BindSearch 三套语义，P1 后必然漂移 | 不采用 |

推荐方案与理由：A。它把“页面打开时写静态文案”的所有权放在 `UIForm`（页面实例），与现有 `LocaleProvider` 注入点重合；遍历器作为编辑器侧单源，同时服务 hash 与契约，避免后续三处口径分叉。

## 设计要点

- 接口/数据：
  - `LTextKeyRef`：`public string Key`，`[DisallowMultipleComponent]`，不加参数/复数/富文本替换。
  - `UiStaticTextBinder`：构造时扫描根下 `LTextKeyRef`；`Bind(ILocalizationService)` 订阅并立即应用；`Unbind()` 退订；`Apply()` 用 `TMP_Text` 优先、UGUI `Text` 回退，缺文本组件只告警一次。
  - `UIForm`：`PrepareForShow` 惰性创建 binder；`ApplyLocale` 一次取 locale 后同时驱动 `ILocaleBound` 与 binder；`EnterClosing` 退订；`DestroyInstance` 释放。
  - `UiBindSurfaceWalker`：深度优先输出 `UiSurfaceNode`（Path/IsRoot/InCloneScope/InsideNestedBindRoot/InsideTemplate/TemplateRoot/Bind/BindRoot）；模板实例根只记录一次，内部件标记 `InsideTemplate`；嵌套 `BindRoot` 子树标记 `InsideNestedBindRoot`。
  - `UiPageContract`：Newtonsoft.Json + `JsonProperty(Order)`；可选字段 null 时省略，`luaPath` 允许 null；JSON 换行归一为 LF、UTF-8 无 BOM。
  - `sourceHash`：域分隔规范行（control/text/style/template），SHA-256；不包含页面元数据、工具版本、时间戳。
  - `UiContractGenerator`：纯生成 `Generate(prefabPath, UiPageMetadata)` + 写盘 `GenerateToDisk`；`GenerateRegistered` 通过 `UiPageScaffold.ListPages()` 取行。
  - `UiContractDiff`：按 API 键比对 controls/text/templates，输出 Added/Removed/Renamed/Changed/Visual/SourceOnly 与 `IsBreaking`。
- 关键流程：读 prefab contents → walker 枚举 → 过滤 Clone_/嵌套根/模板内部 → 收集 controls/text/styles/templates → 计算 sourceHash → 序列化/写盘；diff 分别加载磁盘与重生成模型。
- 失败与边界处理：
  - 重复 BindName、类型不可解析、静态 key 为空、节点无文本组件、prefab 根名与文件名不一致 → 生成失败并列出错误，不写盘。
  - tbuiform 行缺失/读取失败 → 显式失败，不伪造 metadata。
  - `LTextKeyRef` 缺文本组件 → 运行时不崩，告警一次；编辑器规则留 P1-4。
  - 语言服务未注入 → 静态文案保持 prefab 作者文本（工具/测试装配），不抛。

## 通用性与扩展性

- 根因/共性问题：契约、绑定 hash、代码生成都需要“哪些节点属于本页/本模板”的同一答案；静态文案需要页面级生命周期，而不是逻辑脚本级。
- 复用与扩展点：`UiBindSurfaceWalker` 是唯一遍历面；`UiPageMetadata` 是表格投影接缝（P1-3 可由 manifest 填充 Owner/DesignRef）；`UiContractDiff` 的变更类型可被 P1-4 规则与 P1-5 CI 直接消费。
- 避免硬编码/特判的约束：模板边界用 prefab 实例根判定而非名字硬编码；类型用 `BindNode.ResolveTarget()` 的实际类型；`kind` 用组件类型集合推断而非页面特判；`cacheStrategy` 用单一默认常量并在文档登记。

## 测试策略

- 测试层级与关键用例：
  - EditMode `UiStaticTextEditModeTests`：按 key 写入、语言变更刷新、Unbind 后不刷新、无文本组件不抛、UIService 集成打开/关闭。
  - EditMode `UiContractGeneratorEditModeTests`：page/controls/text/styles/templates/sourceHash、Clone_ 排除、嵌套 BindRoot 排除、模板内部不展开、确定性、重复 BindName/非法类型/空 key 负例、写盘读回。
  - EditMode `UiContractDiffEditModeTests`：新增/删除/改名/类型变化/key 变化/模板 hash 变化/source-only。
  - 回归：`UiBindingToolsEditModeTests` 的 hash 用例必须保持绿（遍历重构不改变既有 hash 值）。
- 如何先失败后通过：先加最小类型骨架（抛 `NotImplementedException`/返回空）让测试可编译，运行定向用例取得断言/异常失败证据；再实现行为并复跑至绿。

## 验收标准

- [ ] AC1：P1-0 口径写入设计文档，schema 样例的 `generatedAt` 漂移修正，文档门禁通过。
- [ ] AC2：`LTextKeyRef` + 编辑器 key 下拉 + `UiStaticTextBinder` 落地；页面打开按 key 写入，语言变更刷新，关闭后不再刷新。
- [ ] AC3：`UiBindSurfaceWalker` 成为绑定 hash 与契约生成的共同遍历面；既有绑定 hash 用例保持绿。
- [ ] AC4：契约生成器对真实 prefab 产出符合 schema 的确定性 JSON；`sourceHash` 稳定且对绑定/文案/模板变化敏感；负例不写盘。
- [ ] AC5：定向 EditMode 用例全绿；L1、doc-lint、L2 meta 扫描通过；无 prefab/场景改动。

## 风险与回滚

- 风险：
  - `UIForm` 是保留基座，新增 binder 可能影响页面关闭/复用生命周期；用“只对含 LTextKeyRef 的页面生效”和关闭退订降低影响。
  - 遍历重构可能改变既有绑定 hash 值；以 `UiBindingToolsEditModeTests` 回归钉住，必要时回滚 walker 改动。
  - Prefab 实例根判定在 prefab contents 中的 API 行为不一致；用合成嵌套 prefab 用例验证，失败则退化为“按 `Tmp_` 根名 + 实例根”双重判据。
- 监控/发现：定向 Unity 用例、L1、doc-lint；生成器错误日志带 `[ui.contract]` 前缀。
- 回滚：本批不写 prefab/场景/表；回滚代码与文档即可恢复，无资产迁移成本。
