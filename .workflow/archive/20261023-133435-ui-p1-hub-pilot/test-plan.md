# 测试计划：UI 系统 P1-5/P1-6

## 测试策略

- **先红后绿**：新增 `UiToolchainWiringEditModeTests`，只断言外部可观察面（工具清单、契约产物、页面清单、Lua 文案写者），确保未实现时以断言失败暴露缺口。
- **生成器/批处理单测**：`UiContractBatch` 新增后，在实现任务内补直接类型用例（临时输出目录）——先红阶段不能用直接调用的原因：类型尚不存在会导致编译错误，而红测要求"断言失败而非编译/环境错误"。
- **回归面**：契约生成、契约 diff、控件清单、页面清单、静态文案、校验规则、工具目录七个既有夹具。
- **静态门禁**：`toolchain.py check` / `docs --check` / `doc-lint.py` / L1。
- **运行期**：Play Mode 打开登录页，读三个静态节点 TMP 文本并切换语言复查。
- 真实数据不造假：契约/清单读真实 `tbuiform` 与真实 prefab；测试写入一律用临时目录。

## 用例清单

| 用例 | 夹具 | 断言 | 预期（红阶段） |
| --- | --- | --- | --- |
| 工具面_四个契约与清单动作已登记且可执行 | UiToolchainWiringEditModeTests | 四个 id 存在、kind=action、plane=editor、Execute 非空；写入者声明 writes+outputs | 失败（未登记） |
| 试点页_Page_Login_静态文案三条进入契约 | 同上 | 契约 text[] 恰 3 条：`Common.Settings` / `UI.Login.GuestEnter` / `UI.Login.Title` | 失败（text 为空） |
| 试点页_Lua_不再动态写静态文案 | 同上 | 三处 `SetTextKey` 消失；`SetTextKeyArgs("Txt_Version")`、`SetTextKey("Txt_Status")` 保留 | 失败（仍动态写） |
| 契约基线_每个可生成页已入库 | 同上 | 清单内每个 Page_/Hud_/Tmp_ 页有契约文件；`UIMain` 无契约 | 失败（无文件） |
| 页面清单_契约入库后contractPresent为真 | 同上 | 条目 `contractPresent=true` 且路径文件存在 | 失败（全 false） |
| 批量生成_根名不符跳过并告警_其余页面入库 | 同上（实现期补写） | 临时目录生成 11 份；告警含 `UIMain`；无文件写到默认目录 | 实现后绿 |
| 批量diff_无基线告警_破坏性变更判失败 | 同上（实现期补写） | 空基线 → 告警；删除控件后 → Breaking 且失败 | 实现后绿 |
| 生成物确定性_重复生成零diff | 同上（实现期补写） | 连续两次生成字节一致 | 实现后绿 |
| 契约生成_多行登记取首行并告警 | UiContractGeneratorEditModeTests（扩展） | `Tmp_Dialog` 两行 → formId=201 且 Warnings 含多行说明 | 实现后绿 |
| 页面清单对账_起页后自动登记（注入路径） | 同上（实现期补写） | 注入临时清单路径后，Create 钩子写入条目；失败不抛 | 实现后绿 |
| 回归 | UiContractGenerator / UiContractDiff / UiPageManifest / UiWidgetManifest / UiStaticText / UiValidationRules / EditorToolCatalog | 全绿 | 绿（现状基线） |

## 预期失败证据

先红命令：`python scripts/gate/unity-tests.py --mode editor --filter UiToolchainWiringEditModeTests --timeout 600`

实测（2026-10-23）：**5/5 failed**，全部为断言失败：

- `ui.contract 未在 scripts/gate/toolchain.json 登记`
- `试点页静态文案应恰为三条… actual is <System.String[0]>`
- `Txt_Title 已挂 LTextKeyRef，Lua 不得再写（双写分叉）`
- `Page_Lobby 缺少契约基线：Assets/LiteGame/Editor/UiTools/Contracts/Page_Lobby.contract.json`
- `Page_Lobby 的 contractPresent 仍为假`

`wf verify --red` 证据见 `evidence/red.json`（内含各红命令退出码与输出尾部）。

## 运行方式

```powershell
# 先红 / 定向绿：新夹具
python scripts/gate/unity-tests.py --mode editor --filter UiToolchainWiringEditModeTests --timeout 600

# 回归（受影响面）
python scripts/gate/unity-tests.py --mode editor --filter UiContractGeneratorEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiContractDiffEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiStaticTextEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiValidationRulesEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter EditorToolCatalogEditModeTests --timeout 600

# 工具链与文档
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
python scripts/gate/test.py --lane L1 --profile PullRequest
```

运行期验证：Play Mode 启动至登录页，读取 `Txt_Title` / `Txt_GuestLogin` / `Txt_Settings` 的 TMP 文本；`Bridge.Text.SetLocale("en")` 后复查；并确认 `Txt_Status`/`Txt_Version` 仍由 Lua 写入。

> 环境备注：本机 `run_shell_command` 不可用（powershell 缺失）；Unity 测试经后台进程启动后轮询日志，避免编辑器主线程被同步子进程阻塞。
