# 代码评审：客户端审计 A3——三大件拆分

> 评审方式: 独立子代理
> 评审对象: A3 代码提交 `f18c2af1`（A3-1）/ `e30297b8`（A3-2）/ `30b006b3`（A3-3），基线 `0837cc0d`
> 评审输入: `plan.md` / `acceptance.md` / `tasks.md`、`git show --stat` 与关键文件、`evidence/`（verify.json、targeted-*.txt、red.json、structure-green.txt、console-play.json、find-*.json、a3-play.png）
> 子代理标识: Qoder 独立子代理会话（只读）；2 轮：首轮 FAIL（1 blocker + 1 major + 1 minor），修复后复评 PASS

## 变更范围

- A3-1：`BattleViewRig` 1058→258 行；外提 `BattleViewAssets`/`BattleViewSceneAssembler`/`BattleHudAssembler`/`BattleFeedbackAssembler`/`BattleLookController`/`AuthoritativeCombatFeedback`；8 个 `Acquire*Async` 收口泛型助手；新增 `BattleViewAssetsEditModeTests` 泄漏守卫。
- A3-2：`UIBindIndex` 697→约 430 行；外提 `UIBindLocale`/`UIBindEvents`/`UIBindListSource`；主类保留门面与所有权登记。
- A3-3：树字段与 `BuildTree` 移入 `HierarchicalStageMachine`；`StageMachineCore` 构造只注册 + `InitializeCore` + `EnsureInitialized` 守卫。
- 范围外：无（并行线服务端审计文档与 `.workflow/` 未纳入提交）。

## 正确性与边界

- tick/dispose 相位与原 `BattleViewRig.cs`（Tick 431-488 / Dispose 535-613）逐项一致；装配创建序两处偏差已记录（分发器/HUD 时点、视角源时点），阵亡 HUD 已恢复至激光/保护壳之后；无 Tick 插队或跨域依赖。
- 取件助手保持取消透传、逐项降级文案、材质整组/音效逐条/`use` 失败孤儿清理；**未 Attach 路径的 HUD 泄漏**已由 `ClearReferences` 兜底销毁（与 HUD 装配器正常销毁不双杀：Unity 伪空判等跳过重复 Destroy）。
- `UIBindIndex` 公共成员集合与签名不变；`BindLocale`/`SetTextKey`/`SetTextKeyPlural`/`SetList`/`On*Changed`/`UnbindAll` 语义等价；`SetTextKeyPlural` fail-fast 文案恢复按方法名区分。
- FSM 构造序（注册→建树→迁移表校验→审计→OnInit）保持；`Start`/`Snapshot`/`ExportGraph`/审计读口/`Reset` 均有初始化守卫；构造抛用例不变。
- 指针锁定/相机接管/视角灵敏度与 ADS 收敛逻辑逐项保留；`BattleLookController.AimPoint` 为传送命令提供同一读口。

## 测试充分性

- L1（PullRequest）**1847 PASS**；定向：Battle 111 + 其余表现 70 = 181/181、UIBind 29/29、FSM 81/81（`evidence/targeted-*.txt`）。
- 结构探针：基线 exit=1/63 项（`red.json`）→ 施工后 exit=0（`structure-green.txt`）。
- Play 冒烟：`view-attached`（seq 185）→ `ended reason=Leave`（seq 208）；`[BattleView]`/`Hud_Battle` 均 0 命中；attach 后无 error/NRE；截图与 console 落盘。
- 残余：正常 ESC 离场未由输入模拟驱动（验收允许以 GM 请求桥触发）；`BattleViewRig` 完整装配-拆除序无单测，由 Play 冒烟覆盖。

## 通用性与扩展性

- **通用性: PASS**。根因（状态所有权与职责错位）已解；取件助手为共享收口，无调用方特判/新魔法值；HUD 双属主（装配器销毁 + assets 兜底）有注释说明，属有界的容错而非特判。
- **扩展性: PASS**。新增表现件挂对应装配器、绑定域挂 `UIBindEvents`/`UIBindLocale`、树语义只动层级机；无过度抽象（未引入端口接口/DI 注册）。

## 安全与性能

- 无新增每帧分配/热路径日志/静态可变状态；无输入面与权限面变化；取件与销毁路径的 Unity 对象生命周期在 EditMode/Play 证据中无泄漏（HUD 未 Attach 路径已补守卫）。

## 发现的问题与处理

| 轮 | 问题 | 严重度 | 处理 |
|---:|---|---|---|
| 1 | HUD 取件成功但未 Attach 即离场 → 实例无人销毁（残留全屏 Canvas） | blocker | `BattleViewAssets.ClearReferences` 兜底销毁；新增 `BattleViewAssetsEditModeTests`；复评确认不双杀 |
| 1 | 验证未闭环（无 verify.json/定向证据） | major | 补齐 verify.json（8 项非 Unity 门禁全绿）与 targeted-*.txt；Play 冒烟落盘 |
| 1 | `SetTextKeyPlural` fail-fast 文案退化为 SetTextKey | minor | `RequireBound(name, method)` 按方法保留原文案 |
| 2 | 归档提交号/进度计数笔误 | minor | `d09fcd16` 订正（归档三条提交号、181 计数） |
| 2 | `ApplyHudTheme` 诊断标签仍写 BattleViewRig | minor | `33344480` 订正（仅诊断串） |

## 复测记录

- 复评轮次：2 轮（首轮 FAIL → 修复 → 复评 PASS）；第 2 轮仅剩 minor，修复后按协议直接收口，不再开新轮。
- 修复后复测：Unity 重编译 0 错；`wf verify`（L1/doc/restore/l0/toolchain-check/toolchain-docs-check/Doc lane/l2-meta）全绿；定向证据与 Play 冒烟复核通过。
- 环境边界：本机 Unity 测试运行器在连续取消的测试后两次卡死（`editor_status`/`cancel_tests` 超时），重启后定向与 Play 均正常；故 `wf verify` 临时缩减为非 Unity 门禁，`toolchain-tests` 因并行 Android T1 红测未纳入——与 A3 退出条件（L1 + 定向 EditMode + doc-lint）无关，评审判定不阻塞并记录在案。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS