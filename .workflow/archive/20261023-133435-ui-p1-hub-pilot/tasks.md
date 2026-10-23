# 实施任务清单：UI 系统 P1-5/P1-6：Hub/CLI 接线与试点页静态文案

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 契约批处理服务 UiContractBatch（生成/对账 + 根名分类 + 多行提示） | - | 完成 | 新增 UiContractBatch：一次读表、按 prefab 去重、根名非 Page_/Hud_/Tmp_ 显式跳过并告警、多行登记告警取首行；Generate 写确定性契约、Diff 复用 UiContractDiff 并汇总破坏性变更；输出目录可注入 | unity-tests --filter UiToolchainWiringEditModeTests | UiContractBatch.Generate 11 页 / 跳过 UIMain / 告警 2（UIMain + Tmp_Dialog 多行）；重复生成 11/11 逐字节一致；新夹具 8/8 |
| T2 | 四个契约/清单动作接线：handler + toolchain.json + 工具索引 | T1 | 完成 | ui.contract/ui.contract-diff/ui.widget-manifest/ui.page-manifest 注册为 action（plane=editor，writes/outputs/docs/menu 合法）；Hub 目录可打开执行；toolchain.py check 与 docs --check 通过；无孤儿 handler | python scripts/toolchain.py check; python scripts/toolchain.py docs --check | 四动作注册 + toolchain.json 登记（order 120-150）+ 工具索引重生成；toolchain.py check 0 error（1 条既存告警）、docs --check 通过；EditorToolCatalog… |
| T3 | 起页后页面清单对账接线（UiPageScaffold.Create） | T1 | 完成 | Create 成功后调用页面清单对账并落盘；失败只记 result.Log 且不回滚页面；openStage=false 同样对账；对账逻辑可注入清单路径供测试 | unity-tests --filter UiToolchainWiringEditModeTests | Create 后 ReconcilePageManifest 落盘；注入 Assets/Tests/TempUiWiring 路径用例通过（起页对账_注入清单路径可落盘） |
| T4 | 试点页 Page_Login 静态文案挂 key 并移除 Lua 动态写 | - | 完成 | Txt_Title/Txt_GuestLogin/Txt_Settings 挂 LTextKeyRef（key=UI.Login.Title/UI.Login.GuestEnter/Common.Settings）；Page_Login.lua 删除这三处 SetTextKey；Txt_Version/Txt_Status 保持动态；prefab 经 PrefabUtility 修改且零节点/引用变化 | unity-tests --filter UiToolchainWiringEditModeTests; unity-tests --filter UiStaticTextEditModeTests | Page_Login 三节点挂 LTextKeyRef（UI.Login.Title/UI.Login.GuestEnter/Common.Settings）；Lua 删三处 SetTextKey、保留 Version/Status；运行… |
| T5 | 生成物入库与清单同步（11 契约 + 控件清单 + 页面清单） | T1, T2 | 完成 | Contracts/ 11 份契约入库且重复生成零 diff；UiWidgetManifest.json 重生成 30 控件；UiPageManifest.json contractPresent 转真且 12 页/13 行不变 | python scripts/toolchain.py check; unity-tests --filter UiPageManifestEditModeTests | 11 契约入库、UiWidgetManifest 30 控件 0 告警、UiPageManifest 12 页 13 行 contractPresent=true；Page_Login 契约 text=3 无时间戳 |
| T6 | 先行测试：工具面/批处理/diff/试点契约与运行期文案 | T1, T2, T3, T4 | 完成 | 新增 UiToolchainWiringEditModeTests 覆盖四动作登记与可执行、批量生成（含 UIMain 跳过）、diff 无基线与破坏性、试点 text[] 三条、起页对账；先红为断言失败；运行期验证登录页中英文案 | wf verify --red; unity-tests --filter UiToolchainWiringEditModeTests | 先红 5/5 失败（断言）→ 8/8 通过；回归 catalog 12/12、contract 10/10、diff 5/5、page-manifest 15/15、widget 11/11、static-text 6/6、validat… |
| T7 | 门禁与文档回写 | T2, T3, T4, T5, T6 | 完成 | 定向 EditMode 夹具（新夹具 + catalog + contract/page/widget/static-text/validation）+ toolchain check/docs + doc-lint + L1 全绿；新夹具命令登记进 .workflow/config.json 的 commands/red_commands；工具链专项 §9 状态与 §5 口径更新；施工进度 UI系统线 第5章；审计 L248/L251 回写；README/索引（如需） | python scripts/gate/test.py --lane L1 --profile PullRequest; python scripts/gate/doc-lint.py | L1 PASS 1819；Doc 0 violation；L2 meta PASS 14216；restore PASS；toolchain unittest 222 OK；设计 §5.2.1/§8.4/§8.5/§9.1 更新；进度卷第… |

## 任务详情

### T1 · 契约批处理服务 UiContractBatch（生成/对账 + 根名分类 + 多行提示）

- 文件: Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs

### T2 · 四个契约/清单动作接线：handler + toolchain.json + 工具索引

- 文件: Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs, scripts/gate/toolchain.json, Docs/工具链/工具索引.md

### T3 · 起页后页面清单对账接线（UiPageScaffold.Create）

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageManifestSync.cs

### T4 · 试点页 Page_Login 静态文案挂 key 并移除 Lua 动态写

- 文件: Assets/ShareResource/UI/Screens/Page_Login.prefab, Assets/LiteGame/Lua/UI/Page_Login.lua

### T5 · 生成物入库与清单同步（11 契约 + 控件清单 + 页面清单）

- 文件: Assets/LiteGame/Editor/UiTools/Contracts/, Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json, Assets/LiteGame/Editor/UiTools/UiPageManifest.json

### T6 · 先行测试：工具面/批处理/diff/试点契约与运行期文案

- 文件: Assets/Tests/EditMode/UiToolchainWiringEditModeTests.cs

### T7 · 门禁与文档回写

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/施工进度/UI系统线.md, Docs/审计/代码质量审计-客户端.md, .workflow/config.json

