# 实施任务清单：安卓平台适配

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：正式施工（implement） ｜ 完成 5/16（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 构建链平台参数化 | - | 完成 | build-player.py 以 profile.buildTargetGroup 驱动 NamedBuildTarget 与后端切换，产物后缀与 artifactGlob 由 profile 驱动（Android 产出 apk）；BuiltinBundleBuildInvoker 支持 Android 目标并解除仅 StandaloneWindows64 的 bundles 硬门禁（支持平台清单化）；toolchain.py build 的 -Profile 单横线参数修复为 --profile；windows-x64 profile 行为零变化 | python -m unittest discover -s scripts/tests -p test_build_player.py -v（新增双 profile 平台映射/产物 glob/门禁清单用例） | unittest test_build_player.py 19/19 OK; L1 1901/1901 PASS; toolchain.py check OK(2 pre-existing warnings); Unity refres… |
| T2 | defines 校验面与平台特判守卫 | - | 完成 | toolchain.py check 新增 Android defines 校验面（期望宏集入 toolchain 配置、错集即红）；DisciplineScanner 新增对 #if UNITY_ANDROID 平台特判的守卫（白名单＝方案四处收敛点；正/负例用例） | python scripts/toolchain.py check（错宏集红、对齐绿）＋python scripts/gate/unity-tests.py --mode editor --filter DisciplineScannerTests | defines 校验面 test_toolchain_defines 14/14 OK; DisciplineScanner 定向 52/52（含 R15 正负例与允许面存在性）; L1 1905/1905 PASS; Unity 编译 … |
| T3 | Android 工程设置批 | T2 | 完成 | Android scriptingBackend=IL2CPP 与 targetArchitectures=ARM64；脚本宏与 Standalone 差集逐项对齐（补 HOTFIX_ENABLE/UNITY_PIPELINE_URP/DOTWEEN/DOTWEEN_UITOOLKIT/HIDE_REFLECTION，不补 MAGICACLOTH2 与 ODIN 系并登记）；锁横屏；包名 com.litegame.dev；INTERNET Require；useCustomMainManifest 且 dev 通道 usesCleartextTraffic=true；图形 API 序 GLES3 优先；纹理压缩 ASTC；Android 默认质量档 Performant；移动 targetFrameRate 档启动件单点设置；android profile 的 defines 死字段删除（工程设置为唯一真源） | 编辑器读回探针逐字段断言＋python scripts/toolchain.py check（defines 校验面绿） | 读回探针：IL2CPP/ARM64/LandscapeLeft/com.litegame.dev/INTERNET/customManifest=true/GLES3,Vulkan/ASTC(m_Format=3)/质量档 Perform… |
| T4 | DeployBake 安卓同步读通道 | - | 完成 | ReadArgs 保持同步契约：桌面 File 直读不变，Android 经 APK zip 条目 assets/deploy.args 真同步读取（本机 UnityWebRequest 无 WaitForCompletion——机制替换已记入 build-log 偏差）；进程内 memo 使引导期读次数恒 1；日志拆分 DeployBake missing（正常，一次性）与 DeployBake read failed（异常，一次性 Warning，进程级 flag）；Stopwatch 耗时日志；三处消费点调用形态零改动 | python scripts/gate/test.py --suite L1 --profile PullRequest（新增安卓文本分支与合并序回归用例先行红） | DeployBakeEditModeTests 7/7 PASS（桌面直读/zip 条目/memo 只读一次/missing 一次/失败一次性 Warning/成功不再读/复位语义）；AndroidPlatformSettingsEdit… |
| T5 | player-smoke adb 通道与 ARM 预检 | - | 完成 | player-smoke.py 新增 --platform android 分支：--adb-serial 与 adb connect 双形态参数化、logcat 先清缓冲或 -T 只取新行、真标记与 player-smoke.py:107 同源、会话日志工件导出路径固化、卸载清理；ARM 翻译预检命令（abilist 快检＋首个产物即探针）；分支纯 Python fake adb 单测 | python -m unittest scripts.tests.test_player_smoke -v（扩展 adb 分支用例） | test_player_smoke 20/20（10 契约 + 7 fake adb 流程 + 3 既有）；流程断言：abilist 预检→install -t -r→logcat -c→force-stop→am start→logca… |
| T6 | ILookDeltaSource 泛化镜头喂源缝 | - | 待办 | 可选接口 ILookDeltaSource 住 Platform.Unity；NewInputIntentSource 与触屏侧 TouchLookAdapter 双实现；BattleViewRig.LookDeltaOf 改按 is ILookDeltaSource 取委托；桌面 NewInputIntentSource 现值行为不变断言；模态/死亡门覆盖触屏镜头断言 | EditMode 合成断言（AddLookDelta 收触摸 delta、模态拦截、桌面回归）＋AC4.2 桌面符号回归 | - |
| T7 | 触屏意图链 | - | 待办 | TouchGestureState 纯值手势状态（float 对，Runtime）与 TouchIntentSource : IIntentSource（住 Runtime、L1 链接集不新增引擎类型）产同形态意图（Move 轴归一、Fire/Aim held、Reload 离散沿）；InputModule 按平台或 Touchscreen 能力选源且单源不合并 | python scripts/gate/test.py --suite L1 --profile PullRequest（新增映射用例先行红） | - |
| T8 | 首次 Android 出包与模拟器首启 | T1, T3, T4, T5 | 待办 | android-il2cpp profile 全链出包 Builds/android/Test.apk 且解包断言 lib/arm64-v8a；模拟器 ARM 翻译预检两段通过；player-smoke --platform android 判 PASSED（[Asset] ready 且无 BootstrapError）；development 通道映射确认（channel: development 到 EditorBuildOptions.development，缺则补）；引导期 DeployBake 读次数=1 且 Stopwatch 耗时小于 2s | build-player.py 全链日志＋player-smoke PASSED 输出＋ABI 解包断言＋DeployBake 耗时日志 | - |
| T9 | 返回键接 Back 链 | T8 | 待办 | Android 系统返回键经 Keyboard/escape 映射或补显式映射接通既有 UiInput.BackPressedThisFrame 与 UIFocusCoordinator Back 链（模态先关、页面回退）；不新开开关面 | 模拟器实操（返回关模态/回退页面）＋python scripts/gate/test.py --suite L2 --profile PullRequest（焦点用例回归） | - |
| T10 | 生命周期移动分支 | T8 | 待办 | AppLifetime OnApplicationPause 主动断开/LeaveMatch（释放席位）、resume 走既有自动重连链恢复对局；低内存回调登记日志面；被系统回收后冷启走正常启动链 | 模拟器切后台 30s 以上回前台恢复实测（会话工件）＋EditMode AppLifetime 回归 | - |
| T11 | TouchInputSurface HUD 手势面与契约 | T6, T7 | 待办 | Hud_Battle 契约节点：左半区动态摇杆、右半区镜头拖动区、Fire/Aim/Reload 按钮写 TouchGestureState（显式写入方法）；ui.widget-manifest 重生成与契约工具链全过；动手前核对 HUD prefab 与并行线归属；手势区挂 SafeArea 接收器 | python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests＋契约 diff 工具＋合成指针驱动用例 | - |
| T12 | UI 触控验证与最小修正 | T8, T11 | 待办 | InputSystemUIInputModule 触摸指针覆盖实测（不足则显式绑定触摸动作）；TMP 全局 fallback 表断言用例；CanvasScaler 设备档核对结论登记（分档挂账 UI 批、本批不做分档重排） | PlayMode 合成触摸三页可点（登录/设置/对局退出确认）＋EditMode fallback 断言 | - |
| T13 | 公网破窗与 endpoints 烙入 | T8 | 待办 | 云侧限时窗口操作留痕（安全组开 5000/tcp 与 17777/udp、Meta bind 增公网、lobby.advertise_host 改公网 IP、改后重启任务计划、测后回收步骤入文档）；endpoints local 变体（不入库）烙 meta.url 与 envelope.key；GM 端点不破窗；10/11 手册增补安卓接入节 | 公网 Meta /live 返回 HTTP 200 且含 live＋游客登录成功＋云侧配置与安全组变更记录 | - |
| T14 | 触屏操控与联测验收 | T9, T10, T11, T12, T13 | 待办 | AC4.1 至 AC4.7 逐项通过（adb 合成触点、GM 读数断言、模态/死亡段镜头不转）；AC5 一局至结算回大厅（服务端结算可查）且切后台 30s 以上回前台 30s 内恢复对局 | 会话日志工件＋服务端结算记录＋逐项实操记录（对 AC4/AC5 判据） | - |
| T15 | 确定性对账 | T8 | 待办 | GM 诊断动作 ieeprobe 登记入清单；安卓端 IeeeProbe.BuildText 基线行经冒烟工件通道导出；与 .NET 侧基线逐位一致（不一致则回方案裁决并留痕） | 桌面侧对导出工件的逐位比对输出 | - |
| T16 | 文档回写与挂账收口 | T14, T15 | 待办 | 待办总览 G4 挂账状态更新（模拟器已验项与真机仍缺项分开）；11/10/07/技术总结文档增补；两份质量登记表回写（含 MAGICACLOTH2 残留宏与 profile.defines 死字段清理登记）；AC7 全门禁复跑绿 | python scripts/gate/test.py --suite L1 --profile PullRequest＋--suite L2＋--suite Doc＋python scripts/toolchain.py check＋python scripts/codegen/gen-build-hash.py --check-shared | - |

## 任务详情

### T1 · 构建链平台参数化

- 文件: scripts/build/build-player.py, Assets/LiteGame/Editor/Resource/BuiltinBundleBuildInvoker.cs, scripts/toolchain.py

### T2 · defines 校验面与平台特判守卫

- 文件: scripts/toolchain.py, scripts/gate/toolchain.json, Assets/Tools/DisciplineScanner/Scripts/DisciplineScanner.cs

### T3 · Android 工程设置批

- 文件: ProjectSettings/ProjectSettings.asset, ProjectSettings/QualitySettings.asset, BuildProfiles/android-il2cpp.json, Assets/Plugins/Android/AndroidManifest.xml, Assets/LiteFramework/Runtime/Adapters/Unity/App/AppLifetime.cs, Assets/Tests/EditMode/AndroidPlatformSettingsEditModeTests.cs

### T4 · DeployBake 安卓同步读通道

- 文件: Assets/LiteGame/App/Meta/DeployBake.cs, Assets/Tests/EditMode/DeployBakeEditModeTests.cs

### T5 · player-smoke adb 通道与 ARM 预检

- 文件: scripts/build/player-smoke.py, scripts/tests/test_player_smoke.py

### T6 · ILookDeltaSource 泛化镜头喂源缝

- 文件: Assets/LiteClient/Adapters/Platform.Unity/ILookDeltaSource.cs, Assets/LiteClient/Adapters/Platform.Unity/NewInputIntentSource.cs, Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs

### T7 · 触屏意图链

- 文件: Assets/LiteClient/Runtime/Input/TouchGestureState.cs, Assets/LiteClient/Runtime/Input/TouchIntentSource.cs, Assets/LiteGame/App/Bootstrap/InputModule.cs

### T8 · 首次 Android 出包与模拟器首启

- 文件: BuildProfiles/android-il2cpp.json, scripts/build/build-player.py

### T10 · 生命周期移动分支

- 文件: Assets/LiteFramework/Scripts/Unity/App/AppLifetime.cs

### T11 · TouchInputSurface HUD 手势面与契约

- 文件: Assets/ShareResource/UI/Hud/Hud_Battle.prefab

### T13 · 公网破窗与 endpoints 烙入

- 文件: Docs/项目说明/10-Windows云主机部署.md, Docs/项目说明/11-联机测试流程.md

### T16 · 文档回写与挂账收口

- 文件: Docs/待办总览.md, Docs/审计/代码质量审计-客户端.md

