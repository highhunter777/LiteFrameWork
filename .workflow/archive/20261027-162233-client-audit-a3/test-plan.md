# 测试计划：客户端审计 A3

## 测试策略

- **性质**：行为保持型结构重构，没有可先红的新业务行为。**先行红测 = 结构验收探针**：断言三组协作件已外提、主类不再持有对应域状态（重构的交付物即结构）。
- **行为回归**：既有 L1（FSM 全家 + 纪律扫描）、定向 EditMode（战斗表现/UIBind）、`wf verify` 固定集；`BattleViewRig` 无直接单测，由 **Play 冒烟**（真实启动直入本地测试局 → 装配 → GM 请求桥离场 → 对象销毁/无 NRE）覆盖。
- **边界与错误路径**：取件降级（缺资源/缺服务）、整组材质不注入、逐条音效降级、HUD 孤儿清理、UIBind 未注入语言服务的 fail-fast、FSM 构造校验与初始化守卫。
- **残余风险**：正常离场（ESC→设置页）未由输入模拟驱动；`editor_stop` 宿主关闭路径另作兜底；Play 冒烟不覆盖战斗数值行为。

## 用例清单

| 任务 | 用例/检查 | 预期 |
|---|---|---|
| T1 | 编译；`grep Acquire*Async BattleViewRig.cs`；`BattleDamageNumberEditModeTests`、`BattleAudioDriverEditModeTests` | 无取件方法残留；降级语义不变；通过 |
| T2 | 编译；27 个战斗表现定向 EditMode（BattleHud/BattleCrosshair/BattleRankboard/BattleKillFeed/BattleNetworkHud/BattleRespawnHud/BattleProtectionFx/BattleLaser/BattleDamageNumber/HitFeedbackDispatcher/BattleCombatFx/BattleAudioDriver/BattleRemoteFire/SimView/Tps*/Presentation*）；Play 冒烟 | 相位表逐行保持；`context-created`+`view-attached`；`ended reason=Leave`；`[BattleView]`/`Hud_Battle` 无命中；无 NRE |
| T3 | 编译；`LTextBind`、`LTextTypewriter`、`UiControlListProtocol`、`LTextWiring` EditMode | 公共 API 转发完整；本地化/列表/订阅语义不变 |
| T4 | 编译；`Tests/LiteFramework.Core.Tests` 的 `HierarchicalStageMachineTests`/`PreemptiveHierarchicalStageMachineTests`/`FsmCaptureRestoreTests`/`FsmAuditStatsTests`/`ForceStateWatchdogTests`/`TransitionTableTests` | 构造序/事务/恢复/看门狗/审计全绿 |
| T5 | `python scripts/gate/test.py --suite L1 --profile PullRequest`；`wf verify`；结构探针 exit=0 | 全绿；探针转绿 |
| T6 | `python scripts/gate/doc-lint.py`；登记/归档/批次计划/进度卷 diff | 0/0；计数与条目正确 |

## 预期失败证据

- 探针基线：`python .workflow/active/client-audit-a3/red-structure-check.py` → **exit=1，63 项失败**（缺 6+3 个新文件；主类仍持有取件/HUD/反馈/视角/租约字段；`StageMachineCore` 仍有树字段与 `BuildTree`）。
- 证据文件：`.workflow/active/client-audit-a3/evidence/red.json`（`wf verify --red`：`a3-structure-red` exit=1）。
- 失败原因正确：全部是结构断言，不是导入错误/环境故障；施工后同一探针必须 exit=0（`evidence/structure-green.txt`）。

## 运行方式

```powershell
# 先行红测（已执行，归档于 evidence/red.json）
python .workflow/active/client-audit-a3/red-structure-check.py

# 编译（每子批）
unity --proxy-disable --format json command --project-path "E:\unityProject\Test" recompile
unity --proxy-disable --format json command --project-path "E:\unityProject\Test" recompile_status
unity --proxy-disable --format json command --project-path "E:\unityProject\Test" console --level error --tail 50

# L1
python scripts/gate/test.py --suite L1 --profile PullRequest

# 定向 EditMode（示例）
python scripts/gate/unity-tests.py --mode editor --filter LTextBindEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter BattleHudEditModeTests --timeout 600

# 结构探针转绿
python .workflow/active/client-audit-a3/red-structure-check.py   # 期望 exit=0

# Play 冒烟
unity --proxy-disable command --project-path "E:\unityProject\Test" editor_play
unity --proxy-disable command --project-path "E:\unityProject\Test" eval --code "LiteGame.TestModeRuntime.RequestExit();"
unity --proxy-disable --format json command --project-path "E:\unityProject\Test" find_gameobjects --name "[BattleView]"
unity --proxy-disable --format json command --project-path "E:\unityProject\Test" find_gameobjects --name "Hud_Battle"
unity --proxy-disable command --project-path "E:\unityProject\Test" editor_stop

# 全量门禁
python .agents/skills/staged-delivery/scripts/workflow.py --run client-audit-a3 verify
```
