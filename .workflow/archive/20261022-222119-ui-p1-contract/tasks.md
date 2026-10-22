# 实施任务清单：UI 系统 P1-0/P1-1：口径冻结 + 静态文案 + 页面契约内核

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | P1-0 口径冻结与文档回写 | - | 完成 | sourceHash/kind/模板边界/cacheStrategy/无时间戳口径写入工具链专项设计 §5 与协作规范 §3；修正 schema 样例 generatedAt 漂移；doc-lint 通过 | python scripts/gate/doc-lint.py | doc-lint OK（0 violation）；口径写入 UI编辑器工具链专项设计 §5.2.1（sourceHash 五域/模板边界/kind 推断/cacheStrategy 默认/确定性）与 UI美术协作与交接规范 §3；sche… |
| T2 | 静态文案 LTextKeyRef 与运行时绑定 | T1 | 完成 | 新增 LTextKeyRef、UiStaticTextBinder，并接入 UIForm 打开/语言变更/关闭；编辑器 Inspector 提供 tbltext key 下拉；无 prefab 改动 | unity-tests --filter UiStaticTextEditModeTests | UiStaticTextEditModeTests 6/6（TMP 优先/UGUI 回退/语言刷新/关闭退订）；UIForm 接缝（PrepareForShow 惰性建 binder / ApplyLocale 同驱 / EnterClo… |
| T3 | 绑定面遍历单源与 hash 回归 | T1 | 完成 | UiBindSurfaceWalker 成为 hash/契约共同遍历面；UiBindGenerationHash 改消费 walker 且既有 hash 用例保持绿 | unity-tests --filter UiBindingToolsEditModeTests | UiBindSurfaceWalker 成为 hash/契约共同遍历面；UiBindGenerationHash 改消费同一遍历；UiBindingToolsEditModeTests 12/12（既有 hash 值不变） |
| T4 | 页面契约模型、生成器与 diff | T2, T3 | 完成 | UiPageContract/UiPageMetadata/UiContractGenerator/UiContractDiff 落地；确定性 JSON、sourceHash、Clone_/嵌套根/模板边界过滤、负例不写盘 | unity-tests --filter UiContractGeneratorEditModeTests | UiContractGenerator 10/10（确定性/写盘读回/负例不写盘/模板边界过滤/输出路径守卫）；UiContractDiff 5/5；sourceHash 对页面元数据不敏感、对绑定面敏感 |
| T5 | 先行测试与负例覆盖 | T1 | 完成 | UiStaticTextEditModeTests/UiContractGeneratorEditModeTests/UiContractDiffEditModeTests 覆盖 AC2-AC4 主路径与失败路径，先红后绿 | unity-tests targeted | 3 个夹具 20 例（5+10+5）；先行红测 evidence/red.json（断言失败而非环境错误）；实现后于新程序集复跑全绿 |
| T6 | 门禁、评审与回写 | T4, T5 | 完成 | 定向 Unity 用例、L1、doc-lint、L2 meta 扫描通过；施工记录与文档回写完成 | L1+doc+l2-meta | L1 PASS 1819；doc-lint OK 0 violation；L2 meta 扫描 PASS（14170 meta）；定向夹具 6/6+10/10+5/5+12/12；设计文档接缝回写 + 进度卷 Docs/施工进度/UI系统… |

## 任务详情

### T1 · P1-0 口径冻结与文档回写

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/UI美术协作与交接规范.md, Docs/设计文档/客户端/UI系统/UI制作规范.md

### T2 · 静态文案 LTextKeyRef 与运行时绑定

- 文件: Assets/LiteClient/UI/Localization/LTextKeyRef.cs, Assets/LiteClient/UI/Localization/UiStaticTextBinder.cs, Assets/LiteClient/UI/UIForm.cs, Assets/LiteGame/Editor/UiTools/Text/LTextKeyRefInspector.cs

### T3 · 绑定面遍历单源与 hash 回归

- 文件: Assets/LiteGame/Editor/UiTools/Binding/UiBindSurfaceWalker.cs, Assets/LiteGame/Editor/UiTools/Binding/UiBindGenerationHash.cs

### T4 · 页面契约模型、生成器与 diff

- 文件: Assets/LiteGame/Editor/UiTools/Contract/*.cs

### T5 · 先行测试与负例覆盖

- 文件: Assets/Tests/EditMode/UiStaticTextEditModeTests.cs, Assets/Tests/EditMode/UiContractGeneratorEditModeTests.cs, Assets/Tests/EditMode/UiContractDiffEditModeTests.cs

### T6 · 门禁、评审与回写

- 文件: .workflow, Docs

