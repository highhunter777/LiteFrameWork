# 代码评审：UI 系统 P1-0/P1-1：口径冻结 + 静态文案 + 页面契约内核

> 运行 ui-p1-contract · 创建于 2026-10-22T21:01:41+08:00

## 变更范围

- 对照任务与方案：T1（P1-0 口径冻结与文档回写）→ `plan.md` AC1；T2（静态文案）→ AC2；T3（遍历单源与 hash 回归）→ AC3；T4（契约模型/生成器/diff）→ AC4；T5（先行测试与负例）→ AC2–AC4；T6（门禁/评审/回写）→ AC5。无范围外改动。
- 变更文件/模块：
  - 运行时：`Assets/LiteClient/UI/Localization/LTextKeyRef.cs`、`UiStaticTextBinder.cs`（新增）、`Assets/LiteClient/UI/UIForm.cs`（+18/-1，静态文案接缝）。
  - 编辑器：`Assets/LiteGame/Editor/UiTools/Binding/UiBindSurfaceWalker.cs`（新增）、`UiBindGenerationHash.cs`（改消费 walker）、`Text/UiLTextTable.cs`、`Text/LTextKeyRefInspector.cs`（新增）、`Contract/{UiPageContract,UiPageMetadata,UiContractJson,UiContractGenerator,UiContractDiff}.cs`（新增）。
  - 测试：`Assets/Tests/EditMode/{UiStaticText,UiContractGenerator,UiContractDiff}EditModeTests.cs`（新增，21 例）。
  - 工装：`scripts/gate/unity-tests.py`（超时透传 + 编译新鲜度/失败守卫）。
  - 文档：`Docs/设计文档/客户端/UI系统/UI系统总设计.md` §4.1/§4.2；`Docs/施工进度/UI系统线.md`（新卷第 1 章）；`Docs/施工进度/README.md`（索引行）。

## 正确性与边界

- 主路径：
  - 静态文案：`UIForm.PrepareForShow` 建 binder → `ApplyLocale` 绑语言并立即写入 → 语言事件刷新 → `EnterClosing` 退订 → `DestroyInstance` 释放；无 `LTextKeyRef` 的页面零订阅。
  - 契约：`LoadPrefabContents` → `UiBindSurfaceWalker` 一次遍历 → 过滤（`Clone_`/嵌套 `BindRoot`/模板内部件）→ controls/text/styles/templates → 稳定排序 → `sourceHash` → JSON/写盘。
- 边界与错误路径（均有用例或显式分支）：元数据为空/`formId` 非正/`layer` 越界/`cacheStrategy` 非法、prefab 缺失、根名与文件名不一致、根名/路径不合文法、GUID 取不到、重复 `BindName`、类型不可解析、空 key、缺 TMP/Text 组件、非 `Tmp_*` 嵌套 prefab（Warning 不透明跳过）、模板缺失或无绑定面（`bindingHash` 省略）、输出路径不在 `Assets/` 下（不写盘）、语言服务未注入（保持作者文本、不抛）。
- 回归风险：绑定 hash 由 `UiBindingToolsEditModeTests` 12/12 钉住（既有值不变）；`UIForm` 是保留基座，改动只在语言注入点内增量、关闭即退订，`UIService`/`UIService` 生命周期未变；首开失败回滚路径未受影响（`ApplyLocale` 仅在 OnInit 成功后调用）。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：是。静态文案 6 例覆盖 TMP 优先、UGUI 回退、语言刷新、解绑后不刷新、页面打开/关闭生命周期、坏节点与空 key；契约 10 例覆盖四段提取、`Clone_`/嵌套根/模板内部过滤、确定性（逐字节相等、无 `generatedAt`）、`sourceHash` 对元数据不敏感/对绑定面敏感、写盘读回、负例不写盘、模板边界、输出路径守卫；diff 5 例覆盖 Added/Renamed/Changed/Visual/SourceOnly 与破坏性判定。
- 是否存在迎合实现的测试：否。断言基于公开行为（文本内容、契约字段、JSON 字节、diff 结果），未断言实现细节；无删除/弱化既有用例。`red.json` 证明先红（断言失败，非环境错误）。

## 通用性与扩展性

- 根因与复用边界：遍历单源解决「哪些节点属于本页/本模板」的多处口径分叉；`UiPageMetadata` 是 tbuiform → 契约的投影接缝；`UiContractDiff` 的变更类型可被 P1-4 规则与 P1-5 CI 直接消费。
- 扩展点与接入方式：新页面/新模板只需经既有标记（`BindNode`/`LTextKeyRef`/`UiTextStyleRef`/`Tmp_*` 实例）进入同一遍历；新增契约段/字段走 `UiPageContract` + schema 次版本追加；`cacheStrategy` 表列落地只改 `UiPageMetadata` 投影。
- 硬编码、特判、魔法值：`cacheStrategy` 默认值与词表改由 `UICacheStrategy` 单源（`Enum.GetNames` + `DefaultCacheStrategy`）；页面名/路径文法、`kind` 推断组件族、`sourceHash` 域分隔符为冻结口径（写入设计 §5.2.1），非临时特判；无按页面名的分支。
- 重复逻辑与过度抽象：契约收集按 control/text/style/template 拆小函数，无重复遍历；`UiContractJson`/`UiContractStore` 只做序列化与落盘，未过度抽象；`UiLTextTable` 与运行时 `ConfigService` 共用 `TableLayout` 口径（未自造第二套表路径）。
- 相关场景复用与回归影响：`UiBindGenerationHash` 复用 walker 后既有 hash 不变；`UIForm` 接缝对所有页面生效但只在有标记时工作；`LTextKeyRefInspector` 复用 `UiLTextTable`（失败显式提示）。
- 命名、结构与复杂度：新增文件单一职责（模型/序列化/生成/diff/遍历/绑定器/Inspector 分离）；`UiContractGenerator` 约 390 行、以 4 个小收集函数 + 校验 + 哈希组织，无 >400 行多职责文件。

## 安全与性能

- 安全（注入、权限、敏感信息、输入边界）：无网络/权限面；文件写入限定 `Assets/` 前缀（新增守卫）；`sourceHash` 用 SHA-256、无密钥；错误信息不含敏感数据；`LTextKeyRef` 只存 key，不引入富文本/参数注入面（`Get` 返回作者文本，参数化归工程动态文案）。
- 性能与资源：静态文案只在页面打开/语言变更时线性扫描（`GetComponentsInChildren` 一次，无标记时零订阅）；契约生成与 hash 是编辑器动作，`UiBindSurfaceWalker` 每节点一次分配，页面/模板规模下可接受；`UiLTextTable` 结果进程内缓存并提供重载；`run_tests` 前的新鲜度检查只做 `Assets/**/*.cs` 与 `Library/ScriptAssemblies/*.dll` 的 mtime 比较。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 高（阻塞） | `Assets/LiteGame/Editor/UiTools/Text/LTextKeyRefInspector.cs:15` | `: Editor` 在 `LiteGame.Editor.UiTools.Text` 命名空间下解析成命名空间 `LiteGame.Editor`（CS0118），Editor 程序集编译失败；`run_tests` 仍以旧程序集报告 9/9，首轮 verify 实际验的是旧代码 | 显式 `UnityEditor.Editor`；门禁补「源码新于程序集则强制重编译」与「编译失败即判红」两道守卫；`recompile_status` 确认后复跑 10/10、6/6 | 已修复 |
| C-2 | 中 | `UiContractGenerator.Collect`（原实现） | `templates[]` 未与 controls/text/styles 同过滤面：`Clone_` 与嵌套 `BindRoot` 子树内的 `Tmp_*` 实例被记进本页契约 | 模板收集并入同一遍历/同一过滤面；用例补两条嵌套实例断言（修前会得 3 条模板） | 已修复 |
| C-3 | 低 | `UiContractGenerator` / `UiPageContract` / `UiPageMetadata` | `cacheStrategy` 默认 `"Lru"` 与合法值三串在 4 处手抄，违背方案「单一默认常量」 | 默认值单源 `UiPageContract.DefaultCacheStrategy`（`UICacheStrategy.Lru`），合法值 `Enum.GetNames` | 已修复 |
| C-4 | 低 | `UiContractGenerator.GenerateRegistered` | 手拼 `"Assets/" + row.prefab`，与审计登记的「路径前缀两套约定」同型 | 改用 `PageRow.PrefabPath` 单源 | 已修复 |
| C-5 | 低 | `UiContractGenerator.GenerateToDisk` | `outputPath` 无边界，可写到 `Assets/` 之外（ImportAsset 才报错、文件已落盘） | 写盘前校验 `Assets/` 前缀；新增负例用例 | 已修复 |
| C-6 | 低 | `scripts/gate/unity-tests.py` | `--timeout` 只作用于 python 侧，CLI 默认 30s 误杀慢套件；且编译失败时套件仍“全绿” | 超时透传 `unity command --timeout`；源码/程序集 mtime 检查 + `console_status` 编译失败判红 | 已修复 |
| C-7 | 信息（延后） | `Assets/LiteCodeGen/Editor/EditorTools/BindSearch.cs` | 绑定**生成器**域仍是独立遍历（生成代码需要自己的错误收集/标识符规则），与 walker 两份语义 | 本批只保证 hash/契约/校验同源；生成器域统一留生成器改造批（已在进度卷「边界与遗留」登记） | 已登记 |
| C-8 | 信息（延后） | `UiContractGenerator` | schema 的 `bindName`/`key` 正则未在生成期强制（真实页面当前全部合规） | 属 P1-4 校验规则族（`ui.binding-naming` 等已有实现面），不在本批范围 | 已登记 |

无其他阻塞问题。检查过的风险面：生命周期/池化复用、语言服务未注入、重复 BindName、模板边界、JSON 确定性、文件写入边界、编译门禁真空。

## 复测记录

- 修复后执行的命令与结果（最终代码状态）：
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-contract verify` → **PASS 13/13**（L1 1819 · doc · restore · l0 · toolchain-tests · toolchain-check · toolchain-docs-check · doc-lane · l2-meta · debug-kernel-editmode · ui-static-text-editmode · ui-contract-editmode · ui-contract-diff-editmode）。
  - 定向夹具：`UiStaticText` **6/6** · `UiContractGenerator` **10/10** · `UiContractDiff` **5/5** · `UiBindingTools`（hash 回归）**12/12**。
  - `python scripts/gate/doc-lint.py` → OK（0 violation）。
- 验证证据：evidence/verify.json

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: DeepSeek Harness（接管本批的代理，独立复核 diff 与命令输出）
- 评审时间: 2026-10-22T22:25:00+08:00
