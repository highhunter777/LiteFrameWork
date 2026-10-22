# 测试计划：ui-p1-contract

## 测试策略

- 层级：Unity EditMode（真实 `UIForm`/`UIService`、真实 prefab 资产、真实 `BindNode` 标记）；不涉及 PlayMode/真机。
- 先红后绿：先用返回空结果的最小骨架让新增用例可编译，再运行定向用例取得断言失败证据（`evidence/red.json`）；实现后复跑至全绿。
- 回归面：`UiBindingToolsEditModeTests` 的绑定 hash 用例必须保持绿，证明遍历抽取没有改变既有 hash 值。
- 门禁：定向 UI 用例 + L1 + doc-lint + L2 meta 扫描。

## 用例清单

| 用例 | 覆盖验收 | 预期 |
|---|---|---|
| `UiStaticTextEditModeTests.绑定器_按key写入_语言变更刷新_解绑后不刷新` | AC2 | 打开写源语言；切 en 刷新；解绑后切回 zh 不刷新 |
| `UiStaticTextEditModeTests.页面打开与关闭_静态文案随页面生命周期刷新` | AC2 | UIService 打开页面即写入；关闭后语言事件不再改文本 |
| `UiStaticTextEditModeTests.无文本组件_不抛异常且不影响其他节点` | AC2 边界 | 坏节点告警一次，其他节点照常写入 |
| `UiStaticTextEditModeTests.空key_不写入作者文本` | AC2 边界 | 空 key 不覆盖 prefab 作者文本 |
| `UiContractGeneratorEditModeTests.契约生成_提取四段_并过滤模板内部与克隆与嵌套根` | AC4 | controls 含 Button_Start/List_Rows/Txt_Title；排除 Clone_、嵌套 BindRoot、模板内部件；text/styles/templates 各一段 |
| `UiContractGeneratorEditModeTests.契约生成_确定性_无时间戳_重复生成零diff` | AC4 | 两次序列化逐字节相等；不含 generatedAt |
| `UiContractGeneratorEditModeTests.sourceHash_对绑定面敏感_对页面元数据不敏感` | AC4 | formId 变化不改 hash；BindName 变化改 hash |
| `UiContractGeneratorEditModeTests.契约写盘_读回一致_负例不写盘` | AC4 | 成功写盘可读回；重复 BindName 负例不写盘 |
| `UiContractGeneratorEditModeTests.重复BindName_生成失败并列出错误` | AC4 负例 | `Ok=false`，错误含“重复” |
| `UiContractGeneratorEditModeTests.静态key为空_生成失败` | AC4 负例 | `Ok=false`，错误含“key” |
| `UiContractGeneratorEditModeTests.类型不可解析_生成失败` | AC4 负例 | `Ok=false`，错误含“类型不可解析” |
| `UiContractDiffEditModeTests.新增控件_向后兼容` | AC4 | Added 非破坏 |
| `UiContractDiffEditModeTests.控件改名_判为破坏性` | AC4 | Renamed 且 Breaking |
| `UiContractDiffEditModeTests.控件类型与文案key变化_判为破坏性` | AC4 | controls/text Changed 且 Breaking |
| `UiContractDiffEditModeTests.样式变化_非API破坏` | AC4 | Visual 非破坏 |
| `UiContractDiffEditModeTests.sourceHash_only变化_不误报破坏` | AC4 | SourceOnly 非破坏 |

## 预期失败证据

- `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-contract verify --red`
- 结果：PASS；`ui-static-text-red`、`ui-contract-red`、`ui-contract-diff-red` 均 exit=1，失败信息为断言失败（文本未写入、契约字段为空、diff 无变更），不是环境/导入错误。
- 证据文件：`.workflow/active/ui-p1-contract/evidence/red.json`。

## 运行方式

```bash
python scripts/gate/unity-tests.py --mode editor --filter UiStaticTextEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiContractGeneratorEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiContractDiffEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiBindingToolsEditModeTests --timeout 600
```
