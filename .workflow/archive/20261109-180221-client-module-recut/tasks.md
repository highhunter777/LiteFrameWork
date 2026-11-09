# 实施任务清单：客户端按功能/行为重切模块（方案）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 9/9（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | Phase 0 基线快照与 meta 债登记 | - | 完成 | 记录 git status --short -- Assets Packages Tests Server 快照（当前 8 M + 5 ?? WIP）；相对快照无新增；L1/arch/domain/doc/build-hash --check-shared 基线记录；BuildHash meta 红登记到技术债+进度卷（grep BuildHash meta stale）。 | git status --short -- Assets Packages Tests Server; arch/domain/doc/build-hash --check-shared/hot-boundary; L1 基线取自 3a2d3b32（1972 绿）；当前 7 红为新增红测（预期） | HEAD 3a2d3b32/0283a9e3；生产树干净；arch 33/6/12/28、domain 12、doc 0、shared 5937ac51、hot 23/66 绿；L1 1972 基线（3a2d3b32 前），当前 7 红=… |
| T2 | friend 允许集单一来源 + L1 守卫 | T1 | 完成 | 9 个 Modules/* AssemblyInfo 共 55 条与 arch/units.json friends 一致；未登记即红；AssemblyInfo 归属=最近 asmdef 目录、不递归子单元；合成负例红。 | dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~ModuleFriendAllowlistTests --nologo; python scripts/gate/arch-check.py --check | arch/units.json +73 行，9 单元 friends 合计 55（Battle4/Composition7/Config1/Dev8/Map7/Meta7/Procedure7/Session7/UI7）；ModuleFr… |
| T3 | 零使用边脚本/基线 + toolchain 注册 | T2 | 完成 | zero-use-edges.py 复用 domain-matrix 口径（去注释/字符串、depth=1 顶层类型、出现次数）；arch/zero-use-edges.json 基线；quality.zero-use-edges 登记 toolchain.json；.workflow/config.json 挂 command；--check 只减不增；合成负例红。 | python scripts/gate/zero-use-edges.py --check; python scripts/toolchain.py docs --check | zero-use-edges.py（复用 domain-matrix 口径，--write/--check，子集语义）；arch/zero-use-edges.json 54 条基线；toolchain.json quality.zero… |
| T4 | ISettingsPagePort + ProductSettingsAdapter + 页面上下文 | T1 | 完成 | ISettingsPagePort 28 成员（15 读+13 写/动作，无 Apply）；ProductSettingsAdapter 在 App；IPageContext.Settings/PageLogicBase.Settings；adapter 创建点在 BindProductSettings 之后/惰性；Unity 重编译 0 错。 | python scripts/gate/arch-check.py --check; Unity 重编译 0 错 | 6 授权文件 + 2 .meta；ISettingsPagePort 28 成员（15 读+13 写/动作，无 ProductSettings/Apply）；ProductSettingsAdapter 在 App；IPageContex… |
| T5 | Page_SettingsLogic 10 处替换 + FrameRateIndex + 空端口守卫 | T4 | 完成 | 页面代码 ProductSettings.Current=0；FrameRateIndex(ISettingsPagePort)；空端口 OnShow 早退 + 10 处 ?.；Ctx.Meta 仍用于 Version/AccountId/ServerUrl/LeaveMatch；Unity 重编译 0 错。 | rg ProductSettings.Current Assets/LiteGame/Modules/UI/Pages; Unity 重编译 0 错 | Page_SettingsLogic 10 处替换（+55/-26，305→334）、FrameRateIndex(ISettingsPagePort)、空端口 ApplyPortlessState+12 控件禁用、OnShow 早退、1… |
| T6 | UI asmdef 去 Settings + units/domain/allowlist 收口 | T5 | 完成 | UI asmdef 不再引 Settings；units.json UI refs 去 Settings；domain-matrix 去 Ui→Settings；allowlist 删 ProductSettings；arch-check/domain-matrix/L1 绿。 | python scripts/gate/arch-check.py --check; python scripts/gate/domain-matrix.py --check; python scripts/gate/test.py --suite L1 --profile PullRequest | UI asmdef 去 Settings；units UI refs/domainEdges 去 Ui→Settings（28→27）；CrossModule allowlist 15→14；Docs/18 生成物 Ui→Settings… |
| T7 | Settings 页 PlayMode 用例 | T5 | 完成 | 新增 SettingsPagePlayModeTests：fake ILocalizationService、真 Page_Settings.prefab、Dropdown/Slider 事件、空端口 10 处 ?. + OnShow 早退；打开/改画质/改音量/Back 保存/空端口绿。 | python scripts/gate/unity-tests.py --mode playmode --filter SettingsPagePlayModeTests | SettingsPagePlayModeTests 5/5（PM_SET_1..5）；真 Page_Settings.prefab + 真 UIService/PageContext；fake ILocalizationService +… |
| T8 | friend/零使用边棘轮 | T2, T3, T6 | 完成 | friend 55；零使用边基线只减不增；白名单 15→14 已在 Phase 1；记录数字。 | python scripts/gate/zero-use-edges.py --check; python scripts/gate/test.py --suite L1 --profile PullRequest | zero-use-edges --write 收紧 54→53（删除 UI→Settings）；--check 53/53；unittest 5/5；friend 55；CrossModule allowlist 14（ProductSe… |
| T9 | 文档回写与审计登记路径核销 | T4, T5, T6, T7, T8 | 完成 | 说明 03/08/10/14/15/17/18/19/20、UI 系统/UI 工具链/UI API/DI/设置/技术债/进度卷事实性回写；章程类只记不变/待另立设计；审计路径核销；doc-lint/toolchain docs 绿。 | python scripts/gate/doc-lint.py; python scripts/toolchain.py docs --check | 19 份 Docs 回写：说明 03/08/10/14/15/17/20/README、设计 设置/UI总设/UI API/DI/热更/Lua退出/README、审计 技术债+归档、进度 §58；doc-lint 0；toolchain … |

## 任务详情

### T1 · Phase 0 基线快照与 meta 债登记

- 文件: Docs/审计/客户端/登记/技术债.md, Docs/施工进度/客户端重构线.md, .workflow/active/client-module-recut/build-log.md

### T2 · friend 允许集单一来源 + L1 守卫

- 文件: arch/units.json, Tests/LiteFramework.Core.Tests/ModuleFriendAllowlistTests.cs

### T3 · 零使用边脚本/基线 + toolchain 注册

- 文件: scripts/gate/zero-use-edges.py, arch/zero-use-edges.json, scripts/gate/toolchain.json, .workflow/config.json

### T4 · ISettingsPagePort + ProductSettingsAdapter + 页面上下文

- 文件: Assets/LiteGame/Modules/UI/Abstractions/ISettingsPagePort.cs, Assets/LiteGame/App/PageServices/ProductSettingsAdapter.cs, Assets/LiteGame/Modules/UI/Abstractions/IPageContext.cs, Assets/LiteGame/Modules/UI/Pages/PageLogicBase.cs, Assets/LiteGame/Modules/UI/Pages/PageContext.cs, Assets/LiteGame/App/AppComposition.cs

### T5 · Page_SettingsLogic 10 处替换 + FrameRateIndex + 空端口守卫

- 文件: Assets/LiteGame/Modules/UI/Pages/Screens/Settings/Page_SettingsLogic.cs, Assets/LiteGame/Modules/Settings/Model/ProductSettings.cs, Assets/LiteGame/App/AppComposition.cs

### T6 · UI asmdef 去 Settings + units/domain/allowlist 收口

- 文件: Assets/LiteGame/Modules/UI/LiteGame.Modules.UI.asmdef, arch/units.json, Docs/说明/18-域间引用矩阵.md, Tests/LiteFramework.Core.Tests/CrossModuleContractLocationTests.cs

### T7 · Settings 页 PlayMode 用例

- 文件: Assets/Tests/UI/PlayMode/UI/SettingsPagePlayModeTests.cs

### T8 · friend/零使用边棘轮

- 文件: arch/zero-use-edges.json, Tests/LiteFramework.Core.Tests/ModuleFriendAllowlistTests.cs

### T9 · 文档回写与审计登记路径核销

- 文件: Docs/说明/03-客户端框架与运行时.md, Docs/说明/08-UI与页面逻辑层.md, Docs/说明/10-测试与门禁.md, Docs/说明/14-代码地图与根目录.md, Docs/说明/15-工具链手册.md, Docs/说明/17-全项目数据流.md, Docs/说明/18-域间引用矩阵.md, Docs/说明/19-模块发行矩阵.md, Docs/说明/20-架构单元模型.md, Docs/设计文档/客户端/UI系统/UI系统总设计.md, Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/UI逻辑API参考.md, Docs/设计文档/架构/客户端依赖注入专项设计.md, Docs/设计文档/客户端/UI系统/设置系统专项设计.md, Docs/审计/客户端/登记/技术债.md, Docs/施工进度/客户端重构线.md

