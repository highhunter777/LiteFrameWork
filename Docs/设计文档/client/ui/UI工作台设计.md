# UI 工作台设计

> 状态：设计稿（2026-10-10）——用户裁决：**独立工作台场景**（二选一：场景 vs F11 目录页挂现有链）；场景与面板为 Unity 管线手制，驱动件归 DevHUD 批
> 范围：dev-only 手测工作台——不必跑完整业务流程即可开/关任意已登记页、驱动加载/错误/Toast 反馈面、目检多语言与布局
> 上位：[UI框架总设计](UI框架总设计.md)（真装配、视觉单一来源、反馈面）；[UI测试开发专项设计](../../quality/UI测试开发专项设计.md)（自动化与人工分工）
> 先例：`HUDDesign.unity`（场景内设计 UI）；`SimSandbox` + `LiteGame.DevHUD`（场景级调试件、标注"不担产品职责"、零产品程序集改动）
> 事实依据（2026-10-10 代码核对）：`GameEntry.TakeContainer` 公开给开发工具；`StageMachine.Current/Started`；`UIService.CloseAsync/CloseAllOpen/IsOpen`；`UINavigationController.GoAsync`；`UiInput.EnsureEventSystem`（运行时幂等建位）；`LiteGame.DevHUD.asmdef` 已引 `LiteGame.App/UI`

## 0. 定位与边界

- 工作台 = **人工目检与制作迭代**载体：眼睛与手感归它；断言与证据归 L2 自动化（夜间门禁）——两者互不替代（[UI测试开发专项设计 §1.1](../../quality/UI测试开发专项设计.md) 三类测试职责）。
- **不旁路装配**：场景跑**真装配根**（GameEntry → ClientHost → 全模块 → 流程机 Launch→Patch→Preload→Main 真跑），只换"落点"——开工作台面板而非玩法页/业务首屏。禁止自建 UIService/假加载器（"替身让真链路缺陷隐形"教训，[UI-U2](../../../施工进度/UI-U2.md)）。
- 面板本体 = **场景内 UI + 驱动件**，**不是 tbuiform 页面**（"独立工作台场景"裁决的直接读法）：免去 dev 页进 `Assets/UI/Screens` 收集路径与表行的生产污染（BaselineA/B 在 Screens 内属框架期先例，不构成业务期路径——业务 UI 不走代码生成，2026-10-04 裁决）。
- 默认仅编辑器 Play 使用；是否进开发档 Build Settings、发布剥离口径随 DevHUD/SimSandbox 现状登记（见 §5），本设计不另开机制。

## 1. 场景与装配

- 场景 `Assets/Scenes/UiWorkbench.unity`（Unity 管线手制）：
  - 最小件 = **GameEntry（引导件）+ 主相机**（对照 `Test.unity` 启动场景同款最小形态）；输入底座 `[UIEventSystem]` 由 `UiInput.EnsureEventSystem()` 运行时幂等建位，无需场景 EventSystem 对象。
- 驱动件 `UIWorkbenchDriver.cs` 落 `Assets/LiteGame/DevHUD/`（既有 dev 程序集 `LiteGame.DevHUD`，已引 `LiteGame.App/UI`——SimSandbox 同款；**零产品程序集、零产品码分支**）：
  - **取根**：`GameEntry.TakeContainer()`（公开口，注释明言"引导与开发工具取得已装配的根容器"）——Update 轮询至装配完成。
  - **就绪门控**：`StageMachine.Current == ProcedureId.Main` 后按钮启用（= 内容已初始化，开页安全）；未就绪置灰 + 状态行提示（如"启动中：Patch/Preload"）。
  - **真链路照跑**：编辑器下 Patch 走本地通道（未配 CDN = 无网络依赖离线形态，即快）；Main 阶段仍会加载玩法场景（TrainingGround）——面板不阻止（无害背景）；不做场景名门控跳过（保持零产品码分支；如成负担随 W2 评估）。
  - 说明：`ProcedureMain` 首屏自动打开随五页批C 接线后，工作台场景会继承该行为——面板一键可关；如需隔离随 W2 评估。

## 2. 面板结构（场景内）

建议名过[制作规范 §9](UI制作规范.md) 文法；dev 件不在 §9 强制面，词条从简（需要时按 §1 登记）：

```
WorkbenchPanel                 场景面板根（Canvas + CanvasGroup；排序低于 UIRoot 语义层区间，页面上浮于它）
├─ Cot_Forms                   表单目录容器
│  ├─ Clone_FormRow            行模板（非激活；序列化引用接线：Button_Open / Button_Close / Txt_FormId）
│  └─ [运行时行：按 tbuiform 表逐行实例化]
├─ Cot_Demos                   反馈面演示组
│  ├─ Button_Loading           开加载阻断（BeginLoading）
│  ├─ Button_Progress          进度上报（LoadingScope.Report——随加载页批A 接口）
│  ├─ Button_EndLoading        收场（作用域 Dispose）
│  ├─ Button_Error             错误弹窗（ShowErrorAsync）
│  └─ Button_Toast             Toast（ShowToast）
└─ Txt_Status                  状态读数（流程阶段 / IsLoading / 导航计数（Executed/QueuedCount）/ 最近操作结果）
```

- **目录行来源 = 真表 `Tbuiform.DataList`**（id/layer/全屏）——五页（11-15）登记后**自动出现、零改码**；行克隆走 `Clone_` 条目模板模式（实例化模板行，不建结构——与列表内核同判）。
- 打开/关闭：`UINavigationController.GoAsync(id)` / `UIService.CloseAsync(id)`；系统面（201/202/203）不做目录开关，走演示组服务面（统一入口纪律：反馈面经 `FeedbackService`）。
- `Button_Progress` 依赖[通用加载页设计](通用加载页设计.md) §3 的 `LoadingScope.Report`；未接线前该钮占位（点击记日志"待接线"）。

## 3. 消费计划

| 消费 | 用法 | 批 |
| --- | --- | --- |
| 加载页批A/B 手测 | 演示组：开阻断 → 进度上报 → 收场；Back 取消（编辑器 ESC）目检 | W0/W1 |
| 五页批C 验收 | 目录一键开/关/复用目检（登录/大厅/房间/结算/设置） | 随五页批 |
| 控件与动效迭代 | 打开 Baseline/ListScreen 先例页（204/205/206）看效果 | 常备 |

## 4. 与测试面的分工（不重复造）

- 断言、取消/超时/代次、租约与清理对称 → L2 EditMode/PlayMode（夜间门禁）；工作台**不做自动断言、不进 `scripts/gate`**。
- 视觉人工档的现实载体：[UI测试开发专项设计 §4.4](../../quality/UI测试开发专项设计.md) 的 V1/V2 人工确认可用本场景进行；V0 失败诊断截图仍在用例侧，门禁口径不变。

## 5. 边界与防锈

- **dev-only**：不担产品职责（SimSandbox 同款标注）；发布剥离随 DevHUD/SimSandbox 现状口径（现状 `Assets/Scenes` 收集组整目录收集，Boot/GuideScene/SimSandbox 同列——工作台继承同口径，生产"开发内容排除"职责在发布管道，随发布批核对）。
- **防锈登记**：owner = 客户端 UI 线；失效条件 = ① L2 PlayMode 已覆盖其手测场景，或 ② 制作线出现更顺的页面预览工具；到期未用即退役（防 Boot/GuideScene 式历史场景累积；[代码地图](../../开发导航/代码地图.md) 已标"历史场景"）。
- 不新增第二个"打开任意页"入口：F9/F10 测试对局入口语义不变；本件只管 UI 页。

## 6. 批次

| 批 | 交付 | 依赖 |
| --- | --- | --- |
| W0 场景+驱动 | 场景（GameEntry+相机+面板）+ `UIWorkbenchDriver`：取根/门控、目录行克隆与开关、演出组（Loading/错误/Toast） | Unity 管线手制；演示启动用现有 `BeginLoading(message)` |
| W1 进度演示接线 | `Button_Progress` 接 `Report`；状态行补加载文案回读 | 加载页批A |
| W2（可选，随真实需求） | 语言切换行、状态注入（空列表/长文本）、场景名门控跳过玩法场景、发布剥离口径核对 | — |
