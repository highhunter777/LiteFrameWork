# 假人复活修复与 bot 操作（GM 面板）

> 触发（2026-10-11）：用户回报"测试模式里无限生命的假人会放死亡动画不复位"。
> 处置 = 根因修复（死亡锁存接"复活事实"）+ GM 面板 bot 操作两项；用户裁决：**重置假人现在做、房内追加假人立批挂账**（见 §3）。

## 1. 缺陷：死亡锁存无复位路径（"不复位"根因）

**链路**：假人死亡 → 动画进死亡叶（设计本就"死亡期恒驻"）→ `RespawnSystem` 到点复活（满血 + `LifeStartFrame` 更替 + 写 `FrameEventKind.Respawn`）→ **但驱动器 `DeadSeen` 死亡锁存置位后无任何复位路径**（旧注释自认"置位后恒真"；`Respawn` 事件全工程零消费者）——`SlotAnimContext.IsDead` 恒真 ⇒ `CombatRootStage` 的"复活后按新的存活事实退出"分支不可达 ⇒ **尸体姿态永久滞留**。远端（bot）连死亡沿都只走快照路径——**快照侧"生命更替"是唯一可靠复位信号**。

**修复**：
- `SimView.TryGetLifeStartFrame(slot)`（新读口——本地取预测态 / 远端取权威快照；`LifeStartFrame` 本就在快照 15 载字段，与 `IsDead` 同构可重建）；
- `CharacterLocomotionDriver`：生命起始帧变化 ⇒ 清 `DeadSeen`（注释口径同步为"**单条生命内**不可逆"）。

## 2. GM bot 操作（用户令）

| 项 | 形态 | 语义 |
|---|---|---|
| **bot 无限生命** | `TestModeOptions` 现场开关（Live 组——面板按表自动渲染，页零改动） | 权威侧每帧把补位席位回满（单帧伤害恒 < 满血 ⇒ 永不跨死线；命中/飘字反馈照常）；已死（尸体期）者跳过——走完复活链自然回满。`Set` 副作用镜像宿主钩子 `SimTestRules.BotInfiniteHp`（宿主直读、同帧起效）；进房初值镜像、非测试模式不武装（同 InfiniteAmmo 口径） |
| **重置假人** | 测试模式页「bot 操作」按钮 | 一次性宿主钩子 `SimTestRules.BotResetRequested`：补位席位满血回出生点、清死亡/尸体/复活账、`LifeStartFrame` 更替（动画按新生命退出死亡叶）；消费即清；**进房清残留**（同 TeleportDispatch 口径） |

**装配面**：`SimTestRules` 宿主钩子 4→6 件（**Sim 源集改动 → BuildHash 重生成 shared `caec840e063969c1`**）；宿主实现 = `LocalServerTransport.ApplyTestModeOverrides`（权威步进前执行；BotFrozen 同址）；GM 页 = `GmTestModePage` 新增「bot 操作」块（重置按钮 + 无限生命开关指引）。假人 = 补位席位，本地玩家不受两项管辖。

**验证（本批实测）**：

| 门禁 | 结果 |
| --- | --- |
| Unity 编译 | recompile completed / failed:false / errors:[] |
| EditMode | `CharacterLocomotionEditModeTests` **30/30**（新用例：死亡→复活→退出死亡叶——修复前红）· `DeathAnimEditModeTests` **2/2**（死亡叶回归）· `SimViewEditModeTests` **15/15**（新用例：新生命读口 本地/远端/未见面） |
| BuildHash | 重生成 shared `caec840e063969c1`（76 文件）+ meta `20c3282c0084fb6e` |
| L1 | `test.ps1 -Lane L1 -Profile PullRequest` **1590/1590 PASS**（含 BuildHash 守卫与纪律扫描） |

**边界（如实）**：
- `LocalServerTransport`（宿主钩子实现）无 EditMode 覆盖——其正确性以 **Play 实机**为准（EditMode 覆盖的是读口与驱动逻辑）。
- bot 无限生命为"每帧回满"实现：假人**仍吃伤害数值**（飘字/命中链照常），只是永远死不了；与全房免死 `NoDeath`（保底 1）语义相近但管辖面不同（只护补位席）。

## 3. 余项（挂账、立批）

- **房内追加假人**（对局中新增站桩靶）：需放开"固定 2 座"世界规模（`BattleContext.ExpectedPlayers` 定容重建与预测口径——服务端房间人数与客户端世界构造现约定死 2），归 **G2 8 人基线**面；用户裁决另立批，本批不做。
