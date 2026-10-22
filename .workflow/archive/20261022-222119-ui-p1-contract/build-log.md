# 施工记录：UI 系统 P1-0/P1-1：口径冻结 + 静态文案 + 页面契约内核

> 运行 ui-p1-contract · 创建于 2026-10-22T21:01:41+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-22T21:04:50+08:00 · 新增任务 T1「P1-0 口径冻结与文档回写」
- 2026-10-22T21:04:50+08:00 · 新增任务 T2「静态文案 LTextKeyRef 与运行时绑定」
- 2026-10-22T21:04:50+08:00 · 新增任务 T3「绑定面遍历单源与 hash 回归」
- 2026-10-22T21:04:50+08:00 · 新增任务 T4「页面契约模型、生成器与 diff」
- 2026-10-22T21:04:50+08:00 · 新增任务 T5「先行测试与负例覆盖」
- 2026-10-22T21:04:51+08:00 · 新增任务 T6「门禁、评审与回写」
- 2026-10-22T21:45:00+08:00 · 接手在途工作：核对方案/任务/红测证据（evidence/red.json 5 命令 3 预期失败），复跑四个定向夹具——`UiStaticText` 5/5、`UiContractGenerator` 9/9、`UiContractDiff` 5/5、`UiBindingTools`（hash 回归）12/12 全绿。
- 2026-10-22T21:52:00+08:00 · 评审补两处收紧（模板边界过滤、门禁超时透传）＋`cacheStrategy` 默认值单源化、`LTextKeyRefInspector` 拆件；复跑 `UiContractGenerator` 9/9（含新增模板边界断言）。
- 2026-10-22T22:02:00+08:00 · 门禁预跑：L1 **PASS 1819**、`doc-lint` OK、L2 meta 扫描 PASS；设计文档回写（系统总设计 §4.1/§4.2 接缝）与进度卷 `Docs/施工进度/UI系统线.md` 第 1 章落盘。
- 2026-10-22T22:02:30+08:00 · 开始任务 T1「P1-0 口径冻结与文档回写」
- 2026-10-22T22:02:42+08:00 · 完成任务 T1 · 证据: doc-lint OK（0 violation）；口径写入 UI编辑器工具链专项设计 §5.2.1（sourceHash 五域/模板边界/kind 推断/cacheStrategy 默认/确定性）与 UI美术协作与交接规范 §3；schema 与设计样例无 generatedAt；UI系统总设计 §4.1/§4.2 …
- 2026-10-22T22:02:54+08:00 · 开始任务 T2「静态文案 LTextKeyRef 与运行时绑定」
- 2026-10-22T22:02:55+08:00 · 开始任务 T3「绑定面遍历单源与 hash 回归」
- 2026-10-22T22:02:59+08:00 · 完成任务 T2 · 证据: UiStaticTextEditModeTests 5/5；UIForm 接缝（PrepareForShow 惰性建 binder / ApplyLocale 同驱 ILocaleBound 与静态文案 / EnterClosing 退订 / DestroyInstance 释放）；LTextKeyRef + UiS…
- 2026-10-22T22:03:04+08:00 · 完成任务 T3 · 证据: UiBindSurfaceWalker 成为 hash/契约共同遍历面；UiBindGenerationHash 改消费同一遍历；UiBindingToolsEditModeTests 12/12（既有 hash 值不变）
- 2026-10-22T22:03:08+08:00 · 开始任务 T4「页面契约模型、生成器与 diff」
- 2026-10-22T22:03:09+08:00 · 完成任务 T4 · 证据: UiContractGenerator 9/9（确定性/写盘读回/负例不写盘/模板边界过滤）；UiContractDiff 5/5；sourceHash 对页面元数据不敏感、对绑定面敏感
- 2026-10-22T22:03:14+08:00 · 开始任务 T5「先行测试与负例覆盖」
- 2026-10-22T22:03:14+08:00 · 完成任务 T5 · 证据: 3 个夹具 19 例（5+9+5）；先行红测 evidence/red.json（断言失败而非环境错误）；实现后复跑全绿
- 2026-10-22T22:03:30+08:00 · 开始任务 T6「门禁、评审与回写」
- 2026-10-22T22:03:30+08:00 · 完成任务 T6 · 证据: L1 PASS 1819；doc-lint OK 0 violation；L2 meta 扫描 PASS（14170 meta 全合法）；定向夹具 5/5+9/9+5/5+12/12；设计文档接缝回写 + 进度卷 Docs/施工进度/UI系统线.md 第 1 章落盘
- 2026-10-22T22:11:00+08:00 · 复跑发现门禁真空：Editor 程序集 CS0118（`LTextKeyRefInspector : Editor`）编译失败时 `run_tests` 仍以旧程序集报告 9/9。修：显式 `UnityEditor.Editor` + 门禁两道守卫（源码比程序集新则强制重编译、编译失败即判红）；复跑 `UiContractGenerator` **10/10**、`UiStaticText` **6/6**（补 TMP 优先路径用例）、`UiContractDiff` 5/5、`UiBindingTools` 12/12。
- 2026-10-22T22:13:59+08:00 · 重新打开任务 T4：新增输出路径守卫用例，测试数与证据更新为 10/10
- 2026-10-22T22:13:59+08:00 · 开始任务 T4「页面契约模型、生成器与 diff」
- 2026-10-22T22:14:00+08:00 · 完成任务 T4 · 证据: UiContractGenerator 10/10（确定性/写盘读回/负例不写盘/模板边界过滤/输出路径守卫）；UiContractDiff 5/5；sourceHash 对页面元数据不敏感、对绑定面敏感
- 2026-10-22T22:14:04+08:00 · 重新打开任务 T5：生成器夹具增至 10 例（输出路径守卫），总例数 20
- 2026-10-22T22:14:04+08:00 · 开始任务 T5「先行测试与负例覆盖」
- 2026-10-22T22:14:04+08:00 · 完成任务 T5 · 证据: 3 个夹具 20 例（5+10+5）；先行红测 evidence/red.json（断言失败而非环境错误）；实现后于新程序集复跑全绿
- 2026-10-22T22:17:12+08:00 · 重新打开任务 T2：补 TMP 优先路径用例，夹具增至 6 例
- 2026-10-22T22:17:12+08:00 · 开始任务 T2「静态文案 LTextKeyRef 与运行时绑定」
- 2026-10-22T22:17:12+08:00 · 完成任务 T2 · 证据: UiStaticTextEditModeTests 6/6（TMP 优先/UGUI 回退/语言刷新/关闭退订）；UIForm 接缝（PrepareForShow 惰性建 binder / ApplyLocale 同驱 / EnterClosing 退订 / DestroyInstance 释放）；LTextKeyRe…
- 2026-10-22T22:17:17+08:00 · 重新打开任务 T6：门禁守卫与夹具数更新，回填最终证据
- 2026-10-22T22:17:17+08:00 · 开始任务 T6「门禁、评审与回写」
- 2026-10-22T22:17:17+08:00 · 完成任务 T6 · 证据: L1 PASS 1819；doc-lint OK 0 violation；L2 meta 扫描 PASS（14170 meta）；定向夹具 6/6+10/10+5/5+12/12；设计文档接缝回写 + 进度卷 Docs/施工进度/UI系统线.md 第 1 章落盘

## 关键决定

- **静态文案所有权放页面实例**（`UIForm` + `UiStaticTextBinder`），不塞进 `UIBindIndex`：无 Lua 逻辑的展示页也要随语言刷新；关闭即退订保证池化复用不残留旧语言事件（方案 A 的取舍落地）。
- **遍历单源在编辑器侧**：`UiBindSurfaceWalker` 供 hash 与契约共用；`UiBindGenerationHash` 反向消费 walker（而不是 walker 调 hash），既有 hash 值以 `UiBindingToolsEditModeTests` 12/12 钉住。
- **`sourceHash` 只覆盖接口面五域**（control/text/style/template/event），不含页面元数据、生成器版本、时间戳——页面登记（formId/luaPath）变化不应让 API 指纹漂移；漂移判定才有意义。
- **`cacheStrategy` 默认值单源**＝`UICacheStrategy.Lru`（词表真相是运行时枚举）；生成器校验用 `Enum.GetNames` 而非手抄 `Resident/Lru/DestroyOnClose` 三串；表列落地后只改 `UiPageMetadata` 投影层。
- **`templates[]` 与 controls/text/styles 同过滤面**：`Clone_` 子树、嵌套 `BindRoot` 子树、模板内部件都不进本页接口面（评审发现原实现只对 controls 过滤）。
- **契约 diff 按 API 键比对**：改名按 `nodePath` 配对（`Renamed` + 破坏性），类型/驱动方式/文案 key 变化判破坏，样式变化记 `Visual` 不判破坏，仅 `sourceHash` 变化记 `SourceOnly`。

## 偏差与处理

- 无方案级偏差。实现期两处收紧：① `templates[]` 补 `Clone_`/嵌套 `BindRoot` 过滤（方案 §设计要点“过滤”已要求，原实现遗漏）；② `scripts/gate/unity-tests.py` 补超时透传（方案未列，验证阶段暴露的工装缺陷，根因是 unity CLI 默认 30s）。
- `LTextKeyRefInspector` 从 `UiLTextTable.cs` 拆为独立文件：方案 T2 文件清单即要求独立件，且两类职责（表读面 / Inspector）不合一文件。
- 本批不写 prefab/场景/表：既有页面未挂 `LTextKeyRef`，契约 `text[]` 为空是试点前正常基线（方案已声明）。

## 问题与修复

- **慢套件被 30s 误杀**：`unity command run_tests` 默认 30s，而 `scripts/gate/unity-tests.py` 只把 `--timeout` 用在 python 侧，导致「编译 + 夹具」超 30s 的套件一律判红（编辑器侧实际 5/5 通过，日志 `Run finished: 5 total, 5 passed`）。修：命令改 `unity command --timeout <s> run_tests ...`，python 侧兜底 `--timeout + 120s`。修后同一命令稳定复跑通过。
- **编译失败仍能“全绿”（门禁真空）**：`LTextKeyRefInspector : Editor` 在 `LiteGame.Editor.UiTools.Text` 命名空间下解析成命名空间 `LiteGame.Editor`（CS0118），Editor 程序集编译失败；但 `run_tests` 会拿**上一次成功编译的旧程序集**照常执行并报告 9/9 通过——首轮 `wf verify` 因此验的是旧代码。修：① 显式 `UnityEditor.Editor`；② `scripts/gate/unity-tests.py` 跑测前查 `console_status.groundTruth.compilationFailed`，编译失败直接判红；③ `recompile_status` 确认 `compilationFailed=false` 后复跑：`UiContractGenerator` **10/10**（含新增输出路径守卫用例）。
- **模板边界漏过滤**：`Clone_` 内与嵌套 `BindRoot` 内的 `Tmp_*` 实例被记进 `templates[]`（与 controls 口径不一致，会让克隆行/嵌套生成单位的模板依赖污染本页契约）。修：模板收集并入同一遍历与同一过滤面；用例补两条嵌套实例断言（修复前会得到 3 条模板 → 断言失败）。
- **`GenerateRegistered` 手拼 `"Assets/" + row.prefab`**（审计登记同型：路径前缀两套约定，`代码质量审计-客户端.md` 低）。修：改用 `PageRow.PrefabPath` 单源。
- **契约输出路径无边界**：`GenerateToDisk(outputPath)` 可把文件写到 `Assets/` 之外（`AssetDatabase.ImportAsset` 才报错，此时文件已落盘）。修：写盘前校验 `Assets/` 前缀，负例返回错误不写盘；新增用例钉住。

## 验证结果

- 2026-10-22T21:14:17+08:00 · 先行测试 PASS · 3/5 · 证据 evidence/red.json
- 定向夹具（最终，编译守卫上线后于新程序集复跑）：`UiStaticText` **6/6** · `UiContractGenerator` **10/10** · `UiContractDiff` **5/5** · `UiBindingTools` **12/12**（新增 21 例）
- 门禁预跑：L1 **PASS 1819** · `doc-lint` **OK** · L2 meta 扫描 **PASS**
- 最终 `wf verify` 全量命令记录见下（`evidence/verify.json`）。
- 2026-10-22T22:06:13+08:00 · 验证 PASS · 13/13 · 证据 evidence/verify.json
- 2026-10-22T22:16:16+08:00 · 验证 PASS · 13/13 · 证据 evidence/verify.json
- 2026-10-22T22:19:22+08:00 · 验证 PASS · 13/13 · 证据 evidence/verify.json

## 交付摘要

- **P1-0 口径冻结**：`sourceHash` 五域输入、`kind`/`required` 推断、模板边界、`cacheStrategy` 默认（词表真相 `UICacheStrategy`）、无时间戳/稳定排序，全部写入《UI 编辑器工具链专项设计》§5.2.1 与《UI 美术协作与交接规范》§3；schema 与设计样例的 `generatedAt` 漂移修正；《UI 系统总设计》§4.1/§4.2 登记 `UIForm` 静态文案接缝与绑定面遍历单源。
- **P1-1 静态文案**：`LTextKeyRef`（只存 Key）+ `UiStaticTextBinder`（TMP 优先/UGUI 回退、缺目标与空 key 告警一次）+ `UIForm` 生命周期接缝（打开写入、语言刷新、关闭退订、销毁释放）+ `LTextKeyRefInspector`（tbltext 下拉与中英预览）。
- **P1-1 遍历与契约**：`UiBindSurfaceWalker` 成为 hash/契约共同遍历面（既有 hash 值不变）；`UiPageContract`/`UiPageMetadata`/`UiContractJson`/`UiContractPaths`/`UiContractStore`/`UiContractGenerator`/`UiContractDiff` 落地，确定性 JSON、`sourceHash`、模板边界、负例不写盘。
- **测试**：新增 21 例（静态文案 6 / 契约生成 10 / 契约 diff 5），先红后绿；hash 回归 12/12。
- **工装**：`scripts/gate/unity-tests.py` 修两个门禁真空（CLI 30s 默认超时、编译失败仍跑旧程序集）。

## 未决事项

- **P1-2～P1-6 未做**：`WidgetSpec`/`UiWidgetManifest`/`UiPageManifest`（P1-2/P1-3）、契约校验规则族与 `UiRuleExceptions`（P1-4）、Hub/CLI/CI 接线（P1-5）、试点页面挂 `LTextKeyRef`（P1-6）。
- **`events[]` 恒空**：事件真相仍在代码；等引用对账（P1-4）产生消费者再填。
- **生成器域遍历未统一**：`LiteCodeGen.BindSearch`（生成代码）与 `UiBindSurfaceWalker` 仍两份语义；本批只保证 hash/契约/校验同源。
- **schema 正则未在生成期强制**（`bindName`/`key` 文法）：归 P1-4 校验规则；真实页面当前全部合规。
- **既有页面未挂 `LTextKeyRef`**：契约 `text[]` 为空是试点前基线；本批不写 prefab。
- **`tbuiform` 无 `cacheStrategy` 列**：契约恒写默认 `Lru`。
- **工作区未提交**：本批改动未自行 commit（工作区含他批在途改动）；提交与分批由用户裁决。

## 文件清单

- 运行时：`Assets/LiteClient/UI/Localization/LTextKeyRef.cs`、`Assets/LiteClient/UI/Localization/UiStaticTextBinder.cs`、`Assets/LiteClient/UI/UIForm.cs`
- 编辑器：`Assets/LiteGame/Editor/UiTools/Binding/UiBindSurfaceWalker.cs`、`Assets/LiteGame/Editor/UiTools/Binding/UiBindGenerationHash.cs`、`Assets/LiteGame/Editor/UiTools/Text/UiLTextTable.cs`、`Assets/LiteGame/Editor/UiTools/Text/LTextKeyRefInspector.cs`、`Assets/LiteGame/Editor/UiTools/Contract/{UiPageContract,UiPageMetadata,UiContractJson,UiContractGenerator,UiContractDiff}.cs`
- 测试：`Assets/Tests/EditMode/UiStaticTextEditModeTests.cs`、`Assets/Tests/EditMode/UiContractGeneratorEditModeTests.cs`、`Assets/Tests/EditMode/UiContractDiffEditModeTests.cs`
- 工装：`scripts/gate/unity-tests.py`
- 文档：`Docs/设计文档/客户端/UI系统/UI系统总设计.md`（§4.1/§4.2）、`Docs/施工进度/UI系统线.md`（新卷）、`Docs/施工进度/README.md`（索引行）
- 过程：`.workflow/active/ui-p1-contract/**`

## 后续建议

- **下一批 P1-2**：先落 `WidgetSpec`/`UiWidgetManifest`/`UiPageManifest` 模型与生成器；`UiPageMetadata` 已留 Owner/DesignRef 投影接缝，manifest 可直接填充。
- **P1-4 校验规则**：`UiContractDiff` 的 `Added/Removed/Renamed/Changed/Visual/SourceOnly` 可直接作为规则输入；建议同批把 schema 正则（`bindName`/`key`）接进生成期或校验期，二者选一，避免两套判定。
- **P1-5 工具接线**：`ui.contract`/`ui.contract-diff` 接 Hub 时同时补 CLI 入口，避免只有 C# API；生成物目录 `Assets/LiteGame/Editor/UiTools/Contracts/` 首次生成自动创建。
- **P1-6 试点**：`Page_Login`（6 个 BindName、无模板）挂 `LTextKeyRef` 验静态文案端到端；`Page_Lobby`（有 `Tmp_*` 依赖）验 `templates[]` 与 diff。
- **生成器改造批**：让 `BindSearch` 与 `UiBindSurfaceWalker` 收敛为一套遍历，彻底消掉两份语义（本批已登记为遗留）。
- **门禁推广**：`scripts/gate/unity-tests.py` 的「源码新于程序集则重编译 + 编译失败判红」建议同步到其他 Unity 测试入口（PlayMode 门禁等）。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-22T21:01:41+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-22T21:04:44+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-22T21:05:30+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-22T21:05:32+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-22T21:14:50+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-22T22:03:58+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-22T22:19:57+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-22T22:20:02+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-22T22:20:39+08:00 · 完成「回写与收尾」→ 完成
