# 代码评审：UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

> 运行 ui-p1-widget-manifest · 创建于 2026-10-22T22:40:50+08:00

## 变更范围

- 对照任务与方案：T1（声明组件/词表）→ AC1；T2（作者面）→ AC1；T3（清单/生成器/schema）→ AC2；T4（30 件内容落盘）→ AC3；T5（先行测试）→ AC2/AC3；T6（门禁/回写）→ AC4/AC5。无范围外改动。
- 变更文件/模块：
  - 运行时：`Assets/LiteClient/UI/Spec/{WidgetSpec,WidgetSlotSpec,UiWidgetState,UiWidgetSizeBehavior}.cs`（新增）。
  - 编辑器：`Assets/LiteGame/Editor/UiTools/Widgets/{UiWidgetManifest,UiWidgetManifestGenerator,UiWidgetManifestJson,WidgetSpecAuthoring,WidgetSpecInspector}.cs`（新增）、`Assets/LiteGame/Editor/UiTools/UiDeterministicJson.cs`（新增）、`Contract/UiContractJson.cs`（改为委托共用序列化口径）。
  - 内容：30 个 `Assets/ShareResource/UI/Widgets/Tmp_*.prefab`（仅新增 `WidgetSpec` 组件/字段，+627/−0）；迁移输入 `AgentScripts/{BuildWidgetSpecs.cs,widget-specs.json}`。
  - 测试：`Assets/Tests/EditMode/{WidgetSpecEditModeTests,UiWidgetManifestEditModeTests}.cs`（新增，14 例）。
  - 文档：`Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md` §8.4、`UI系统/README.md`、`UiWidgetManifest.schema.json`（新增）、`Docs/施工进度/UI系统线.md` 第 2 章、`Docs/审计/代码质量审计-客户端.md`（挂账 1 条）。

## 正确性与边界

- 主路径：`WidgetSpec` 声明 → `WidgetSpecAuthoring` 发现/并入插槽 → `UiWidgetManifestGenerator` 扫描模板并汇总（spec + `UiWidgetPropertyCatalog` 属性 + `UiBindGenerationHash`）→ 稳定排序 → `sourceHash` → `UiDeterministicJson` 写盘。
- 边界与错误路径（均有用例）：缺 spec（Warning + `specPresent=false`，不阻断）、插槽名不合约定/节点不存在/重名、事件空/重名/`类型.成员` 解析不到、状态重复、约束空串/重名、模板根名与文件名不一致、输出路径不在 `Assets/`、负例不写盘。
- 回归风险：`UiContractJson` 改委托后契约序列化逐字节不变（`UiContractGenerator` 10/10 含「重复生成零 diff」）；`UiWidgetLibrary`（目录即清单）6/6；绑定 hash 12/12；静态文案 6/6。prefab 仅新增组件，无节点/引用变化（抽查 `Tmp_HpBar` diff 为纯新增 MonoBehaviour）。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：是。14 例覆盖默认声明、插槽发现的三种排除面、幂等并入、清单汇总（使用段/属性/bindingHash）、确定性（逐字节、无 `generatedAt`）、`sourceHash` 敏感/不敏感、6 个负例、写盘读回与路径边界、真实目录 30/30 spec + 0 告警。
- 是否存在迎合实现的测试：否。断言基于公开行为与资产事实（模板名、属性白名单、hash 长度、JSON 字节），未断言私有实现；红测证据（2/3、9/11 失败）证明先红后绿。

## 通用性与扩展性

- 根因与复用边界：模板能力此前只存在于文档表格；本批把声明放回模板、清单做投影。复用 `UiWidgetLibrary`（目录即清单）、`UiWidgetPropertyCatalog`（attribute 单源）、`UiBindSurfaceWalker`（遍历单源）、`UiBindGenerationHash`（模板 hash）、`UiDeterministicJson`（序列化口径单源）。
- 扩展点与接入方式：新模板挂 `WidgetSpec` 即进清单；新状态/尺寸行为扩枚举并同步设计文档；清单新增字段走 schema 次版本；P1-4 校验规则可直接消费 `warnings`/`sourceHash`/条目字段。
- 硬编码、特判、魔法值：无按模板名分支；状态/尺寸为枚举；事件由反射校验；30 件内容表是**一次性迁移输入**（落盘后真相在 prefab），不参与运行时；模板枚举来自目录。
- 重复逻辑与过度抽象：清单与契约共用序列化口径；插槽前缀判定收敛为 `IsSlotName`（私有词表 + 公开判据），无公开可变静态数组。
- 相关场景复用与回归影响：`UiContractJson` 委托改造对页面契约零行为变化；`WidgetSpec` 只声明不驱动，运行时零行为、零第三方依赖。
- 命名、结构与复杂度：新增文件单一职责（组件/词表/作者面/Inspector/模型/生成器/存储分离）；生成器按投影/校验/指纹分段，无 >400 行多职责文件。

## 安全与性能

- 安全：无网络/权限面；清单写盘限定 `Assets/` 前缀；无密钥/敏感信息；`WidgetSpec` 不含可执行逻辑。
- 性能与资源：清单生成在编辑器一次扫描 30 个模板 + 反射，实测 ~0.8s（含属性收集与 hash）；无缓存（模板数量小，无需）；prefab 保存走 unity-pipeline 一次性脚本，幂等可重跑。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 中 | `UiWidgetManifestGenerator` | 结果类 `Warnings` 与 `Manifest.Warnings` 双份，生成器写前者、序列化读后者 ⇒ 清单告警恒空（红测「告警需可定位到模板」失败） | 结果类只暴露 `Manifest.Warnings`（单一来源），写入口统一 | 已修复 |
| C-2 | 中 | `AgentScripts/widget-specs.json` | `Tmp_RedDot` 声明 `RedDot.OnChanged`，但该事件在 `RedDotNode` 上（`RedDot` 无公开事件）⇒ 生成器反射校验报错、真实目录用例红 | 数据改为 `events=[]`（红点无根组件公开事件）；机制校验按设计拦下错误，不静默放过 | 已修复 |
| C-3 | 低 | `WidgetSpecAuthoring` | `public static readonly string[] SlotPrefixes` 是公开可变静态数组（红线 6 精神：禁新增 public static 可变状态） | 收敛为私有前缀表 + 公开判据 `IsSlotName`；生成器改调判据 | 已修复 |
| C-4 | 低 | `Contract/UiContractJson.cs` | 契约 JSON 的序列化设置与控件清单将要各写一份（重复实现风险） | 抽出 `UiDeterministicJson` 单源，契约与清单共用；契约用例 10/10 保持逐字节一致 | 已修复 |
| C-5 | 信息（延后） | `UiWidgetManifestGenerator.TryResolveEvent` | 事件解析按**简单类型名**匹配第一个组件；若两组件跨命名空间同名，可能解析到另一个类型 | 当前 30 件模板类型名唯一；P1-4 规则批可改全名匹配 | 已登记 |
| C-6 | 信息（延后） | `UiWidgetManifestGenerator.GenerateToDisk` | 只有默认目录扫描入口，无「显式路径 + 写盘」重载；测试用 `Generate(paths)` + `Store.Save` 组合 | P1-5 工具接线时按需补重载 | 已登记 |

无其他阻塞问题。检查过的风险面：告警/错误分流、事件成员漂移、prefab 批量改动的结构安全、序列化确定性、输出路径边界、真实目录基线。

## 复测记录

- 修复后执行的命令与结果（最终代码状态）：
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-widget-manifest verify` → **PASS 15/15**（L1 1819 · doc · restore · l0 · toolchain-tests · toolchain-check · toolchain-docs-check · doc-lane · l2-meta · debug-kernel-editmode · ui-static-text · ui-contract · ui-contract-diff · widget-spec · widget-manifest）。
  - 定向夹具：`WidgetSpec` **3/3** · `UiWidgetManifest` **11/11** · 回归 `UiWidgetLibrary` 6/6 · `UiBindingTools` 12/12 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiStaticText` 6/6。
  - `python scripts/gate/doc-lint.py` → OK（0 violation）；L2 meta 扫描 14184 个 `.meta` 全合法。
- 验证证据：evidence/verify.json

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: DeepSeek Harness（本批执行代理，独立复核 diff、资产改动与命令输出）
- 评审时间: 2026-10-22T23:20:00+08:00
