# GM 与调试工具分层

> 《GM 与调试工具分层设计》落地批（2026-10-11）。**状态：已完成（附验证证据）**——pipeline 编译 0 错、L1 1572 全绿；L2 夜间、四页手测待本机。

## 一刀：爆头覆写收口（SimTestRules 同型尾巴根治）

`CombatConfig.HeadHitLineDevOverride/HeadshotRadiusDevOverride`（public static 可变浮点，三宏内）被确定性路径直读（`ShootingSystem.cs:194` 爆头判据读 `HeadHitLineLive`、`SimRaycast.cs:80-81` 双柱几何读两个 Live）——与已根治的 SimTestRules 完全同型（§1.3"含 dev 宏内"）。收口：

- `MatchTestRules` 增 `HeadHitLineOverride/HeadshotRadiusOverride` 两字段（**不进 CombatValues 摘要、不可重放**——连续调参进摘要会随试验抖动联机身份）+ 静态解析口 `ResolveHeadHitLine/ResolveHeadshotRadius(rules)`（null ⇒ 恒烘焙导出值）。
- `SimRaycast.RaycastEntities` 三重载加 `MatchTestRules rules = null`（默认参数——L1 用例/既有调用零改动）；`ShootingSystem` 判定与射线判定改注入（:133/:194）。
- 客户端可视化四处传 `SimTestRules.ActiveMatch` 与真实弹道同值：`BattleLaserDriver`×2（激光截停）、`NewInputIntentSource`（瞄准射线）、`SimSandbox`（灰盒沙盒）；`BattleHeadshotDebugDrawer` 黄/红圈同改。
- 写通道：`TestModeRuntime` 增跨局待用值两字段；`ProcedureMatch` 进房种子；GM 面板滑杆/`HeadHitLineTuner`「取当前覆写值/复位」改双落（活动局实例即时生效 + 待用值跨局延续）；`CombatConfig` 静态覆写面整体删除（留单源指向注释）。

## 二刀：GM 工具箱拆页（god panel 闭环）

- `GmPanel`（原 621 行六区一窗，审计"低"条）→ **壳 90 行** + 四页件（`Editor/Gm/`）：`GmTestModePage` / `GmCommandsPage` / `GmScenePage` / `GmConfigDiagnosticsPage`（原③状态+④表查询+⑥产物核对合一），契约 `IGmPage`（OnShow/OnHide/Draw/Poll，轮询仅活动页），取件单源 `GmEditorResolve`（TakeContainer 口径）。
- EditorPrefs 统一前缀 `LiteGame.Gm.*`（旧 `LiteGame.ConfigPanel.*` 键弃用——开发偏好，未迁移）。
- 产物核对路径单源：`TableLayout.TableDataPath(file)` 新增（DataDir+file+".bytes" 拼接口径唯一——装载/服务端/编辑器三面共用），`ConfigService.LoadAsync` 同步改走；GmPanel:570/591 两条"低"债（手拼路径+表名硬编码）随页拆收口（`DefaultTableName` 常量化）。
- 命令化口径如实：传送已走 `ProcedureBattle → SendQueued(TeleportToAimCommand)`（审计+可撤销）——按钮无需改；进/退测试模式是 **pre-Play 请求桥**（容器尚不存在），维持一次性标志；场景操作/表查询未命令化（带参+异步，待议）——三分口径见设计文档 §2。

## 验证

- **pipeline 编译**：unity-pipeline（7801 实例）recompile → 0 错（首轮抓出 `GmScenePage` 缺 `using LiteFramework`，修复后过）。
- **L1 全量**：`test.ps1 -Lane L1 -Profile PullRequest` → **1572/1572 全绿**（含纪律扫描/表守卫）。
- **LiteSim 定向**：335/335（SimRaycast 几何/射击判定/回滚链在第一刀后逐位保持）。
- **BuildHash**：共享源集改动（MatchTestRules/CombatConfig/SimRaycast/ShootingSystem/SimTestRules）已重生成（与资源收集迁移批三的 `04162cb8` 链衔接，终值以收尾复算为准）。

## 待办

- L2（EditMode/PlayMode）夜间流水线；GM 面板四页手测（翻页生命周期/开关/滑杆对局实弹/爆头可视化随动）。
- 场景操作/表查询命令化（带参+异步形态）待议；DevHUD 命令通道待真机需求。
