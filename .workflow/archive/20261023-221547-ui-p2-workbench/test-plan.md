# 测试计划：UI 系统 P2-1 单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23

## 测试策略

- **测试层级**：EditMode 单元/契约级为主（Unity Editor 夹具，无 PlayMode）。理由：本批交付的是编辑器制作面（窗口 + 策略 + 投影），核心判定全部可外提为纯类（`UiLanePolicy`/`UiWorkbenchState`/`UiWorkbenchPageList`/`UiWorkbenchPageEditor`/`UiContractReportView`），窗口只做绘制与转发；窗口自身用 unity-pipeline 手动冒烟（T6 证据位），不把无法实例化的 UI 写进自动夹具。
- **关键风险与覆盖重点**：
  1. Lane 矩阵漂移 → 全动作 × 全角色穷举断言，禁止只测两三条。
  2. 上下文丢失（切透视/切角色重置目标）→ 状态切换用例。
  3. 清单/契约投影复制第二份逻辑 → 真实清单纯只读断言 + 投影只做字段映射。
  4. 清单误写 → 编辑落盘用临时路径，真实清单只读；确定性逐字节断言。
  5. 入口分叉（`ui.compose` 残留）→ Catalog 断言在册/退役。
- **先失败后通过**：先落最小可编译骨架（类型存在、行为为空），再以上夹具运行到 **10/10 断言失败**（红证据见 §预期失败证据）；实现后同一命令转绿。T2/T5/T6 期间追加 3 例（阶段可用性、能力标签、契约生成集成），最终 13 例；追加用例不改变已观察到的红基线，须在实现后全绿。红必须是断言失败（不是编译/导入/环境错误）。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| 角色矩阵_美术可起页_不可登记与绑定生成 | EditMode | T2 / AC3 | 查询 Art × Scaffold/Register/GenerateBinding | 起页允许；登记/绑生成禁止且原因非空；允许时无原因 |
| 角色矩阵_工程可登记与绑定生成_不可起页 | EditMode | T2 / AC3 | 查询 Engineering × 三动作 | 登记/绑生成允许；起页禁止且原因非空 |
| 角色矩阵_每个动作至少一个角色允许且禁止必有原因 | EditMode | T2 / AC3 | 穷举枚举全部动作 × 全部角色 | 每个动作至少一个角色允许；每个禁止返回非空原因 |
| 透视清单_五透视按序且启用 | EditMode | T2/T6 / AC2 | 读 `UiWorkbenchPerspectives.All` | 5 项、顺序 Page→Widgets→Binding→Contract→Validation、Order 递增、标题唯一、全部启用、未知 id 返回 null |
| 状态_切透视与角色不丢上下文 | EditMode | T3 / AC2 | 选页/设目标/标脏 → 切透视 + 切角色 | 选中页、目标路径、未保存标记保留；ClearDirty 生效 |
| 页面清单投影_真实基线十一页十二行 | EditMode | T4 / AC4 | 读默认清单 | 11 页 / 12 行；Page_Login 有契约、Owner/Prefab 非空 |
| 页面清单投影_缺失时显式报错不伪造空表 | EditMode | T4 / AC4 | 注入不存在的清单路径 | 返回空列表 + 非空错误（不伪造条目） |
| 页面编辑_维护字段落盘确定性 | EditMode | T4 / AC4 | 对 Page_Login 改 Owner/设计稿/备注 → 保存到两个临时路径 | 编辑成功、sourceHash 变化、两次保存逐字节一致 |
| 契约报告_生成与对账投影 | EditMode | T5 / AC5 | 构造生成结果（1 写/1 跳过）与破坏性 diff | 计数/Ok/输出目录/摘要正确；破坏性 → Ok=false、摘要含「破坏性」 |
| 阶段可用性_起页登记归P2二且原因优先 | EditMode | T2 / AC3 | 查询 `UiWorkbenchAvailability` 与 Lane 交叉 | 起页/登记未实现且原因含 P2-2；Lane 允许与阶段未建两层正交 |
| 透视能力标签_迁移未丢动作 | EditMode | T2/T6 / AC2 | 读五透视 Capabilities | 11 个能力标签齐备；绑定含 binding.generate、控件库含 widgets.properties |
| 契约生成_临时目录十一份且重复逐字节一致 | EditMode | T5 / AC5 | `UiContractBatch.Generate(临时目录)` ×2 | 11 份、无 Error、两次逐字节一致（真实生成半的证据） |
| 工具清单_工作台在册_旧制作窗口退役 | EditMode | T7 / AC1 | `EditorToolCatalog.Load().Find(...)` | `ui.workbench` 在册且为 window、标题「UI 工作台」；`ui.compose` 为 null |

## 预期失败证据

- **命令**：`python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests --timeout 600`（对应 `.workflow/config.json` 的 `ui-workbench-red`）
- **观察到的失败**（2026-10-23 20:09，实现前，最小骨架已就位、编译 0 错）：
  - `tests: 10/10 failed`，全部为断言失败：
    - 角色矩阵：`Expected: True / But was: False`（策略表为空）
    - 透视清单：`Expected: 5 / But was: 0`
    - 状态切换：`Expected: Contract / But was: Page`（setter 为空实现）
    - 页面清单：`Expected: 11 / But was: 0`
    - 缺清单：`Expected: not null / But was: null`
    - 页面编辑：`"未实现" / Expected: True / But was: False`
    - 契约报告：`Expected: 1 / But was: 0`（投影为空）
    - 工具清单：`ui.workbench 必须在册`（未登记）
- **为什么这是正确的失败**：所有失败均来自目标行为缺失（空策略/空投影/未登记），而非编译错误、类型缺失或环境异常；红测可作为后续实现的验收基线。
- **最终红证据**：`wf verify --red` PASS（20 命令；`ui-workbench-red` exit=1，其余 19 条通过），见 `evidence/red.json`。

## 运行方式

- **仅新增测试**：`python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests --timeout 600`
- **红测留证**：`python .agents/skills/staged-delivery/scripts/workflow.py verify --red --run ui-p2-workbench`（写入 `evidence/red.json`；含既有 19 条红命令 + 本批 `ui-workbench-red`）
- **全量验证**：`python .agents/skills/staged-delivery/scripts/workflow.py verify --run ui-p2-workbench`（30 条命令：L1 + 全部 UI 夹具 + doc/l0/toolchain）
- **手动冒烟（窗口，T6 证据位）**：`python scripts/toolchain.py open ui.workbench` 后经 unity-pipeline 读取 Console 与窗口状态；逐透视切换、选真实页面、跑一次契约 diff（只读），确认 `[ui/workbench]` 输出且 Console 无 error。

## 豁免与说明

- **窗口 UI 不写自动夹具**：`EditorWindow` 在 EditMode 下实例化与绘制依赖 GUI 事件循环，不可靠；按方案风险 2 的处理，逻辑全部外提为纯类，窗口仅冒烟。
- **真实清单只读**：AC4 的编辑用例写临时路径（`Assets/LiteGame/Editor/UiTools/TempWorkbenchTests/`，用例结束删除），避免污染入库清单。
- **`ui.compose` 退役断言**：本批之前该断言为「在册」（`UiPreviewEditModeTests`），T7 同批改为「退役」，属预期变更而非弱化。
