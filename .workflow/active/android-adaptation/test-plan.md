# 测试计划：安卓平台适配（android-adaptation）

## 测试策略

- 分层：L1 纯逻辑（Python 单测/纪律扫描/契约）→ EditMode/PlayMode（Unity 内行为）→ 端到端（模拟器实测，红线 13 真实操作口径）。
- 先写红测的范围：无模拟器即可验证的纯逻辑/脚本任务（T1、T2、T4、T5、T6、T7 与 T3 的读回校验）；模拟器依赖任务（T8 起）以"替代验证"登记（端到端实操证据＋日志工件），PlayMode 合成输入用例可先行编写。
- 每任务实施前先跑其定向用例（红），实施后同命令转绿；全批收口跑 L1/L2/Doc/toolchain check/`--check-shared`。

## 用例清单

### T1 构建链平台参数化（本批首件，红测先行）

- `scripts/tests/test_build_player.py::BuildPlayerPlatformContractTests`
  - `named_build_target`：Android→Android、Windows→Standalone、未知平台→RuntimeError、显式 `buildTargetGroup` 优先；
  - `player_extension`：.apk / .exe / 未知平台拒绝；
  - `artifact_glob`：profile 显式值优先；缺省按平台（*.apk / *.exe）；
  - `bundles_supported`：Android/Windows true、其余 false；
  - `backend_switch_cs`：Android 档用 `NamedBuildTarget.Android`＋IL2CPP（且**不含** Standalone）；Windows 档保持 Standalone；backend=Mono 的 profile→`ScriptingImplementation.Mono`；
  - `build_output_expr`：Android 以 `".apk"` 结尾且含 profile.output；Windows `".exe"`；
  - `bundles_cs`：Android 档 `BuildTarget.Android`、Windows 档 `BuildTarget.StandaloneWindows64`；
  - profile 契约字段：`android-il2cpp.json` 的 platform/artifactGlob 与档位一致。
- `ToolchainBuildCommandTests`
  - `build_command` 使用 `--profile`（含值）、不得出现 `-Profile`；无 profile 时不带 `--profile`。
- 后续任务（实施时补写用例）：T2 toolchain defines 校验面＋DisciplineScanner 正/负例；T4 DeployBake 安卓文本分支/合并序/memo；T5 player-smoke fake adb；T6 `ILookDeltaSource` 桌面行为不变＋模态门；T7 `TouchIntentSource` 映射＋零意图门。

### T5 android 冒烟通道（红测已备）

- `scripts/tests/test_player_smoke.py::PlayerSmokeAndroidContractTests`
  - `parse_args`：`--platform android` / `--adb-serial` / `--package-name` / `--log-artifact` 可解析；默认 `platform=desktop`；未知平台 argparse 拒绝；
  - `adb_serial_args`：serial→`["-s", serial]`、空→`[]`；
  - `adb_path_from_editor`：编辑器 exe 路径→编辑器自带 platform-tools/adb.exe；空路径→RuntimeError；
  - `android_package_name`：与 ProjectSettings.asset 的 Android applicationIdentifier 一致（不硬编码值——T3 会改包名）；
  - `evaluate_markers`：命中返回标记、未命中 None（桌面/android 共用判定）；
  - `session_log_artifact`：默认落在 `TestResults/android-smoke/*.log`；
  - CLI：`--platform android --adb-path 不存在` 快速失败（退出码 1、消息含 adb）。

### 模拟器依赖任务的替代验证（T8～T15）

- 判据：AC2/AC3/AC4/AC5/AC6 的端到端观测量（真标记 logcat、GM 读数、服务端结算记录、Ieee 工件逐位比对）——按 `acceptance.md` 逐条执行；不设"接口存在即过"。

## 预期失败证据

- T1 红测命令：`python -m unittest discover -s scripts/tests -p test_build_player.py -v`
- 实现前预期：`AttributeError`（`named_build_target` 等函数不存在）/断言失败；实现后：全绿。
- 证据落盘：`wf verify --red` 写 `evidence/red.json`（本次 `red_commands` 只含 T1 定向用例——既有 UI 运行的红测证据已在各自运行目录留档，不重复执行）。

## 运行方式

- 单任务定向：`python -m unittest discover -s scripts/tests -p test_build_player.py -v`
- 全量收口：`python scripts/gate/test.py --suite L1 --profile PullRequest`；`--suite L2`；`--suite Doc`；`python scripts/toolchain.py check`；`python scripts/codegen/gen-build-hash.py --check-shared`
- Unity 侧（后续任务）：`python scripts/gate/unity-tests.py --mode editor|playmode --filter <Fixture>`
