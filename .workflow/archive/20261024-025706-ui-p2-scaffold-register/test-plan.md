# 测试计划：UI 系统 P2-2：起页/登记拆分

> 运行 ui-p2-scaffold-register · 方案 v4 · 验收标准 `acceptance.md` AC1–AC8

## 测试策略

| 层 | 覆盖 | 理由 |
|---|---|---|
| Python 单测 | `_uiform_row.py --table` 全分支（list/check/suggest/confirm/rollback/delete）、layer 0–3、`Page_` PascalCase、真实表字节不变 | 表是唯一数据面；把路径注入与校验规则钉在脚本层，避免 C# 夹具借道生产表 |
| Unity EditMode（新） | `UiPageScaffoldRegisterEditModeTests`：起页（产物/根契约/安全区/原子失败）；草稿契约（生成/formId=0/确定性/漂移/diff 口径）；登记（契约门槛/表核验回滚/生成链/清单状态/缓存策略/契约刷新/hash 规则/端口注入失败）；工作台（可用性/能力/事件/保存冲突/焦点重载） | 起页与登记是编辑器内资产/表/清单事务，必须用真实 `AssetDatabase`/`PrefabUtility` 与真实表副本 |
| Unity EditMode（回归） | `UiWorkbench`、`UiPreview`、`UiPageManifest`、`UiValidation`、`UiContract*`、`UiToolchainWiring`、`UiUimainRetirement`、`UiBindingTools`、`EditorToolCatalog` | 防“新工具破坏既有契约/基线/工具面” |
| 命令级验证 | `toolchain.py check`、`docs --check`、`gen-build-hash.py --check-shared`、L1/Doc、`wf verify` | 门禁与真实生成链一致性 |
| 冒烟 | `unity command eval_file` 打开两窗口（标题/关键字段/Console 0 error）+ 人工点击一次 | 编辑器窗口无法自动化点击；红线 13 要求真实操作路径，自动化边界如实记录 |

## 用例清单

### Python（scripts/tests/test_uiform_row.py）

| 编号 | 用例 | 预期 |
|---|---|---|
| P1 | `--list --table <副本>` | ok=true，行数与真实表一致；真实表字节不变 |
| P2 | `--check` 重复 id / 保留 id | ok=false，错误含“已存在/保留区” |
| P3 | `--confirm` 新页写副本 | ok=true，备份路径在副本目录旁；`--list` 可见新行 |
| P4 | `--rollback <备份> --table <副本>` | 恢复后 `--list` 无新行；副本字节回到写前 |
| P5 | `--delete-id` | 行删除；备份路径返回 |
| P6 | `--layer 4` / `--layer -1` | ok=false（layer 0–3） |
| P7 | 页名 `Page_login` | ok=false（PascalCase） |
| P8 | 真实表字节 | 每个用例前后与初始 SHA256 一致 |

### EditMode 起页（UiPageScaffoldRegisterEditModeTests）

| 编号 | 用例 | 预期 |
|---|---|---|
| S1 | `Scaffold(Page_TestScaffoldA, Tmp_BaselineA, tempManifest)` | Ok；prefab 自包含、根四件套齐备、`SafeAreaReceiver` 在 `Cot_Baseline`（内容容器）上；tempManifest 1 页 Draft/forms=0/owner=art.ui；无 Lua；真实表字节不变 |
| S2 | 目标 prefab 已存在 | `FailedAt.Validate`；tempManifest 不创建；既有 prefab 不被覆盖 |
| S3 | 非法页名（`Page_bad`/`Tmp_Page`） | `FailedAt.Validate`；零产物 |
| S4 | 清单路径父级被文件占用 | `FailedAt.Manifest`；新建 prefab 被删除；清单字节不变 |
| S5 | 基线路径不存在 | `FailedAt.Validate`；零产物 |

### EditMode 草稿契约

| 编号 | 用例 | 预期 |
|---|---|---|
| C1 | 起页后 `UiContractBatch.Generate(tempContracts, tempManifest)` | 草稿契约 `formId=0`、`luaPath=null`、`layer=0`、`fullScreen=true`、name/prefabGuid 正确；重复生成逐字节一致 |
| C2 | 无清单时批量 | 既有 11 页仍生成；Warnings 含“清单缺失，草稿契约未纳入” |
| C3 | 草稿 `Regenerate(entry, allowDraft:true)` | 与盘上契约逐字节一致；修改 prefab 后不一致（漂移可检出） |
| C4 | `UiContractDiff.Compare` 草稿→真实 | `formId 0→N` 非破坏；`cacheStrategy`/`layer`/`fullScreen` 变化非破坏；`luaPath null→路径` 非破坏；真实→真实 formId 变化仍破坏 |
| C5 | 既有 11 份契约 | 生成后逐字节与基线一致（dir 摘要不变） |

### EditMode 登记

| 编号 | 用例 | 预期 |
|---|---|---|
| R1 | 正常登记（临时表副本/tempLua/tempManifest/tempBind/假 codegen 全成功/真契约刷新到 tempContract） | Ok；表新增一行+备份；Lua 骨架存在；codegen 调用序 `Luban→Hash`；清单 `Registered`+formId+`DestroyOnClose`；tempContract 重生成 `formId=真实` 且缓存策略一致；无 BindRoot → 绑定 hash 清单无该页 |
| R2 | 无草稿契约 | `FailedAt.Contract`；不建 Lua、不写表（副本字节不变） |
| R3 | 契约 `formId≠0` 或 `prefabGuid` 不符 | `FailedAt.Contract`；零产物 |
| R4 | 无草稿条目 | `FailedAt.Validate`；零产物 |
| R5 | id 与副本既有行冲突 / 保留 id | `FailedAt.Validate`；不建 Lua |
| R6 | layer=4/-1、缓存策略非法 | `FailedAt.Validate`；零产物 |
| R7 | `IUiPageTablePort` 假实现：先真实落行再返回失败 | `FailedAt.WriteTable`；`TryFindRow` 命中后回滚并复核行不存在；本次新建 Lua 删除；副本字节回到写前 |
| R8 | 假 codegen：Luban 失败 | `FailedAt.Codegen`；表行回滚并复核；清单仍 Draft；调用序列含“失败后重跑 Luban” |
| R9 | 假 codegen：Luban 成功、Hash 失败 | `FailedAt.Codegen`；回滚+复核+重跑 Luban 复原；清单 Draft |
| R10 | 假 `IUiPageContractRefresher` 返回失败 | `FailedAt.ContractRefresh`；清单已 Registered；错误含“契约未刷新/重跑 ui.contract” |
| R11 | 注入 `TablePath` 但 `Codegen` 为默认（null） | 直接拒绝（混用生产生成链） |
| R12 | 页面挂 `BindRoot` 后登记 | 绑定 hash 清单新增该页条目 |

### EditMode 工作台

| 编号 | 用例 | 预期 |
|---|---|---|
| W1 | Availability | Scaffold/Register 均 `IsImplemented=true`，无 P2-2 原因 |
| W2 | 页面透视能力标签 | Page 透视含 `page.scaffold`/`page.register` |
| W3 | 工具清单 | Catalog 含 `ui.page-scaffold`/`ui.page-register`（window，无 Error） |
| W4 | 页面透视请求事件 | 触发 `ScaffoldRequested`/`RegisterRequested` 各一次 |
| W5 | 外部清单变更 + 无未保存 | `ReloadIfChanged` 返回 true，选中页与目标保留 |
| W6 | 外部清单变更 + 有未保存编辑 | `ReloadIfChanged` 返回 false、不重载、`ExternalChanged=true` |
| W7 | 磁盘外改后保存 | `TrySave` 返回 false；磁盘 `sourceHash` 不变（不覆盖外部写入） |

## 预期失败证据

- 先落可编译骨架（T1）：`Scaffold/Register` 返回 `FailedAt.NotImplemented`/`ScaffoldFailure.NotImplemented`，`Baselines` 空、`SetCacheStrategy` 返回 false、`UiContractBatch` 草稿重载/`Regenerate(allowDraft)` 返回显式错误、工作台事件/重载/冲突 API 为空实现且 Availability 仍挂起。
- 运行 `python scripts/gate/unity-tests.py --mode editor --filter UiPageScaffoldRegisterEditModeTests`：**全部用例断言失败**（正例断言产物/状态；负例断言具体失败步/磁盘字节），编译 0 错、无环境错误。
- 运行 `python -m unittest scripts.tests.test_uiform_row -v`：P1–P8 因 `--table` 未实现以非零退出/断言失败；这是“CLI 契约未实现”的预期失败（任务评审 R1 已登记），T2 起改为行为断言。
- 保存 `evidence/red.json`（`wf verify --red`，需先把新夹具加入 `.workflow/config.json` 的 `red_commands`）。

## 运行方式

```powershell
# 新增 EditMode 夹具（先红 / 后绿）
python scripts/gate/unity-tests.py --mode editor --filter UiPageScaffoldRegisterEditModeTests --timeout 600

# Python 表助手
python -m unittest scripts.tests.test_uiform_row -v

# 回归
python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiContractDiffEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests --timeout 600

# 全量收尾
python scripts/gate/test.py --lane L1 --profile PullRequest
python scripts/gate/doc-lint.py
python scripts/toolchain.py check
python scripts/codegen/gen-build-hash.py --check-shared
```
