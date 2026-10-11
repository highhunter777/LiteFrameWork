# UI工作台W0（场景+驱动）

《UI工作台设计》W0 执行（2026-10-11）。dev-only 人工目检工作台：不跑完整业务流程即可开/关任意已登记页、驱动反馈面、目检状态。

## 状态

**已完成（Unity 编译证据齐全；实机手测待本机——dev 件不做自动断言，设计 §4）**。

## 交付

| 面 | 落点 |
|---|---|
| 场景 | `Assets/Scenes/UiWorkbench.unity`（原 HUDDesign 改造：改名保持 GUID、旧 HUD 树已清、`GameEntry`+主相机就位；不在 Build Settings——编辑器 Play 使用，同 SimSandbox 口径） |
| 驱动件 | `Assets/LiteGame/DevHUD/UIWorkbenchDriver.cs`——**不旁路装配**：Update 轮询 `GameEntry.TakeContainer()`，服务容器 Resolve（Navigation/UIService/FeedbackService/IConfigService/流程机）；就绪门控 = `ConfigService.Loaded` ∧ `StageMachine.Current == Main`（未就绪五钮置灰+状态行提示）；真链路照跑（Patch 本地通道、Main 加载玩法场景不阻止） |
| 面板 | `WorkbenchPanel`（Canvas Overlay sortingOrder -100——低于页面语义层；1920×1080 match .5；含 GraphicRaycaster，按钮可点）：`Cot_Forms` 目录列 + `Clone_FormRow` 行模板（Button_Open/Button_Close/Txt_FormId）+ `Cot_Demos` 演示组五钮 + `Txt_Status` 状态行 |
| 目录行 | 运行时按真表 `ConfigService.Tables.Tbuiform.DataList` 逐行克隆——**五页登记后自动出现、零改码**；系统面（201/202/203）行置灰（反馈面走演示组——统一入口纪律）；开= `UINavigationController.GoAsync(id)`、关= `UIService.CloseAsync(id)`（UniTaskVoid 包装，失败入状态行） |
| 演示组 | Loading 开（`BeginLoading`，作用域存字段）/ 进度上报（**占位**——`LoadingScope.Report` 随加载页批A 接线）/ Loading 收场（Dispose）/ Error 弹窗 / Toast |
| 状态行 | 0.5s 节流：流程阶段 / 就绪 / IsLoading / 最近操作结果 |
| asmdef | `LiteGame.DevHUD` 补三引用：`UniTask`、`Unity.TextMeshPro`、`Luban.Tables`（表行枚举+TMP+异步） |

## 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | `completed / failed:false / 0 错误`（三轮收敛：asmdef 引用名/嵌套 LoadingScope/TMP 实例方法——均即时修正） |
| 面板构建 | run_script `DRIVER wired / SAVED`（SerializedObject 接线 9 字段；幂等重跑清旧重建） |
| 场景 | UiWorkbench.unity 含 WorkbenchPanel/GameEntry/Main Camera（pipeline rename GUID 保持） |

## 边界与防锈（设计 §4/§5 摘要）

- 工作台**不做自动断言、不进 gate**——断言与证据归 L2（夜间）；实机手测待本机（编辑器 Play 打开本场景即跑真装配链）。
- dev-only 不担产品职责；**不做整场景运行时保存**、prefab 回写工具归 W2（2026-10-11 裁决见设计 §2/§5）。
- 失效条件：① L2 PlayMode 覆盖其手测场景，或 ② 出现更顺的页面预览工具——到期退役。
