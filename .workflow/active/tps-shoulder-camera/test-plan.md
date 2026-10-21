# 测试计划：第三人称越肩射击改造

> 运行 tps-shoulder-camera · 阶段 4（先写测试）· 2026-10-20

## 测试策略

- **红测先行（EditMode 定向为主）**：
  - T1/T6（相机端口）：`ICameraService` 新增 `AddLookDelta`/`LookYaw`/`LookPitch` 成员 + 适配器**骨架实现（no-op）**——接口变更先行落位使测试可编译，行为空缺使断言红；
  - T4（采集解算）/T5（准心）：**现状行为与目标行为差异**直接断言红（无需骨架——现实现可编译可运行，只是行为是旧的）；
  - T3（指针锁定）/T8（俯仰姿态）：**新组件**——先落类型骨架（签名齐、行为惰性）再挂测试，断言红；
  - T7（镜像退役）：纯删除任务，红测不适用——**替代验证**＝退役点 grep 零命中（`SetViewMirrored`/`ViewMirrored`）+ 全量编译绿 + L1 全量绿，连同删除的 `CameraViewMirrorEditModeTests` 一并记录。
- **门禁分层**：EditMode 定向（TestRunnerApi）→ Unity 重编译 0 错 → L1 全量（含纪律扫描，改 LiteView/LiteGame 源后必跑）→ `gen-build-hash.py --check-shared` 不变 → doc-lint → 实机矩阵（T11，取代旧矩阵 #9/#10）。
- **运行通道**：本机 shell 通道损坏（powershell 不可达）——一切命令经 exec_editor_script + Process 直起（2026-10-19 实证配方）；EditMode 定向跑＝TestRunnerApi（2026-10-14 实证配方：`ScriptableObject.CreateInstance<TestRunnerApi>()` + `Filter{testMode=EditMode, groupNames}` + ICallbacks/TaskCompletionSource 非阻塞收结果）。

## 用例清单

| # | 任务 | 用例 | 预期失败原因（红） |
|---|---|---|---|
| R1 | T1 | `AddLookDelta` 累积：两次增量（0.10+0.05 rad）→ `LookYaw≈0.15`；yaw 环绕（+2π 归位） | 骨架 no-op，`LookYaw` 恒 0 |
| R2 | T1 | pitch 钳制：超限正/负增量 → `LookPitch` 钳在限位（机制常量带推导注释） | no-op → 恒 0 |
| R3 | T1 | `Reset()` 清视角态（复活/传送硬切回默认视角） | 现无视角态概念 |
| R4 | T6 | ADS 翻转（`SetAiming` 真/假沿）不重置视角状态 | 同上 |
| R5 | T1 | `Shutdown()` 后 `AddLookDelta` 静默 no-op（与既有"关闭后不写相机"契约同源） | 同上 |
| R6 | T4 | 屏心射线解算：fake 相机正俯投 + 注入 `SetAimWorld`——AimPoint 必落**屏心射线**与世界的交点（不再依赖 `Mouse.current` 屏幕位） | 现按 `Mouse.current.position`（EditMode 下＝OS 光标位，非屏心）解算 → 点偏 |
| R7 | T4 | 射线无地面交（瞄天）→ **射程截断点**有效：`TryGetAimPoint`=true 且 `|P−射手| ≤ HitscanRange×1.1`（InputGate 闸值口径） | 现返回 false（无点）——开火帧会撞"无点=非法" |
| R8 | T4 | 地面交点超程（远地）→ 截断到闸内 | 现不截断 → InputGate 整帧丢弃风险 |
| R9 | T5 | 准心位置恒屏心（构造已知鼠标位≠屏心的注入位，断言 `_reticle` 在屏心） | 现跟注入的鼠标屏幕点 |
| R10 | T3 | 指针锁定状态机：进局→锁定+隐藏；上下文门被拦/死亡/退场→解锁恢复 | 新组件骨架惰性 |
| R11 | T8 | pitch→上半身姿态映射：区间插值 + 越界钳制（纯映射件，不依赖具体骨架） | 新组件骨架惰性 |

配套**编译面**：`ICameraService` 新成员会触及全部实现方（`CinemachineCameraService` + 测试替身们）——骨架补齐后全量编译须绿（编译红不是有效红）。

## 预期失败证据

**红测实跑**（2026-10-20，TestRunnerApi 定向跑五夹具，Unity 编译 0 错后执行；复跑两轮一致——第二轮为
`PlayerInputActions.Dispose()` EditMode 报错债处置后的干净版，报告存 `.codely-cli/tmp/tps-red-run.txt`）：

`total=12 fail=10 nonFail=2` —— **10 例因预期原因红（断言级失败）**：

| # | 失败用例（断言首行） | 红因（与预期一致） |
|---|---|---|
| R11 | 俯仰映射_区间内恒等 → Expected 0.3 But was 0.0 | 骨架惰性 |
| R6 | 屏心射线解算 → Expected 5.0 But was **-5.264** | 现行按鼠标屏幕位解算（点偏离屏心——旧行为实锤） |
| R7 | 瞄天截断 → Expected ≤ 110.0 But was **61,530,548.0** | 现行无截断——近水平射线地面兜底点飞 61km（InputGate 距离闸 100×1.1=110 必丢整帧，**生产缺陷实锤**） |
| R8 | 远地超程 → Expected ≤ 110.0 But was **479.54** | 同上（超程无截断，超闸 4 倍+） |
| R4 | ADS翻转不重置 → Expected 0.2 But was 0.0 | 骨架 no-op |
| R2 | pitch 超限钳制 → Expected 1.047(PitchClampMaxRad) But was 0.0 | 骨架 no-op |
| R1 | yaw/pitch 累进 → Expected 0.15 But was 0.0 | 骨架 no-op |
| R9 | 准心恒屏心 → Expected 0 But was **-460.0** | 现行跟注入鼠标点（旧行为实锤） |
| R10b | 指针锁定幂等 → Expected True But was False | 骨架惰性 |
| R10a | 指针锁定锁定并隐藏 → Expected True But was False | 骨架惰性 |

**2 例空洞绿（预期内、如实记录）**：`Reset_清视角态`、`Shutdown后_视角增量静默no_op`——骨架 no-op 下
"状态恒 0"使清零/不写断言平凡成立；R1 实现后即成为真实守卫（清零/关闭拒写）。

**红测期顺带发现并已登记的存量债**：`PlayerInputActions.Dispose()`（生成文件 Player.cs:291）EditMode 下
`Object.Destroy` 报错——按格式登记审计（技术债·生成器 bug 节），EditMode 测试侧以共享资产实例规避
（源不持所有权 ⇒ Dispose 只 Disable 不销毁）。

## 运行方式

- **EditMode 定向**：exec_editor_script + TestRunnerApi（Filter.groupNames 按类全名数组；失败用例按 `TestStatus==Failed && HasChildren==false` 打 FullName+Message；输出落 `.codely-cli/tmp/` 备查）；
- **L1**：`dotnet test Tests/LiteFramework.Core.Tests`（Process 直起、UTF-8 重定向落盘、显式 `$LASTEXITCODE` 等价物＝Process.ExitCode，中文摘要行为准）；
- **完整门禁**（阶段 6 verify）：`.workflow/config.json` commands 配真实命令（L1 gate / buildHash --check-shared / doc-lint）后 `wf verify`。
