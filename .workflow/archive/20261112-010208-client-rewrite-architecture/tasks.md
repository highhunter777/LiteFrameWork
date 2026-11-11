# 实施任务清单：客户端架构重写（不继承旧设计）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 架构设计文档与旧文档处置 | - | 完成 | Docs/设计文档/架构/客户端重写架构总设计.md 落地；设计索引登记；旧文档处置清单明确；独立评审 PASS；Doc 门禁通过 | python scripts/gate/test.py --suite Doc | Docs/设计文档/架构/客户端重写架构总设计.md 落地；设计索引登记；旧文档状态行/处置清单；Doc 门禁 PASS（doc-lint/comment-lint OK） |
| T2 | Next 子树 + AOT/hot 程序集 + BootNext 启动骨架 | - | 完成 | Assets/LiteGame/Next 子树编译；GameEntryNext→ClientApp→占位页；静态扫描无旧机制；EditMode 启动用例通过 | python scripts/gate/unity-tests.py --mode editor --filter ClientNextBootTests | Next 子树+3 asmdefs+BootNext 场景；Unity 编译 compilationFailed=false/0 console error；BootNext 实跑日志 Platform→ClientApp→UiRunti… |
| T3 | UiRuntime + PageBase/PageSpec + 占位/Login/Error 页面 | T2 | 完成 | UiRuntime 打开/关闭/栈/模态/Back/缓存/输入阻断；PageBase 绑定；三个页面可用；EditMode 用例全绿 | python scripts/gate/unity-tests.py --mode editor --filter ClientNextUiRuntimeTests | UiRuntime/PageBase/PageSpec/PageHandle/BindName 实现；Placeholder/Login/Error 三页 prefab + EditorPageFactory；BootNext 实跑打开真… |
| T4 | ClientApp/AppFlow/Lifetime + 最小 Platform + 假账号登录 | T3 | 完成 | ClientApp 显式构造；AppFlow 打开 Login/占位；取消/关闭；AppFlow/Lifetime 用例全绿 | python scripts/gate/unity-tests.py --mode editor --filter ClientNextAppFlowTests | AppFlow/ClientApp/AccountFeature 接线；BootNext EventSystem；ClientNextAppFlowTests 8/8、ClientNextBootTests EditMode 2/2 + … |
| T5 | 架构守卫 + 短链路测量 + 冷启动 | T3, T4 | 完成 | 静态守卫覆盖 Next 路径/旧 token/功能边界；S1-S4 触碰面记录；冷启动子代理 ≤2 文件 0 反问 | python scripts/gate/test.py --suite L1 --profile PullRequest | Next 路径单源豁免 AR1/AR2；dir-flat OK/namespace 893 baseline no growth/change-unit 404 no new unit；client-next-change-budget … |
| T6 | P0 验证与证据 | T1, T2, T3, T4, T5 | 完成 | 编译、EditMode、PlayMode BootNext、静态扫描通过；证据写入 build-log | python scripts/gate/unity-tests.py --mode editor --filter ClientNext | Unity compile 0 error；EditMode ClientNext 26/26；PlayMode Boot 1/1；L1 1895/0；Doc PASS；dir-flat/namespace/change-unit/cli… |

## 任务详情

### T1 · 架构设计文档与旧文档处置

- 文件: Docs/设计文档/架构/客户端重写架构总设计.md, Docs/设计文档/README.md, Docs/归档记录/README.md

### T2 · Next 子树 + AOT/hot 程序集 + BootNext 启动骨架

- 文件: Assets/LiteGame/Next/Bootstrap, Assets/LiteGame/Next/Runtime/App, Assets/LiteGame/Next/Runtime/Platform, Assets/Scenes/BootNext.unity, Packages/com.litegame.framework/Next, arch/units.json, arch/client-roots.json, scripts/gate/hot-boundary-check.py

### T3 · UiRuntime + PageBase/PageSpec + 占位/Login/Error 页面

- 文件: Assets/LiteGame/Next/Runtime/Platform/Ui, Assets/LiteGame/Next/Runtime/Features/Account, Assets/LiteGame/Next/Runtime/Platform/Ui/Content, Assets/Tests/EditMode/ClientNext

### T4 · ClientApp/AppFlow/Lifetime + 最小 Platform + 假账号登录

- 文件: Assets/LiteGame/Next/Runtime/App, Assets/LiteGame/Next/Runtime/Platform/Content, Assets/LiteGame/Next/Runtime/Platform/Settings, Assets/LiteGame/Next/Runtime/Features/Account

### T5 · 架构守卫 + 短链路测量 + 冷启动

- 文件: Tests/LiteFramework.Core.Tests/ClientNextArchitectureTests.cs, scripts/gate/client-next-change-budget.py, arch/client-roots.json

### T6 · P0 验证与证据

- 文件: .workflow/active/client-rewrite-architecture/build-log.md

