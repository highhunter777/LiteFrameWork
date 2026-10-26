# 方案：安卓平台适配

> 运行 android-adaptation · 创建于 2026-10-26T17:03:04+08:00 · 2026-10-26 三轮独立评审后修订（三轮评审发现与修订记录见 reviews/plan-review.md）

## 背景与目标

- 现状：项目为"桌面先行"形态——Windows Standalone IL2CPP 联测闭环已验证，安卓处于"**档位已备、链路未接**"状态：出包档 `BuildProfiles/android-il2cpp.json` 已存在（C0 批曾在另一台机产出 Test.apk 33.3MB，提交 b796b5a），本机 Tuanjie 编辑器 AndroidPlayer 模块齐备（含 SDK/NDK/OpenJDK），xLua 安卓原生库在 `Assets/Plugins/Android/libs/`（arm64-v8a/armeabi-v7a/x86——**无 x86_64**），LiteSim 确定性内核为跨平台逐位一致而设计（自研软 Sqrt＋SimTrig 查表＋运行期禁超越函数）——但构建脚本三处硬编码绑死 Windows、启动配置烙入通道在安卓失效、输入面零触摸绑定、冒烟无 adb 通道、联测接入强假设 WireGuard。
- 要解决的问题：把安卓从"档位已备"推进到"**可出包、可在模拟器上跑通启动链与联测、具备完整触屏操控**"，并按用户裁决以公网破窗方式接入现有联机测试环境。
- 目标（可观察）：
  1. `build-player.py --profile BuildProfiles/android-il2cpp.json` 端到端出包 ARM64 APK，全链门禁绿；
  2. 模拟器安装冷启走完 `Launch→Patch→Preload→Main` 落到登录/主页面（既有真标记判据）；
  3. deploy.args 烙入（meta.url 公网端点＋信封 PSK）在安卓生效，公网破窗后能登录、入房、**触屏打完一局**到结算回大厅；
  4. 触屏操控六要素可用：摇杆移动、拖动镜头、开火、ADS、换弹、返回键 Back 链；切后台→回前台自动重连恢复。

## 需求澄清与假设

- 需要确认的问题：无（三项高影响口径已裁决闭合；触屏按钮集合按输入面现状裁剪一事写入"范围"节，在验收确认时复核）。
- 已确认答复（2026-10-26 用户裁决）：
  1. 适配范围＝**B·完整触屏操控**（联测可用之上做虚拟摇杆＋触摸镜头＋开火/ADS 等触屏按钮与 UI 触控化）；
  2. 联测接入＝**公网破窗**（云侧临时开放公网端口供真机/模拟器直连）；
  3. 目标机型＝**先模拟器**（安卓模拟器/云真机验证链路，真机性能档位后定）。
- 采用假设：
  1. 模拟器为带 **ARM 翻译**的 x86_64 镜像（MuMu 12 / AVD API 30+ 翻译层），可运行 ARM64-only APK——因 `libxlua.so` 无 x86_64 ABI，**无 ARM 翻译的镜像不可用**（预检门见设计要点与 AC2，失败即提前裁决真机/云真机）；
  2. 测试包使用 debug keystore（发布级签名与渠道分发归 C 级，非本批）；安卓测试包走 **development 通道**（profile `channel: development`——与 DevHUD/GM 触发面配套）；
  3. 信封 PSK 与 Meta 地址经 `endpoints` 烙入 APK——封闭测试口径（与 Windows release 通道每人 conf 同等安全级）；
  4. 公网破窗为限时窗口（测后回收），GM 端点不破窗（继续只绑隧道）；
  5. 安卓包名设为 `com.litegame.dev`（测试期；验收确认时可改）。
- 假设影响与风险：ARM 翻译层可能掩盖真机性能/兼容问题（xLua so、IL2CPP AOT 在翻译层的行为需实测）——模拟器结论标注"模拟器口径"，真机矩阵仍归 G4；PSK 烙入 APK 意味着泄露即失效——封闭测试红线（不得公开宣传）下可接受，测后轮换。

## 范围与非目标

- 范围内（八个工作包）：
  1. **构建链平台参数化**：`build-player.py`（NamedBuildTarget 按 profile.buildTargetGroup 映射、产物后缀/artifactGlob 由 profile 驱动、后端切换 eval 参数化）、`BuiltinBundleBuildInvoker`（内置内容包构建支持 Android 目标、解除 `platform != StandaloneWindows64` 硬门禁）、`toolchain.py cmd_build` 的 `-Profile`→`--profile` 参数 bug 修复（修复后断言：`toolchain.py build --profile ...` 退出码 0 且 builder 收到 `--profile`）、新工具登记 `toolchain.json`＋工具索引；
  2. **Android 工程设置批**（经 unity-pipeline，禁直改）：IL2CPP＋ARM64、Android 脚本宏与 Standalone 差集逐项裁决后对齐（**守卫＝`toolchain.py check` 新增 defines 校验面**，随本批落地）、锁横屏（现 defaultScreenOrientation=4 自动旋转）、包名、INTERNET Require、cleartext 策略（dev 通道 manifest）、图形 API 序 GLES3 优先、纹理压缩显式 ASTC、默认质量档与移动 targetFrameRate 档、**新增 `#if UNITY_ANDROID` 平台特判纪律守卫（DisciplineScanner 目标扩列，随批①落地）**；
  3. **启动配置安卓通道**：`DeployBake.ReadArgs()` 读流平台化（安卓 UnityWebRequest 同步收流〔WaitForCompletion〕，桌面 File 直读不变），三处同步消费点调用形态零改动，Loader 纯函数合并序不动；PSK 经 endpoints 烙入；
  4. **触屏操控面**：HUD 触摸手势面（动态摇杆/镜头拖动区/Fire/Aim/Reload 按钮，走 UI 契约工具链重生成）＋ `TouchIntentSource : IIntentSource`（既有文档口径"新平台设备源＝新写 IIntentSource"，装配点 `InputModule.cs`）＋ **镜头喂源经新增可选接口 `ILookDeltaSource` 泛化既有 `LookDeltaOf` 工厂缝**（接口住 Platform.Unity、手势状态纯值住 Runtime——控制态门零改动）＋ 返回键接入既有 Back 链；
  5. **UI 触控与 SafeArea（验证＋最小修正口径）**：InputSystemUIInputModule 触摸指针覆盖验证（不足则显式绑定）、战斗 HUD 手势区 SafeArea 接收、TMP 全局 fallback 一个断言用例；CanvasScaler aspect 分档与页面重排**不在本批**（见非目标）；
  6. **生命周期**：`AppLifetime` 既有钩子扩移动分支（暂停→断开、恢复→既有自动重连链）；
  7. **公网破窗联测接入**：云侧（安全组窗口开 5000/tcp＋17777/udp、Meta bind 增公网、`lobby.advertise_host=公网IP`；Room 已绑 0.0.0.0:17777 只差安全组）＋ 安卓包 endpoints 烙公网端点 ＋ 10/11 手册增补；
  8. **冒烟与确定性核验**：`player-smoke.py` 增 adb 通道（编辑器自带 SDK platform-tools，install/launch/logcat 真标记）；`IeeeProbe` 基线行安卓端产出（GM 诊断动作触发）与离线对账。
- 非目标：
  - iOS；Google Play 发布合规（隐私/targetSdk 合规审查、正式 keystore、渠道包、AAB 分发）——归 C 发布级；
  - ARMv7 与 x86_64 ABI（本批 ARM64-only；`libxlua.so` 无 x86_64 登记为已知边界，不补）；键鼠外设（Android USB/BT 键鼠）；
  - 真云 CDN、Android 热更真机矩阵全量闭合、断点续传/写盘中断矩阵——G4 挂账维持（模拟器可预验的项在本批内验并回写挂账状态）；
  - 真机性能定档（模拟器口径不替代真机矩阵）；
  - 辅助瞄准/准心吸附（登记为后续手感批）；
  - **CanvasScaler aspect 分档/页面重排**（评审 B5 收口：本批只"验证＋最小修正"，分档实测后挂账 UI 批裁决）；
  - 新增游戏玩法动作：Jump 在游戏本体即不存在、Switch/Skill 输入位有位无生产者（IInputService 注释明确"未实现"）——触屏面只交付**现有可玩动作集合**（Move/Look/Fire/Aim/Reload＋菜单导航＋返回），不新增玩法。

## 现状与约束

- **A 构建与工程设置**
  - 编辑器：Tuanjie 2022.3.55f1c1（E:/unity3d）；AndroidPlayer 模块已装（`PlaybackEngines/AndroidPlayer`，含 `SDK/`、`NDK/`、`OpenJDK/`、il2cpp+mono 双变体）；activeBuildTarget=StandaloneWindows64；Standalone 侧 scriptingBackend=IL2CPP、apiCompat=NET_Unity_4_8（编辑器探针 2026-10-26）。（`Docs/环境/非作者机恢复与验证记录.md:211` 记录的"Android Build Support 未装"是另一台机器的历史快照，非本机现状。）
  - Android 平台设置全为 URP 模板默认：scriptingBackend[Android]=**Mono2x**、targetArchitectures=**ARMv7 only**、minSdk=22、targetSdk=Auto、包名 `com.UnityTechnologies.com.unity.template.urpblank`、无 keystore、Graphics APIs=[OpenGLES3,Vulkan]（m_Automatic: 0）、ForceInternetPermission=0、useCustomMainManifest=0、`defaultScreenOrientation: 4`（自动旋转含竖屏）、`m_ActiveColorSpace: 1`（Linear）、`androidRenderOutsideSafeArea: 1`、activeInputHandler=2（Both，C0 遗留告警）、Android 默认质量档=Balanced（`ProjectSettings.asset` L166-300/L553-554/L939 与编辑器探针）。
  - **Android 脚本宏为模板残留**（`scriptingDefineSymbols.Android: DOTWEEN;DOTWEEN_UITOOLKIT;HIDE_REFLECTION;UNITY_POST_PROCESSING_STACK_V2`），缺 Standalone 侧的 `HOTFIX_ENABLE`/`UNITY_PIPELINE_URP`/`MAGICACLOTH2`/`ODIN_INSPECTOR*`——切平台即编出不同代码路径；其中 `MAGICACLOTH2` 经全库核实**插件本体不在工程内**（仅一处测试文件文本提及），属残留宏。
  - 出包档：`BuildProfiles/android-il2cpp.json` 已存在（IL2CPP/ARM64/OfflineBundles/debug-keystore/stripping Minimal/`*.apk`/endpoints{}/`channel: development`——2026-09-22 建）；**其 `defines: []` 字段无任何消费方**（`build-player.py` 不读 defines/architecture——架构后端由工程设置承载），存在双真源风险（评审 M2）；C0 批曾在另一台机交付 Android IL2CPP 构建（Test.apk 33.3MB、649s、提交 b796b5a，`Docs/施工进度/客户端线.md:35/120`）。
  - 构建链三处硬编码（`scripts/build/build-player.py:113/170/219/268`）：产物名拼 `productName + ".exe"`；artifactGlob 默认 `*.exe`；后端切换 eval 写死 `NamedBuildTarget.Standalone + IL2CPP`；另有 `platform != StandaloneWindows64 → raise`（Android 只能 `--skip-bundles`）。
  - 内置内容包构建写死 Windows：`Assets/LiteGame/Editor/Resource/BuiltinBundleBuildInvoker.cs` 的 `RunCore` 固定 `BuildTargetGroup.Standalone`＋默认 `BuildTarget.StandaloneWindows64`（源码注释记录过"Android 目标打 ARM64 包进 Windows Player"事故）；`RunForCandidate` 同样固定 Windows。
  - 冒烟纯 Windows：`scripts/build/player-smoke.py` 只认 `.exe`、读 `LocalLow/<company>/<product>/Player.log`、`taskkill`——无 adb/logcat；真标记清单在 `player-smoke.py:107`（`BootstrapError` 失败标记/`[Asset] ready` 成功标记，另有 MemLoop/InputModal 变体）。
  - 工具链入口 bug：`scripts/toolchain.py:440` 传 `-Profile`（单横线）而 build 子命令 parser 只认 `--profile`（L519）——`toolchain.py build` 路径当前不可用。
  - 门禁宿主：`scripts/gate/test.py:244` L2 显式要求 Windows 宿主；`restore-check` 对 JDK/SDK/NDK 仅 WARN 不阻塞。
- **B 运行链平台依赖**
  - `DeployBake.cs`（读 `StreamingAssets/deploy.args`）：`File.Exists/ReadAllText` 直读——**安卓 APK 内恒 false→返回 null**，代码注释自述"Android 烙入需 UnityWebRequest 读流——出现该平台分发需求时另批接"（`Assets/LiteGame/App/Meta/DeployBake.cs` XML 注释）→ **P0**：`meta.url`/信封 PSK/`content.cdnUrl`/GM 键全丢。
  - **`DeployBake.ReadArgs()` 有三处同步消费点**（评审 B1）：`ContainerModule` 装配行（`Assets/LiteGame/App/Bootstrap/ContainerModule.cs:94`）、`ContentModule` 装配行（`Assets/LiteGame/App/Bootstrap/ContentModule.cs:45`）、`MetaEntryConfig.LoadWithBaked()`（GM 页二次取配置，`Assets/LiteGame/App/Meta/MetaDeployConfig.cs:112`）——三处都在**同步装配期/同步调用**，决定读取必须**同步完成**。
  - **引导结构约束（评审二轮核实、三轮加固）**：模块初始化（含两处装配行）全在 `GameEntry.RunBootstrapAsync` 的 `await _host.InitializeAsync(ct)` 内（`ClientHost.cs:101-146` 逐模块 await，桌面路径同步完成）；`GameEntry.Start()` 先 `TakeContainer()`（`_container == null` 时**直接抛 `InvalidOperationException("未装配")`**）再断言 `_fsm != null`（否则 `ShowBootstrapError("host bootstrap incomplete")`）——**任何真正 yield 的 await 都会把续体推到 Start() 之后→必炸**。⇒ 安卓读取必须同步完成（方案 A），或重构引导序列（备选方案 B，不默认采用）。
  - 命令行源：`ContentDeployConfig.Load()`/`MetaEntryConfig` 走 `Environment.GetCommandLineArgs()`——移动端无自定义命令行 → 三源里"命令行"与"烙入"两源同时失效。三源合并序（命令行＞包内烙入＞默认）为 token 层单流拼接（2026-10-15 裁决形态，`ContentDeployConfig.Load(string[], string)`）。
  - YooAsset 3.0.5 内嵌包（`Packages/com.tuyoogame.yooasset`）：Editor=EditorSimulateMode，Player=OfflinePlayMode（内置包根 `StreamingAssets/yoo` 或已确认候选 `bundle/` 绝对路径镜像）；收集组 10 组（Config bytes/Lua/场景/UI/角色/音效全进 AB，`BundleCollectorSetting.asset`）；无 Host/Web 远端文件系统（现网远端分发走内容信封＋磁盘镜像）。YooAsset 内置文件系统对 Android StreamingAssets 自带支持（其设计目标平台）。
  - **候选根/激活记录/结构化日志全部锚 `persistentDataPath`，安卓天然可移植（评审 B5 核实结论）**：`FileSys` 根＝`UnityPathProvider.RootPath => Application.persistentDataPath`（`LiteFramework/Scripts/Unity/IO/UnityPathProvider.cs:9`）；候选资产包根＝`persistentDataPath/content/candidate/bundle`（`YooAssetContentService.cs:85-89`＋`ReleaseLayout` 单源常量）；激活记录＝FileSys 相对路径下 JSON（`FileActivationRecordIO.cs`）；移动端磁盘余量探针已有 `DiskSpaceProbes`（FileSys 卷查询＋写探针回退形态）——本项**无需改造**，模拟器实测读写即可。
  - Luban bytes 经 YooAsset `TextAsset` 装载（无 File.IO），`TableLayout` 跨平台 `Assets/` 逻辑路径（`Assets/LiteClient/Adapters/Serialization.Luban/ConfigService.cs`/`TableLayout.cs`）——平台中立 ✓。
  - xLua 源码内嵌（`Assets/XLua/`），安卓原生库在 `Assets/Plugins/Android/libs/`（arm64-v8a/armeabi-v7a/x86，磁盘核实）；**无 x86_64 ABI**——模拟器必须有 ARM 翻译（预检门见设计要点与 AC2）；`HOTFIX_ENABLE` 宏 Android 侧缺失（见上）。
  - 传输层：kcp2k 纯 C#（`Assets/LiteNet/Transport/KcpTransportClient/Server.cs`、`Vendor/kcp2k/`）——平台风险低；安全信封 AES-256-GCM＋票据＋三层限流已交付；PSK 装载链：参数/烙入→`LITENET_SECURE_ENVELOPE_KEY` 环境变量（**移动端无环境变量机制**）。
  - HTTP：`HttpCandidateFetcher` 走 UnityWebRequest；`ContentDeployConfig` 无 scheme 校验（明文 http 合法）→ **安卓 9+ 默认 cleartext 拦截**，需 manifest 策略。
  - 确定性内核：float 判定面＋自研 `SimMath.Sqrt`（软 sqrt 正确舍入，绕开 Mono/.NET ulp 差异）＋`SimTrig` 4096 查表（运行期禁超越函数，纪律扫描 R1 把守）＋`IeeeProbe`/`IeeeBaselineChecker` 跨运行时对账机制（`Assets/LiteSim/Core/Scripts/{SimMath,SimTrig}.cs`、`Core/Scripts/IeeeProbe.cs`、`Core/Editor/IeeeBaselineChecker.cs`）——为跨 ARM/x86 逐位一致而设计；`IeeeProbe` 为 LiteSim 运行时纯类（`System.BitConverter`，IL2CPP Player 可直接跑）。
- **C 输入/操控/UI**
  - Input System 1.11.2；源资产 `Assets/LiteClient/Adapters/Platform.Unity/Actions/Player.inputactions` 在库（磁盘核实），生成类 `LiteClient.PlayerInputActions`（`Player.cs`）：GamePlay map 含 Fire/Move/Look/Aim/Dash/Reload，**唯一 control scheme=KeyboardMouse，零触摸绑定**；Jump 动作不存在；Dash 有码无消费者；Switch/Skill 有位无生产者。
  - 意图链：`NewInputIntentSource : IIntentSource` → `InputService` 三件门（上下文门/上行/帧边界门；UI 模态写零意图）→ `SimInputFrame`；扩展点口径＝**"新平台设备源＝新写 IIntentSource"**（`Docs/修改指南.md:152`），装配点 `App/Bootstrap/InputModule.cs`（`InputService.SetSource` 单源赋值，`InputService.cs:59`）——"单源不合并"成立。
  - **视角链（评审 B2＋二轮 #2 核实后的准确形态）**：`BattleViewRig.Tick`（`Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs:436-464`）的 `pointerLocked` 是**对局控制中谓词**（`_view != null && _view.HasLocalDisplay && _view.LocalEntityId != 0 && (_input == null || !_input.IsBlocked) && !IsLocalDead()`——模态/死亡/加载门内含其中）；视角增量经**注入缝 `_lookDeltaOf`**（声明 L68、注入 L495、判空＋`look != Vector2.zero` 跳过 L443-452）喂 `AddLookDelta`（L450）；`_pointer.Lock()/Unlock()` 由该谓词**下游驱动**（L464）；灵敏度走 `LookTuning`（`LookSensitivityRadPerPixel`×ADS `AdsScale`，GM 实时覆写、实机定档后写回机制常量）。**但喂源工厂硬绑单实现**：`BattleViewRig.LookDeltaOf(IInputService)`（L890-895）只在 `source is NewInputIntentSource` 时返回委托（读 `NewInputIntentSource.LookDelta:204`）；`IIntentSource` 接口**无**视角增量成员（`IIntentSource.cs:24-45`，可选接口先例＝`IAimWorldSink`〔同文件 L51〕）——`TouchIntentSource` 走 `SetSource` 上线后该工厂现形态返回 **null → 触屏镜头永不生效**（须以 `ILookDeltaSource` 可选接口泛化，见设计要点）。⇒ 触屏接入＝门零改动＋泛化喂源缝。
  - 射击链：`aimCamera.ViewportPointToRay(0.5,0.5)`＋射程截断，源码注释明确"不再依赖鼠标屏幕位，Mouse.current 缺席也可解算"——**无鼠标硬依赖** ✓。
  - `PointerLockService`：全仓唯一光标写者（仅 `Cursor.lockState/visible`，无 P/Invoke——安卓空操作）。
  - 移动痕迹：**零**（OnScreenButton/OnScreenStick/Touchscreen/虚拟摇杆全库 0 命中）。
  - UI：EventSystem 运行时由 `UiInput.EnsureEventSystem()` 建 `InputSystemUIInputModule.AssignDefaultActions()`（默认动作集注释为"键盘+手柄"——触摸指针覆盖待验证）；无 hover 依赖（点击＋Submit/Cancel 形态，触屏友好）；CanvasScaler 全页面统一 1920×1080 match 0.5；**SafeArea 设施齐**（`SafeAreaReceiver`+`ISafeAreaStrategy`+页面契约 `Cot_SafeArea`+四形态测试）；NotoSansSC 主题字体族＋单测（TMP 全局 fallback 表未核实）；设备档预览工具既有（UiDeviceProfiles/UiPreviewMatrix）。
  - 返回键：仅 `Keyboard.escapeKey`（`UiInput.BackPressedThisFrame`，注释自承"Android 返回键随平台适配批接 Action"）。Input System 在安卓将系统返回键映射为 Keyboard/escape——**可能近乎免费**，模拟器实测后定。
  - 生命周期：`Assets/LiteFramework/Scripts/Unity/App/AppLifetime.cs` 已集中 OnApplicationPause/Focus/Quit＋EditMode 测试 ✓（移动专属行为未接：暂停断链/恢复重连）。
  - 帧率/画质：第一方代码零 `targetFrameRate`/`vSyncCount` 设置点。
  - 平台分叉：第一方代码零 `#if UNITY_ANDROID`；`Application.platform` 仅 3 处（诊断快照/`ContentModule.BuildPlayerCapabilities` 能力上报/编辑器字体目录）；无 DllInvoke/注册表/System.Windows。
- **D 联测接入**
  - 现链路强假设 WireGuard：安全组仅公网 `51820/udp`；Meta 绑 `10.8.0.1:5000`；Room 绑 `UDP 0.0.0.0:17777`（仅防火墙挡公网）；`lobby.advertise_host=10.8.0.1`；测试者接入＝Windows 基包＋每人 conf（`Dist/` 全 Windows 构件：.cmd/.ps1/.msi/.exe）。
  - 客户端配置：编辑器＝`Assets/StreamingAssets/deploy.args`（`-meta.url=http://10.8.0.1:5000`）；出包＝endpoints 烙入（`windows-x64-release.local.json` 填 endpoints 后拷入）；空白 endpoints 会**删除** deploy.args（fail-closed）。
  - 公网破窗所需云侧动作（10 手册口径）：安全组窗口开 `5000/tcp`＋`17777/udp`；Meta bind 增公网可达；`lobby.advertise_host` 改公网 IP；改配置须重启任务计划；PC 测试者隧道链路零改动。
- **E 挂账对齐**
  - "Android 热更真机矩阵"随 G4 登记（阻塞原因：无设备——`Docs/施工进度/框架先行与验收线.md:65/98`）：移动端磁盘余量未验、Android IL2CPP 密码学后端未验（#9 Windows x64 已验 RSA-OK）、断点续传未做——本批在**模拟器**可预验前两项的前置链路，真机项维持 G4 挂账并回写状态。
  - UI 线既有挂账："Android 返回键随平台适配批"（`Docs/施工进度/UI线.md:514`）＝本批；"根页面 Back 策略随消费者裁决"仍归消费者批；CanvasScaler aspect 分档随本批"验证＋最小修正"实测后挂账 UI 批（评审 B5）。
- **F 约束**
  - 项目红线：Unity 序列化资产（Boot.unity/HUD prefab/PlayerSettings）一律走 unity-pipeline CLI（红线 4）；机制常量须有推导注释、禁硬编码（端点/PSK 走既有 endpoints 机制）；注释卫生（无日期戳/迁移叙述）；改 `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol,Transport/Security}`、生成读码面须重跑 `gen-build-hash.py`（**本批预期不动共享源集**，出包门禁 `--check-shared` 须保持绿）；真机/模拟器端到端验证必须走真实操作路径（红线 13，UI 面先可点性自检）。
  - 工具链：新增脚本一律 Python；`scripts/**/*.ps1` 禁新增；新工具须登记 `scripts/gate/toolchain.json`＋`Docs/工具链/工具索引.md`（否则 L0/Doc 门禁红）；UI 契约面变更走 `ui.widget-manifest` 等既有工具。
  - 本机 shell 通道损坏（run_shell_command 报 powershell not found）——wf/dotnet/python 命令经 Unity 进程内 `Process.Start` 直起（2026-10-19/23 实测配方）。
  - 工作区有并行线 WIP（GM/DevHUD 线 18 文件，含 `ProcedureBattle.cs`/`Boot.unity`/`ClientHost.cs`）——本批提交须逐文件隔离（严禁 `git add -A`；共享文件 hunk 级部分暂存）；**批④ 动 `Hud_Battle` prefab/重生成 UI 契约前，先核对 HUD prefab 归属**（GM/DevHUD 并行线可能同文件在场——重生成前 git diff 核归属，避免覆盖并行线改动）；**本批不触碰引导链**（方案 A 不动 `GameEntry`/`ClientHost`；`ClientHost.cs` 同为并行线在改文件）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 触摸意图源路线 | 依既有扩展点新写 `TouchIntentSource : IIntentSource`；HUD 触摸手势面（摇杆/拖动区/按钮，UI 契约节点）喂手势状态；**镜头增量经新增可选接口 `ILookDeltaSource` 泛化既有 `LookDeltaOf` 喂源缝（控制态门零改动）**；返回键接既有 Back 链 | 完全落在项目文档口径（修改指南明文"新平台设备源＝新写 IIntentSource"）；意图生产逻辑纯第一方、可 L1 单测；区域语义显式（左摇杆/右镜头/按钮各自手势区）；`Player.inputactions` 保持桌面口径不动（零再生成风险）；镜头门/引导链/部署读取契约均零改动 | 新增一个意图源实现＋手势面组件＋一个可选能力接口（代码量高于 B）；Input System 合成路径不经由（自管多指状态） | **推荐** |
| B Input System On-Screen 控件路线 | `Player.inputactions` 加 Touch/Gamepad 绑定＋HUD 挂 `OnScreenStick/OnScreenButton` 合成输入，`NewInputIntentSource` 零改动 | Input System 官方形态；意图源零改动；资产已在库可加绑定 | 依赖 On-Screen 控件合成设备机制（黑盒、版本行为差异）；意图生产退化为仅 Unity 侧可测（L1 不可达）；镜头"区域分离拖动"语义要靠 UGUI 射线层叠绕行、Fire 长按/多指并发行为不透明；改绑定须再生成 `Player.cs`；**On-Screen 控件与 `ui.widget-manifest` 契约工具链双范式并存，增加 UI 契约漂移面**（评审补充） | 不推荐（记录为备选） |
| C 键鼠外设先行 | Android 只出包＋键鼠/手柄外接操控，触屏后置 | 最小改动 | 与用户裁决 B 冲突（触屏是本批交付物）；模拟器上手测也不便 | 否决（用户已裁决） |

推荐方案与理由：**方案 A**。理由：①它兑现项目自建的 `IIntentSource` 抽象设计目标（第二个设备源实现即其通用性证明，未来 iOS/手柄是第三/四个实现，装配点唯一）；②意图生产链可 L1 单测（项目测试文化主面），UI 手势面可 EditMode 合成指针驱动；③`Player.inputactions` 与生成类零改动，消除 B 的再生成与双范式风险；④区域手势语义（动态摇杆＋镜头拖动＋按钮长按）在第一方代码里显式可控；⑤镜头接入＝`ILookDeltaSource` 可选接口泛化——控制态门、引导链、部署读取同步契约全零改动，改动面最小且避开并行线冲突文件。

## 设计要点

- 推进形态（五批——返回键/生命周期不依赖触屏，独立成批尽早暴露）：
  - 批①：工程设置＋构建链参数化＋DeployBake 安卓读通道＋冒烟 adb 通道＋模拟器 ARM 翻译预检＋`#if UNITY_ANDROID` 纪律守卫＋toolchain defines 校验面；
  - 批②：首次 Android 出包（development 通道映射确认）＋模拟器首启打通（"黑屏级"先于触屏）＋WaitForCompletion 实测判据＋引导期 DeployBake 读次数/耗时判据；
  - 批③：返回键接入 Back 链＋生命周期移动分支（独立小批，与批④可并行）；
  - 批④：触屏操控面＋UI 触控/SafeArea（验证＋最小修正口径）；
  - 批⑤：公网破窗＋联测验收＋确定性对账＋文档回写。
- **构建链参数化**：`build-player.py` 以 `profile.buildTargetGroup` 映射 `NamedBuildTarget`；产物名后缀与路径、artifactGlob 全由 profile 驱动（Android→`Builds/android/<productName>.apk`，profile 已含 `*.apk`）；后端切换 eval 参数化（Android+IL2CPP）；支持平台清单化（Standalone/Android 走内置包构建，其余仍拦）。`BuiltinBundleBuildInvoker.RunCore` 的 group/target 由调用方传入（顺带修 `RunForCandidate` 固定 Windows）。`toolchain.py` `-Profile` 一行修复（修复后断言：`toolchain.py build --profile ...` 退出码 0 且 builder 收到 `--profile`）。
- **工程设置批**（unity-pipeline/Editor 脚本写）：Android scriptingBackend=IL2CPP＋targetArchitectures=ARM64；**Android 脚本宏对齐机制**——与 Standalone 差集**逐项裁决**后对齐：补 `HOTFIX_ENABLE`/`UNITY_PIPELINE_URP`/`DOTWEEN`/`DOTWEEN_UITOOLKIT`/`HIDE_REFLECTION`；**不对齐** `MAGICACLOTH2`（插件经核实不在工程内，残留宏，登记审计）与 `ODIN_INSPECTOR*`（编辑器宏，Player 无效）；**守卫＝`python scripts/toolchain.py check` 新增 defines 校验面**（Python 读 `ProjectSettings.asset` 比对期望宏集——期望宏集清单入 toolchain 配置；**随批①落地**，消除"实现批定"开口）；**`profile.defines` 死字段裁决**：从 `android-il2cpp.json` 删除，工程设置为唯一真源（消除双真源）；锁横屏（Landscape Left+Right）；包名 `com.litegame.dev`；INTERNET Require；cleartext：`useCustomMainManifest`＋dev 通道 `usesCleartextTraffic=true`（发布级 https 归 C）；图形 API 序 GLES3 优先（模拟器兼容）；纹理压缩显式 ASTC；`activeInputHandler=2`（Both）**保持不动**（收敛为 Input System only 属全平台行为变化，评估项另批）；Android 默认质量档→Performant；移动 `targetFrameRate` 档在启动件单点设置（框架级"平台性能档"，60 起步，禁散写）；**`#if UNITY_ANDROID` 平台特判纪律守卫**（DisciplineScanner 目标扩列＋守卫用例，随批①落地——平台分叉只允许四处收敛点）。
- **DeployBake 安卓通道（评审 B1→二轮 blocker#1 修订）——同步读、零引导重构（方案 A）**：
  - `ReadArgs()` 保持**同步契约**：桌面/编辑器＝File 直读（不变）；安卓＝`UnityWebRequest` 读 `StreamingAssets/deploy.args`＋`WaitForCompletion()` 同步收流（Unity 2022.3 主线程同步完成 API——与 UniTask `.Wait()`/task 同步等待的死锁形态**不同**，首轮"禁同步等待"针对后者；deploy.args 为包内本地 jar 的几 KB 文本，是该 API 的规范用例，正常 <100ms）。**三处同步消费点（`ContainerModule:94`/`ContentModule:45`/`LoadWithBaked()`）调用形态零改动；无快照、无预载步、无时序契约、不触碰 `GameEntry`/`ClientHost` 引导链**。
  - **进程内 memo（评审三轮 #5）**：`ReadArgs()` 首读后缓存文本（同步、无时序契约——与被否决的"预载步＋快照"无关）→ 引导期阻塞读次数恒＝1；"一次性"日志＝进程级静态 flag（跨三消费点只打首条）。
  - 命名超时常量＋推导注释（机制常量纪律）：`BakeReadTimeoutSeconds = 2`（**期望上界非保证**——`UnityWebRequest.timeout` 对 `jar:file://` 本地读取通道是否强制不保证〔评审三轮 #2〕；读前后以 `Stopwatch` 记耗时日志，批②判据可量；不可中断长阻塞＝触发备选方案 B 的客观信号）。
  - 日志语义拆分：无文件＝正常路径 `DeployBake missing`（一次性）；读异常/超时＝`DeployBake read failed`（一次性 Warning）——二者都退化为"无烙入"（Loader 走默认值，启动链既有 fail-closed/最小错误 UI 兜底）。读发生在引导早期——标记用 `Debug.Log`（Log 门面/容器由引导件经 ClientHost 建立，此窗口未装配）。
  - **备选方案 B（仅当 A 实测受阻，回方案重裁决）**：引导序列重构——预载 await 置 `RunBootstrapAsync` 之首＋`_fsm.Start` 移入异步续体（`Start()` 改等待标志）；**不默认采用**：`Start()` 的 `TakeContainer()` 在 `_container == null` 时**直接抛 `InvalidOperationException("未装配")`**（早于 `_fsm` 断言、无 ProcedureError 兜底——评审三轮 #3），B 必须改写 `Start()` 等待/断言语义、影响面覆盖全部启动帧时序；`ClientHost.cs` 并行线冲突为附带因素（非首因）。
  - A 方案实测判据（批②）：引导期 `DeployBake` 阻塞读次数＝1、`Stopwatch` 耗时 <2s、无异常、值正确（AC3 三面验证）；受阻即按备选方案 B 回方案重裁决，不静默换道。
  - PSK 经 `endpoints["envelope.key"]` 烙入（封闭测试口径）。候选根/激活记录已核实为 persistentDataPath 可移植（现状 B 节），本批仅在模拟器实测激活记录读写回写挂账。
- **触屏输入面**（方案 A）：
  - **分层落位（评审三轮 #1——L1 红线）**：`ILookDeltaSource { Vector2 LookDelta { get; } }` 住 `Assets/LiteClient/Adapters/Platform.Unity/`（与 `NewInputIntentSource` 同程序集——`UnityEngine.Vector2` 依赖**不出 Runtime**；接口若照 `IAimWorldSink` 先例放进 `IInputService.cs`〔Runtime〕，会随 `Tests/LiteSim.Core.Tests` 的 Runtime 源链接集把 `Vector2` 拖进纯逻辑工程 → L1 编译红）；手势状态＝纯值 `TouchGestureState`（float 对，住 Runtime）＋`TouchIntentSource : IIntentSource`（住 Runtime、进 L1 链接集，意图映射纯逻辑可测）；镜头增量＝Platform.Unity 侧薄适配器 `TouchLookAdapter : ILookDeltaSource`（读同一手势状态→`Vector2`）。**L1 链接集不新增引擎类型**。
  - `TouchInputSurface`（HUD 层，`Hud_Battle` 契约节点：左半区动态摇杆〔拇指落点起摆〕＋右半区镜头拖动区＋Fire/Aim/Reload 按钮）→ `TouchGestureState`（显式写入方法，非 public 可变状态袋）→ `TouchIntentSource` 产同形态意图（Move 轴归一/Fire held/Aim held/Reload 沿），经 `InputService` 既有三件门——**UI 模态零意图不变式与键盘源同门**；
  - **镜头（评审 B2→二轮 #2 修订）**：控制态门（`pointerLocked` 谓词）**零改动**；`BattleViewRig.LookDeltaOf` 工厂（现 L890-895 硬绑 `is NewInputIntentSource`）改为按 `is ILookDeltaSource` 取委托——`NewInputIntentSource` 与 `TouchLookAdapter`（触屏侧实现）双双接入；意图源选型（`InputModule.SetSource`）与镜头喂源（`LookDeltaOf`）随同一单源切换自动一致；`_pointer.Lock()/Unlock()` 仍由控制态谓词驱动（安卓 Cursor API 空操作，`PointerLockService` 注释注明）；模态/死亡/加载门因此覆盖**所有**镜头输入源。**装配点澄清：镜头喂源缝在 `BattleViewRig.LookDeltaOf`（非 InputModule——InputModule 只选意图源）**；
  - 触屏灵敏度走既有 `LookTuning` 调参机制（独立触屏档位常量＋推导注释；GM 实时覆写→实机定档后写回机制常量——与准心调参同型挂账）；ADS 收敛复用既有档；
  - 装配：`InputModule` 按运行平台/设备能力选设备源（`RuntimePlatform.Android` 或 `Touchscreen.current != null` → 触屏源；桌面不变，**单源不合并**——`InputService.SetSource` 单源赋值语义）；
  - 批④ 动 `Hud_Battle` prefab/重生成契约前先核对归属（约束 F）；按钮可点性自检（红线 13）适配触点。
- **UI 触控（验证＋最小修正口径，评审 B5）**：PlayMode 合成触摸验证 `InputSystemUIInputModule` 默认动作集的触摸指针覆盖（Touchscreen 属 Pointer——预期可点，实测为准；不足则显式绑定触摸动作）；战斗 HUD 手势区挂 SafeArea 接收器（`androidRenderOutsideSafeArea=1` 现状下由策略收缩）；TMP 全局 fallback 表补一个 EditMode 断言用例（不再扩大为"补核实"开口）；CanvasScaler 1920×1080 match 0.5 以设备档预览工具核对 16:9~20:9 表现——**结论只登记（含挂账 UI 批的分档建议），本批不做分档/重排**。
- **返回键（批③）**：Input System 安卓把系统返回键映射 `Keyboard/escape` → 既有 `UiInput.BackPressedThisFrame`+`UIFocusCoordinator` Back 链预期直接生效——模拟器实测；不生效则补显式映射，仍接同一 Back 链（不新开开关面）。
- **生命周期（批③）**：`AppLifetime` Pause→主动断开（KCP 心跳停后服务端自然超时踢出亦可，二择一实现批定，倾向"主动 LeaveMatch/断链"以立即释放席位）；resume→既有断线自动重连链（C2 已交付）恢复对局或回大厅重进；被系统回收→冷启走正常启动链。低内存回调登记日志面（深度治理归 G4）。
- **模拟器 ARM 翻译预检门（批①落地、两段式，评审 B3＋三轮 #4）**：①`adb shell getprop ro.product.cpu.abilist` 快检（秒级，**先于本批首次 649s 出包**——AC1 顺序断言指此段；abilist 不含 arm64 即换镜像）；②**首个出包产物自身即探针**（`install`＋`am start` 失败＝镜像翻译不可用，换镜像重跑——代价一次出包；若手头有任一现成 ARM64 APK 可选作前置探针）。`libxlua.so` 无 x86_64 ABI 登记为已知边界（本批不补——ARM64-only 已裁决）。
- **公网破窗**（限时窗口）：云侧按 10 手册——安全组开 `5000/tcp`＋`17777/udp`；Meta bind 增公网（配置项以环境变量为准，改后重启任务计划）；`lobby.advertise_host=公网IP`；测后回收安全组。客户端：`android-il2cpp` profile 的 `endpoints` 烙 `meta.url=http://<公网IP>:5000`＋`envelope.key=<PSK>`（构建前填 `*.local.json` 变体，**不入库**）；GM 端点不破窗。安全面：信封 AES-256-GCM/票据/三层限流已有＋封闭测试红线；日志观察窗口期。
- **冒烟 adb 通道**：`player-smoke.py` 增 `--platform android`——adb 用编辑器自带 `<editor>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`（无系统依赖）；**设备寻址双形态参数化**：`--adb-serial <serial>` 与 `adb connect <host:port>`（各模拟器端口不同，默认值文档化不硬编码）；`install -t -r` → `am start` → logcat 轮询既有真标记（`BootstrapError` 失败标记/`[Asset] ready` 成功标记，`player-smoke.py:107` 同源）——**读取前 `logcat -c` 清缓冲或以 `-T` 只取本次会话新行**，防上一局残留标记造成假绿 → PASSED → 卸载清理；**冒烟通道固化"会话日志工件导出路径"**（logcat 导出文件——AC5/AC6 引用同一工件，减少三处各写 adb 参数的漂移）；分支纯 Python 可单测（fake adb 输出）。
- **确定性对账**：`IeeeProbe` 为 LiteSim 运行时纯类（`System.BitConverter`，IL2CPP Player 可直接运行）——安卓端经**新增 GM 诊断动作 `ieeeprobe`**（GM 页动作清单登记，名字定死）触发 `BuildText()` 产出基线文本（Sqrt 逐值＋四则相邻对＋Chain checksum），与 .NET 侧基线（`Tests/LiteSim.Core.Tests/IeeeBaselineSpec`）**离线逐位比对**（冒烟工件导出后桌面比对）；触发入口依赖 development 构建——批②确认 `build-player.py` 的 `channel: development` → `EditorBuildOptions.development` 映射（缺则补 profile 字段映射，落批②）；差异即回方案（预期一致——SimMath/SimTrig 已工程化；若 IL2CPP C++ 编译器引入 fp-contract 差异，记录并评估关闭路径）。
- **失败与边界处理**：模拟器无 ARM 翻译→预检门当场暴露（换镜像/借真机/云真机），出包链仍可先行交付；cleartext 被拦→症状为 UnityWebRequest 错误/连接失败日志→manifest 修正；IL2CPP 编译时长（历史 649s）与内存→增量构建；endpoints 空→既有 fail-closed 删除 deploy.args→`DeployBake missing`→启动落入最小错误 UI（既有行为，作为"配置缺失"验证路径保留）；桌面带触摸屏的选源边界→单源策略文档化；公网窗口遭遇扫描→限流已有＋窗口期观察＋测后关闭；WaitForCompletion 个别设备异常→判据（读次数/Stopwatch 耗时/异常/值正确）暴露后按备选方案 B 回方案重裁决。

## 通用性与扩展性

- 根因/共性问题：安卓适配的各断点都是"桌面先行"时**平台假设未参数化**的沉淀（产物后缀/后端切换/目标组写死、File 读 StreamingAssets、镜头喂源绑死单一意图源实现）——本批逐点把"平台"从代码常量提升为 profile 驱动/扩展点选择，而非堆 `#if UNITY_ANDROID`。
- 复用与扩展点：`IIntentSource`（第二个实现＝触屏源，兑现抽象设计目标；iOS/手柄为后续实现）；`ILookDeltaSource` 可选能力接口（镜头喂源的第二实现——后续手柄摇杆镜头同缝接入；**注意缝变宽**：自瞄验证件/测试替身若实现该接口，非零 `LookDelta` 会带动相机——排查时防误判为回归，评审三轮意见）；`AppLifetime` 钩子（移动分支挂既有链）；Back 链（返回键接既有 `UIFocusCoordinator`）；SafeArea 接收器/策略（HUD 手势区直接复用）；endpoints 烙入机制（安卓通道只换读取机制）；UI 契约工具链（触摸控件走既有 `ui.widget-manifest`/页面契约）；出包 profile（平台字段已是数据，本批补齐消费端）；`LookTuning` 调参机制（触屏灵敏度档复用）；FileSys/persistentDataPath 抽象（候选根天然可移植，零改动复用）。
- 避免硬编码/特判的约束：端点/PSK 只经 endpoints 机制；触屏灵敏度/按钮区布局入常量或表（机制常量须有推导注释，经 LookTuning 可调）；adb 路径/包名/serial/模拟器端口入脚本参数（默认值文档化）；平台差异收敛点＝构建 profile＋`DeployBake` 单文件＋`InputModule` 装配＋`LookDeltaOf` 喂源选择，四处之外不得出现平台分叉——**`#if UNITY_ANDROID` 纪律守卫（DisciplineScanner 目标扩列）随批①落地机制化**，不靠自觉。
- 反例验证：触屏源就位后桌面键盘源回归不变（L2 全量）；选源逻辑对"无触摸屏桌面"与"模拟器"两态成立；出包链对 Windows profile 零行为变化（windows-x64 出包回归冒烟）；镜头门对"模态打开时触屏拖动"（不吃）与"对局中触屏拖动"（吃）两态成立；`ILookDeltaSource` 泛化后桌面 `NewInputIntentSource` 路径行为不变（喂源值同源——L1＋AC4.2 桌面符号回归断言）。

## 测试策略

- 测试层级与关键用例：
  - L1（纯逻辑，新增红测先行）：`TouchGestureState`＋`TouchIntentSource` 映射用例（摇杆矢量→Move 轴归一；Fire/Aim held；Reload 离散沿；UI 模态经 `InputService` 门写零意图——复用既有门用例形态；**L1 链接集不新增引擎类型**）；`DeployBake` token 解析（合并序回归；安卓文本分支）；`build-player.py` 平台参数化（NamedBuildTarget 映射/产物后缀/门禁清单）纯 Python 单测；`player-smoke.py` adb 分支单测（fake adb 输出）；toolchain defines 校验面单测（期望宏集比对）。
  - EditMode/PlayMode（Unity 侧）：手势面合成指针驱动（既有 EditMode 裸 EventSystem＋合成指针先例）；镜头喂源切换（模拟 `ILookDeltaSource` 断言 `AddLookDelta` 收到触摸 delta、模态门拦截下不吃、`NewInputIntentSource` 现值行为不变）；SafeArea 四形态回归；Ieee 基线对账工具回归；InputSystemUIInputModule 触摸可点性（PlayMode 合成触摸）；`#if UNITY_ANDROID` 纪律守卫正/负例。
  - 端到端（模拟器）：ARM 翻译预检→出包→adb 冒烟真标记→公网破窗后登录/入房/触屏一局/切后台重连——按红线 13 真实操作路径（adb `input tap/swipe` 系统级注入属真实输入事件），UI 面先可点性自检。
- 如何先失败后通过：`TouchGestureState`/`TouchIntentSource`/`DeployBake` 安卓分支用例在实现前运行即红（类型缺失/行为缺失）；wf `red_commands` 补对应定向命令；构建脚本单测先红（断言新映射函数不存在→实现后绿）。
- 回归面：Windows profile 出包＋冒烟回归（`BuildProfiles/windows-x64.json`＋`player-smoke.py --exe-path`）；L1 全量（含纪律扫描）；L2 全量；Doc 门禁（文档回写后）。

## 验收标准

- [ ] AC1 构建链贯通：`python scripts/build/build-player.py --profile BuildProfiles/android-il2cpp.json` 全链通过（restore-check/l0/`gen-build-hash.py --check-shared`/Android 目标内置包/构建 0 错）并产出 `Builds/android/Test.apk`（当前 productName=Test；工程设置批若改 productName 以实名为准）；**产物 ABI 断言**：解包检查 `lib/arm64-v8a` 存在（`aapt dump badging` native-code 含 arm64-v8a 或 unzip 清单核），不只看"有 apk"；`toolchain.py build --profile ...` 修复后退出码 0 且 builder 收到 `--profile`；`python scripts/toolchain.py check` 绿；**Windows 回归**：`build-player.py --profile BuildProfiles/windows-x64.json` 出包＋`player-smoke.py --exe-path ...` 冒烟不受影响；**ARM 翻译预检①（abilist 快检）先于本批首次 649s 出包执行**（顺序断言）。
- [ ] AC2 模拟器启动（含预检）：**前置预检门**——①`adb shell getprop ro.product.cpu.abilist` 含 arm64（秒级快检，不满足即换镜像、不进构建）；②首个出包产物 `install`＋`am start` 自身即探针（失败＝镜像翻译不可用，换镜像重跑）；`player-smoke.py --platform android --adb-serial <emulator-serial>`（另支持 `adb connect <host:port>` 形态，默认值文档化）判 PASSED——logcat（清缓冲后本次会话）出现成功真标记 `[Asset] ready`（或同源变体）、不出现 `BootstrapError`；多模拟器并存时 serial 参数保证可复现；**冒烟通道固化会话日志工件导出路径**（AC5/AC6 引用同一工件）。
- [ ] AC3 安卓烙入生效：启动日志出现 `DeployBake missing`/`DeployBake read failed` 之外的正常装载证据（含引导期读取耗时读数），且配置装载日志输出 `meta.url` 实际值（或 GM 网络诊断页显示该值）＝endpoints 烙入的公网端点；公网破窗后 Meta `/live` 返回 HTTP 200 且响应体含 `live`（`Server/MetaServer/Host/HealthEndpoints.cs:32` 口径），游客登录成功；无烙入时为 `DeployBake missing`＋最小错误 UI（既有 fail-closed 路径回归）。
- [ ] AC4 触屏操控可用（红线 13 口径，逐项拆分）：
  - AC4.1 动态摇杆推动→角色移动（GM 网络诊断输入帧 Move 分量非零或角色位移可见）；UI 模态打开时摇杆推动被 `!IsBlocked` 门拦截（零意图＋零位移）；
  - AC4.2 右半屏拖动→镜头 yaw/pitch 变化（GM 相机/朝向读数），方向符号与桌面一致（拖右→镜头右转，同桌面 Mouse X 同符号）；**模态/死亡段拖动不转镜头**（控制态门覆盖触屏源——评审 B2 修复的回归项）；
  - AC4.3 开火按钮按住→连续开火（HUD 弹药递减/开火日志）；
  - AC4.4 ADS 按钮按住→瞄准态（移动限速生效——速度降档可见；相机接管生效＝FOV/相机优先级变化的日志或 GM 读数）；
  - AC4.5 换弹按钮点按→换弹**完成态**（换弹有时长，判完成态——弹匣回满，非瞬时断言）；
  - AC4.6 触点点击菜单（限定三页：登录页、设置页、对局退出确认）可点可操作（可点性自检＋实操）；
  - AC4.7 Android 系统返回键→既有 Back 链（模态先关/页面回退）。
- [ ] AC5 联测一局：模拟器客户端与 PC 测试者（隧道链路不动）同房完成一局至结算回大厅——服务端结算记录可查（服务端日志结算横幅＋Meta `/matches/result` 提交与 `/matches` 归档查询，11 手册 §8 观测口径；证据取自 AC2 固化的会话日志工件）；**切后台≥30s 回前台后 30s 内恢复对局**（重连链日志与对局续玩证据）；"回大厅重进"不作为通过路径，仅作失败时的容忍说明留痕。
- [ ] AC6 确定性对账：安卓端为 **development 构建**（批②确认 `channel: development` → `EditorBuildOptions.development` 映射，缺则补），经 **GM 诊断动作 `ieeeprobe`**（GM 页动作清单登记）触发 `IeeeProbe.BuildText()` 产出基线行（经 AC2 固化的工件通道导出），与 .NET 侧基线**逐位一致＝通过**；不一致＝本 AC 不成立，差异记录与回方案裁决属变更证据（不是通过条件）。
- [ ] AC7 门禁与纪律（枚举，全部字面命令）：`python scripts/gate/test.py --suite L1 --profile PullRequest` 全绿（含纪律扫描 0 违规——**含新增 `#if UNITY_ANDROID` 平台特判守卫**〔批①落地，正/负例〕）；`python scripts/gate/test.py --suite L2 --profile PullRequest` 全量绿（EditMode+PlayMode＋Player 冒烟）；`python scripts/gate/test.py --suite Doc` 绿；`python scripts/toolchain.py check` 绿（新工具登记齐＋**新增 defines 校验面**——期望宏集比对，批①落地）；`python scripts/codegen/gen-build-hash.py --check-shared` 不变绿（本批不动共享源集）。
- [ ] AC8 回写与挂账（清单）：`Docs/待办总览.md`（G4 Android 挂账状态——模拟器已验项与真机仍缺项分开）；`Docs/项目说明/11-联机测试流程.md`（安卓测试者接入节）；`Docs/项目说明/10-Windows云主机部署.md`（公网破窗操作与回收）；`Docs/项目说明/07-本地联测.md`（安卓本地验证节）；`Docs/技术总结/10-构建部署与云侧运维.md`（profile 表安卓行状态）；两份质量登记表按格式回写本批新问题（含 MAGICACLOTH2 残留宏、`profile.defines` 死字段清理登记）。

## 风险与回滚

- 风险：
  1. ARM 翻译层兼容——**Burst 使用面先核清单，无消费者即从风险面移除**；真实原生面＝xLua so＋IL2CPP AOT（`MAGICACLOTH2` 插件经核实不在工程内，该风险点撤销）；`libxlua.so` 无 x86_64 ABI——无翻译层镜像当场被预检门拦下（评审 B3）——缓解：预检门提前暴露，换镜像/借真机/云真机裁决。
  2. **`UnityWebRequest.WaitForCompletion()` 阻读风险（`timeout` 对 jar:file 本地通道不保证生效、主线程阻塞不可中断）**——批②以实测判据（引导期读次数＝1、Stopwatch 耗时 <2s、无异常、值正确〔AC3〕）暴露；不可中断长阻塞＝触发备选方案 B（引导序列重构）的客观信号——受阻即回方案重裁决，不静默换道。
  3. IL2CPP 首编译时长/内存（历史 649s）——增量构建与夜间窗口。
  4. 公网暴露安全——信封/票据/限流已有＋限时窗口＋GM 不破窗＋测后回收安全组；泄露风险（PSK 烙入 APK）以封闭测试红线＋测后轮换兜底。
  5. cleartext 与 targetSdk=Auto——dev 通道 manifest 放行明文；未来发布级需 https（登记 C 级前置）。
  6. 触屏手感（灵敏度/布局/摇杆死区）主观面——交付可调常量/表位（经 `LookTuning` GM 覆写），实机手感定档归用户（准心调参同型挂账）。
  7. 并行线 WIP 在场（GM/DevHUD 线 18 文件，含 `ProcedureBattle.cs`/`Boot.unity`/`ClientHost.cs`）——提交逐文件隔离/hunk 级暂存；批④ 动 `Hud_Battle` prefab/重生成契约前先核对该 prefab 与并行线的 git 归属；**本批不触碰引导链（方案 A 不动 `GameEntry`/`ClientHost`）**。
  8. `ILookDeltaSource` 接缝泛化的回归面——`NewInputIntentSource.LookDelta` 现值行为不变为验收断言（L1＋AC4.2 桌面符号回归）；泛化使缝变宽（自瞄验证件/测试替身实现该接口的语义提示已入通用性节）。
- 监控/发现：模拟器 logcat 真标记（清缓冲后）与 GM 网络诊断页；`DeployBake missing/read failed` 启动标记＋`Stopwatch` 读取耗时（Debug.Log，引导早期）；云侧限流日志窗口期观察；L1/L2 门禁；确定性差异由 Ieee 对账工具暴露。
- 回滚（清单）：
  1. 构建链改动全部 profile 驱动，Windows 路径行为不变（回滚＝恢复硬编码三处，或不消费新 profile）；
  2. 触屏面独立装配（`InputModule` 选回键盘源；`LookDeltaOf` 接口判回旧形态即回桌面镜头——接口泛化保留亦可零行为回退）；
  3. DeployBake 安卓读分支与超时常量**单文件内成对回退**（桌面路径从未改动；删分支即回 File-only 形态）；
  4. 云侧破窗回滚＝安全组回收＋Meta bind 还原（改配置重启任务计划即生效）；
  5. **endpoints 测后清理**：`*.local.json` 变体不入库、测后清空公网端点回 `10.8.0.1` 口径（防下批 Windows 出包烙入公网值）；
  6. 云侧 hosts/`lobby.advertise_host` 还原原值。
