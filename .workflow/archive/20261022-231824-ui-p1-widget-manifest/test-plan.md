# 测试计划：UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

> 运行 ui-p1-widget-manifest · 创建于 2026-10-22T22:40:50+08:00

## 测试策略

- 层级：Unity EditMode（真实 `Tmp_*` prefab 资产、真实组件、真实 `AssetDatabase`/`PrefabUtility`）；不涉及 PlayMode/真机。
- 先红后绿：先落最小骨架（`WidgetSpec` 数据组件、`DiscoverSlots` 返回空、生成器返回空清单），运行两个定向夹具取得**断言失败**证据；实现行为后复跑至全绿。
- 真实资产基线：`真实模板目录_全部声明且零告警` 在内容落盘（T4）后必须通过——30 个模板全部 `specPresent=true`、清单 0 告警。
- 回归面：`UiWidgetLibraryEditModeTests`（目录即清单 6/6）、`UiBindingToolsEditModeTests`（hash 12/12）、P1-1 三个套件（静态文案/契约生成/契约 diff）。
- 门禁：定向 UI 用例 + L1 + doc-lint + L2 meta 扫描。

## 用例清单

| 用例 | 覆盖验收 | 预期 |
|---|---|---|
| `WidgetSpecEditModeTests.默认声明_空插槽空状态_固定尺寸` | AC1 | 新组件默认 Owner=art.ui、数组为空、SizeBehavior=Fixed |
| `WidgetSpecEditModeTests.发现插槽_只收Cot与Slot_排除克隆与嵌套根与模板内部` | AC1 | 只收 `Cot_Header`/`Slot_Content`；`Clone_`/嵌套 `BindRoot`/模板实例内部件排除 |
| `WidgetSpecEditModeTests.发现结果并入_幂等_保留作者填写` | AC1 | 只补缺失插槽；重复并入 0 新增；已有 description 保留 |
| `UiWidgetManifestEditModeTests.清单_汇总声明与属性与hash` | AC2 | 模板/使用段/spec/插槽/状态/事件/尺寸/约束/属性/bindingHash 全部投影正确 |
| `UiWidgetManifestEditModeTests.清单_缺spec_告警不阻断` | AC2 | `Ok=true`、`specPresent=false`、Warning 可定位、属性仍投影 |
| `UiWidgetManifestEditModeTests.清单_确定性_重复生成零diff` | AC2 | 两次序列化逐字节相等、无 `generatedAt` |
| `UiWidgetManifestEditModeTests.sourceHash_对声明敏感_对路径不敏感` | AC2 | 同声明不同路径指纹相同；Summary/States 变化指纹不同 |
| `UiWidgetManifestEditModeTests.清单_插槽节点缺失_报错` | AC2 负例 | `Ok=false`，错误含「插槽」 |
| `UiWidgetManifestEditModeTests.清单_重复插槽_报错` | AC2 负例 | `Ok=false`，错误含「重复」 |
| `UiWidgetManifestEditModeTests.清单_事件成员解析不到_报错` | AC2 负例 | `Ok=false`，错误含「事件」 |
| `UiWidgetManifestEditModeTests.清单_空事件名_报错` | AC2 负例 | `Ok=false`，错误含「事件」 |
| `UiWidgetManifestEditModeTests.清单_空约束_报错` | AC2 负例 | `Ok=false`，错误含「约束」 |
| `UiWidgetManifestEditModeTests.清单_写盘读回一致_输出路径限定Assets` | AC2 | 成功写盘可读回；`Assets/` 外路径失败不写盘 |
| `UiWidgetManifestEditModeTests.真实模板目录_全部声明且零告警` | AC3 | ≥30 条、全部 `specPresent`、0 告警、`sourceHash` 合法 |

## 预期失败证据

- `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-widget-manifest verify --red`
- 结果：`widget-spec-red` 2/3 失败（空发现 → `Expected: equivalent to <Cot_Header, Slot_Content> / But was: <empty>`；并入 0 条），`widget-manifest-red` 9/11 失败（空清单 → `Sequence contains no elements`、负例 `Expected: False / But was: True`、真实目录 0 条）——均为断言失败，不是环境/导入错误。
- 证据文件：`.workflow/active/ui-p1-widget-manifest/evidence/red.json`。

## 运行方式

```bash
python scripts/gate/unity-tests.py --mode editor --filter WidgetSpecEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiWidgetLibraryEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiBindingToolsEditModeTests --timeout 600
```
