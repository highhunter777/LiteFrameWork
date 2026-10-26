# 验收标准确认：安卓平台适配（android-adaptation）

## 验收标准

- **AC1 出包链贯通**：`python scripts/build/build-player.py --profile BuildProfiles/android-il2cpp.json` 全链通过（restore-check/l0/BuildHash `--check-shared`/Android 内置包/构建 0 错）并产出 `Builds/android/Test.apk`；解包核验 `lib/arm64-v8a` 存在（不只看"有 apk"）；`toolchain.py build --profile ...` 修复后退出码 0；`toolchain.py check` 绿；Windows 出包＋冒烟回归不受影响；ARM 翻译预检①（abilist 快检）先于本批首次 649s 出包。
- **AC2 模拟器启动（含前置预检）**：预检两段——①`adb shell getprop ro.product.cpu.abilist` 含 arm64（秒级快检，不含即换镜像）；②首个出包产物 `install`＋`am start` 自身即探针（失败＝镜像翻译不可用）；`player-smoke.py --platform android --adb-serial <serial>`（另支持 `adb connect <host:port>`）判 PASSED——清缓冲后 logcat 出现 `[Asset] ready`（或同源变体）、不出现 `BootstrapError`；冒烟通道固化会话日志工件导出路径。
- **AC3 安卓烙入生效**：配置装载日志输出 `meta.url` 实际值（或 GM 网络诊断页显示）＝endpoints 烙入的公网端点；公网破窗后 Meta `/live` 返回 HTTP 200 且响应体含 `live`，游客登录成功；无烙入时为 `DeployBake missing`＋既有最小错误 UI（fail-closed 路径回归）。
- **AC4 触屏操控可用**（红线 13 真实操作口径，逐项）：
  - AC4.1 动态摇杆推动→角色移动；UI 模态打开时被 `!IsBlocked` 门拦截（零意图＋零位移）；
  - AC4.2 右半屏拖动→镜头 yaw/pitch 变化且方向符号与桌面一致；模态/死亡段拖动不转镜头；
  - AC4.3 开火按钮按住→连续开火（HUD 弹药递减/开火日志）；
  - AC4.4 ADS 按钮按住→瞄准态（移动限速速度降档可见；相机接管＝FOV/优先级变化的日志或 GM 读数）；
  - AC4.5 换弹按钮点按→换弹完成态（弹匣回满，非瞬时断言）；
  - AC4.6 触点点击菜单（限定三页：登录页、设置页、对局退出确认）可点可操作；
  - AC4.7 Android 系统返回键→既有 Back 链（模态先关/页面回退）。
- **AC5 联测一局**：模拟器客户端与 PC 测试者（隧道链路不动）同房完成一局至结算回大厅——服务端结算可查（结算横幅＋Meta `/matches/result`/`/matches`）；切后台≥30s 回前台后 30s 内恢复对局（"回大厅重进"不作为通过路径）。
- **AC6 确定性对账**：安卓端为 development 构建，GM 诊断动作 `ieeeprobe` 触发 `IeeeProbe.BuildText()` 产出基线行（经工件通道导出），与 .NET 侧基线逐位一致＝通过；不一致＝本条不成立（差异记录与回方案裁决属变更证据）。
- **AC7 门禁与纪律**：`test.py --suite L1 --profile PullRequest` 全绿（含纪律扫描 0 违规＋新增 `#if UNITY_ANDROID` 守卫正/负例）；`test.py --suite L2 --profile PullRequest` 全量绿；`test.py --suite Doc` 绿；`toolchain.py check` 绿（含新增 defines 校验面）；`gen-build-hash.py --check-shared` 不变绿。
- **AC8 回写与挂账**：待办总览（G4 Android 挂账——模拟器已验项与真机仍缺项分开）、11-联机测试流程（安卓接入节）、10-云侧部署（公网破窗操作与回收）、07-本地联测（安卓验证节）、技术总结-10-构建部署（profile 表安卓行）、两份质量登记表（含 MAGICACLOTH2 残留宏与 `profile.defines` 死字段清理登记）全部回写。

## 提审记录

- 2026-10-26：方案 v5（三轮独立评审通过——记录见 `reviews/plan-review.md`）的全部 8 条验收标准（AC4 含 7 个小项）以对话形式逐条向用户展示，并以结构化提问请求"确认通过/提出修改"。用户答复记入下节。

## 用户确认

确认结论: 通过
确认人: 用户（对话指示）
确认时间: 2026-10-26
用户原话: 先只出任务清单，施工后续待定
补充说明: 用户未对任何验收标准提出修改；指示以本 8 条验收标准（含 AC4 七小项）作为**任务拆解基线**。**施工（阶段 6 起）待用户后续开工指令**——任务清单产出并完成任务评审后暂停等待。

## 变更记录

（暂无）
