# 测试计划：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

> 运行 ui-p1-page-manifest · 创建于 2026-10-22T23:28:19+08:00

## 测试策略

- 层级：Unity EditMode（真实 `tbuiform` 读面经 `UiPageScaffold.TryListPages`、真实 prefab/契约路径、真实 `AssetDatabase`）。
- 先红后绿：先落最小骨架（`Sync` 返回空清单、编辑 API 返回 null/false），运行定向夹具取得**断言失败**证据；实现后复跑至绿。
- 真实资产基线：`真实页面_基线齐全且零错误` 要求真实 13 行全部入清单、Owner 非空、状态在五态词表内、0 告警。
- 回归面：P1-1/P1-2 四套件（静态文案/契约生成/契约 diff/控件清单）+ `WidgetSpec` + `UiWidgetLibrary`。
- 门禁：定向 UI 用例 + L1 + doc-lint + L2 meta。

## 用例清单

| 用例 | 覆盖验收 | 预期 |
|---|---|---|
| `UiPageManifestEditModeTests.首次生成_按表行建立条目` | AC2 | 两条目、按名排序、身份字段投影（含默认 `Lru`/`Registered`/`art.ui`/契约路径） |
| `UiPageManifestEditModeTests.对账保留维护字段_刷新身份字段` | AC2 | Owner/状态/设计稿/设备档/备注保留；layer/luaPath 刷新 |
| `UiPageManifestEditModeTests.新增表行_新增条目` | AC2 | 新行产生 `Registered` 条目 |
| `UiPageManifestEditModeTests.孤儿条目_Draft保留_非Draft告警` | AC2 | 草稿保留无告警；非 Draft 保留 + 告警 |
| `UiPageManifestEditModeTests.重复formId_报错` | AC2 负例 | `Ok=false`，错误含「重复」 |
| `UiPageManifestEditModeTests.重名页面_报错` | AC2 负例 | `Ok=false`，错误含「重名」 |
| `UiPageManifestEditModeTests.非法状态_报错且不写盘` | AC1/AC2 负例 | `Ok=false`、错误含「状态」、磁盘无文件 |
| `UiPageManifestEditModeTests.确定性_重复生成零diff` | AC1 | 两次序列化逐字节相等、无 `generatedAt` |
| `UiPageManifestEditModeTests.sourceHash_对维护字段敏感` | AC1 | Owner 不同 → 指纹不同 |
| `UiPageManifestEditModeTests.写盘读回一致_输出路径限定Assets` | AC1 | 写盘可读回；`Assets/` 外路径失败不写盘 |
| `UiPageManifestEditModeTests.草稿创建_幂等` | AC3 | 草稿无 formId、`Draft`、重复调用同一条目 |
| `UiPageManifestEditModeTests.状态流转_合法与非法` | AC3 | 迁移表内通过、Wired→Draft 拒绝、同值无操作、未知页面报错 |
| `UiPageManifestEditModeTests.编辑维护字段_更新指纹` | AC3 | 维护字段写入 + `sourceHash` 变化 |
| `UiPageManifestEditModeTests.真实页面_基线齐全且零错误` | AC3 | ≥13 条、formId>0、Owner 非空、状态合法、0 告警 |

## 预期失败证据

- `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-page-manifest verify --red`
- 结果：`page-manifest-red` 12/14 失败（空清单 → `Expected: 2 / But was: 0`、`Sequence contains no elements`、`Expected: True / But was: False`、草稿 `Assert.IsNotNull` 失败等），其余 7 条 red 命令通过；均为断言失败，不是环境/导入错误。
- 证据文件：`.workflow/active/ui-p1-page-manifest/evidence/red.json`。

## 运行方式

```bash
python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter WidgetSpecEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiContractGeneratorEditModeTests --timeout 600
```
