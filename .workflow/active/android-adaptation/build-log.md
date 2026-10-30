# 施工记录：安卓平台适配

> 运行 android-adaptation · 创建于 2026-10-26T17:03:04+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-26T17:25:46+08:00 · 新增任务 T1「构建链平台参数化」
- 2026-10-26T17:25:46+08:00 · 新增任务 T2「defines 校验面与平台特判守卫」
- 2026-10-26T17:25:46+08:00 · 新增任务 T3「Android 工程设置批」
- 2026-10-26T17:25:46+08:00 · 新增任务 T4「DeployBake 安卓同步读通道」
- 2026-10-26T17:25:46+08:00 · 新增任务 T5「player-smoke adb 通道与 ARM 预检」
- 2026-10-26T17:25:47+08:00 · 新增任务 T6「ILookDeltaSource 泛化镜头喂源缝」
- 2026-10-26T17:25:47+08:00 · 新增任务 T7「触屏意图链」
- 2026-10-26T17:25:47+08:00 · 新增任务 T8「首次 Android 出包与模拟器首启」
- 2026-10-26T17:25:47+08:00 · 新增任务 T9「返回键接 Back 链」
- 2026-10-26T17:25:47+08:00 · 新增任务 T10「生命周期移动分支」
- 2026-10-26T17:25:47+08:00 · 新增任务 T11「TouchInputSurface HUD 手势面与契约」
- 2026-10-26T17:25:47+08:00 · 新增任务 T12「UI 触控验证与最小修正」
- 2026-10-26T17:25:47+08:00 · 新增任务 T13「公网破窗与 endpoints 烙入」
- 2026-10-26T17:25:48+08:00 · 新增任务 T14「触屏操控与联测验收」
- 2026-10-26T17:25:48+08:00 · 新增任务 T15「确定性对账」
- 2026-10-26T17:25:48+08:00 · 新增任务 T16「文档回写与挂账收口」
- 2026-10-26T17:33:20+08:00 · 开始任务 T1「构建链平台参数化」
- 2026-10-29T20:52:36+08:00 · 完成任务 T1 · 证据: unittest test_build_player.py 19/19 OK; L1 1901/1901 PASS; toolchain.py check OK(2 pre-existing warnings); Unity refresh 0 errors; files: scripts/build/build-p…
- 2026-10-29T21:07:49+08:00 · 开始任务 T2「defines 校验面与平台特判守卫」
- 2026-10-29T21:07:50+08:00 · 完成任务 T2 · 证据: defines 校验面 test_toolchain_defines 14/14 OK; DisciplineScanner 定向 52/52（含 R15 正负例与允许面存在性）; L1 1905/1905 PASS; Unity 编译 0 错; Doc 0/0; buildHash --check-shared 不…
- 2026-10-29T21:18:02+08:00 · 开始任务 T3「Android 工程设置批」
- 2026-10-29T21:18:02+08:00 · 更新任务 T3（files）
- 2026-10-29T21:18:02+08:00 · 完成任务 T3 · 证据: 读回探针：IL2CPP/ARM64/LandscapeLeft/com.litegame.dev/INTERNET/customManifest=true/GLES3,Vulkan/ASTC(m_Format=3)/质量档 Performant/宏含 HOTFIX_ENABLE+UNITY_PIPELINE_URP;…
- 2026-10-29T22:09:14+08:00 · 开始任务 T4「DeployBake 安卓同步读通道」
- 2026-10-29T22:09:15+08:00 · 更新任务 T4（acceptance, files）
- 2026-10-29T22:09:15+08:00 · 完成任务 T4 · 证据: DeployBakeEditModeTests 7/7 PASS（桌面直读/zip 条目/memo 只读一次/missing 一次/失败一次性 Warning/成功不再读/复位语义）；AndroidPlatformSettingsEditModeTests 8/8 PASS（含源码清单不放明文的断言）；L1 1905…
- 2026-10-29T22:19:12+08:00 · 开始任务 T5「player-smoke adb 通道与 ARM 预检」
- 2026-10-29T22:19:12+08:00 · 完成任务 T5 · 证据: test_player_smoke 20/20（10 契约 + 7 fake adb 流程 + 3 既有）；流程断言：abilist 预检→install -t -r→logcat -c→force-stop→am start→logcat -d 轮询→工件落盘→uninstall；BootstrapError 判失…

## 关键决定

- 待填写

## 偏差与处理

- T4 读取机制替换（2026-10-29）：方案写 `UnityWebRequest + WaitForCompletion()`，**本机 Tuanjie 2022.3.55f1c1 的 `UnityWebRequest` 无该成员**（反射实测只有 `Send()`/`SendWebRequest()`，`WaitForCompletion` 缺失）。若改成"Sync 自旋等 isDone"，本地 jar 读的完成可能依赖主循环派发 → 有把引导链卡死到超时的风险（方案风险段点名的同一类问题）。改用 **APK 自身即 zip** 的事实：`Application.dataPath` 指向 APK，按条目 `assets/deploy.args` 用 `System.IO.Compression.ZipFile` **真同步读**。同步契约、三处消费点形态、memo、日志拆分全部保持；`BakeReadTimeoutSeconds` 随之取消（不再有阻塞式网络等待），保留 `Stopwatch` 耗时日志供批②判据。读取实现与平台分发分离：`ReadZipEntry`/`ReadDesktopFile` 平台无关（Windows 可用临时 zip 直测），仅 `ReadPlatform` 带 `#if UNITY_ANDROID`（本件在 R15 白名单内）。真机行为仍需 T8 首启实测确认。
- T3 追加（2026-10-29）：明文放行从"清单全通道单文件"改为**通道驱动注入**——源码清单保持不放明文的安全默认，`build-player.py` 在 development 通道出包前注入 `usesCleartextTraffic` 并 `finally` 还原；非 development 通道出包前断言清单干净（残留明文即构建失败）。

## 问题与修复

- 待填写

## 验证结果

- 待填写（verify 会自动追加每次运行记录）
- 2026-10-26T17:33:20+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json

## 交付摘要

- 待填写

## 未决事项

- 待填写（无则写“无”）

## 文件清单

- 待填写

## 后续建议

- 待填写

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-26T17:03:04+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-26T17:08:18+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-10-26T17:22:45+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-10-26T17:25:46+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-10-26T17:25:48+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-26T17:26:10+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-26T17:33:20+08:00 · 完成「先写测试」→ 「正式施工」
