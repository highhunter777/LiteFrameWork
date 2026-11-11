# 代码评审（主代理）：客户端重写 P0 骨架

> 评审对象：`.workflow/active/client-rewrite-architecture` 本轮全部改动
> 基线：git HEAD `8ca025b3774fe30b4663e570cd4fe58778127098` + 工作区改动
> 评审方式：主代理，基于 `git status` / 实际文件 / 实际命令输出

## 变更范围

- 新增 `Assets/LiteGame/Next/`：`LiteFrameworkNext` AOT、`LiteGameNext.Bootstrap` AOT、`LiteGameNext` hot；`ClientApp`/`AppFlow`/`UiRuntime`/`PageBase`/`PageSpec`/`PageHandle`/`BindName`/`EditorPageFactory`；Placeholder/Login/Error 三页；Account 占位功能。
- 新增 `Packages/com.litegame.framework/Next/`：`Log`/`Lifetime`/`IClientApp`/`Platform`/asmdef。
- 新增 `Assets/Scenes/BootNext.unity`、`arch/client-roots.json`、`scripts/gate/client-next-change-budget.py`、Next 测试（EditMode 26 / PlayMode 1 / L1 静态 9）。
- 修改：`ProjectSettings/EditorBuildSettings.asset`（BootNext 入列，Boot 仍 index 0）、`arch/units.json`、`ScanTargets.Generated.cs`、三个旧守卫脚本（Next 豁免）、测试 asmdef、设计文档/索引、`.workflow/config.json`（红测命令）。
- 未改旧业务代码、旧 `Boot.unity`、旧测试。

## 正确性与边界

- AOT/hot：`LiteFrameworkNext`/`LiteGameNext.Bootstrap` AOT，`LiteGameNext` hot；`hot-boundary --strict` 通过；`LiteGameNext.Bootstrap` 反射装载 hot `ClientApp`，无 AOT→hot 编译边。
- 页面生命周期：`UiRuntime.OpenAsync` 创建（非激活）→ bind → 激活 → 入栈 → `OnShow`，由 `ClientNextUiRuntimeTests` 的 16 例钉住；关闭会 `SetActive(false)`（本轮修掉的缺陷）。
- 流程：`AppFlow` 是线性 async 调用栈；取消静默、异常开 ErrorPage；无阶段机/页面服务定位器。
- 关闭：`ClientApp` 的 `Lifetime` 逆序释放 `AccountFeature → UiRuntime`；`Lifetime` 的逆序/异常聚合/已释放 Add 有 EditMode 用例。
- 架构登记：`arch/client-roots.json` 为路径单源；旧 AR1/AR2 守卫只对 Next 豁免，旧目录基线不变（`--all`/默认输出一致）。
- 共享源未触碰：`Assets/LiteGame/Modules/Config/Table/*` 与 `Meta/WireSubModule/**` 未移动；P1 再迁出。

## 测试充分性

- L1 1895/0；`ClientNextArchitectureTests` 9/9；EditMode `ClientNext` 26/26；PlayMode `ClientNextBootTests` 1/1（真实 EventSystem 射线 + 真实指针点击）。
- `dir-flat`/`namespace-path`/`change-unit`/`hot-boundary`/`arch-check`/Doc 全绿。
- **不足（非阻塞）**：S3 HUD / S4 设置项未在 P0 实测；`PageSpec.Address` 当前是 AssetDatabase 资源路径，P1 接 YooAsset `AddressByFileName` 时需要统一（否则地址不一致）。已在 build-log 待办中登记。
- 未跑全量 EditMode（历史 32 失败基线）；本轮只跑新增 filter + 编译覆盖，符合 plan 口径。

## 通用性与扩展性

- 新页面只需页面类 + prefab + 所属功能打开点；无中央注册/代码生成。
- 新功能只需功能目录 + `ClientApp` 构造一行；平台服务按构造参数注入，不传聚合体。
- 旧机制未回潮：Next 子树无 `PageIds`/`UIPageCatalog`/`UIBindIndex`/`BindGenerator`/`IClientSegment`/`ServiceContainer`/`PageServices`/`ProcedureId`（静态守卫 9 例）。
- 复用边界：`LiteSim`/`LiteNet`/`LiteSync` 未动；旧 `LiteFramework` 只作对照，Next 不引用其旧契约。

## 安全与性能

- 无新增 public static 可变状态（`GameEntry.s_booted` 为引导幂等守卫，编辑器 SubsystemRegistration 复位；`UiRuntime`/`Platform` 状态为实例字段）。
- 页面控件索引在 `OnCreate` 一次扫描；`UiRuntime.Tick` 用快照数组，避免遍历中开关页面失效。
- 无反射取私有服务；`GameEntry` 的反射只用于加载 hot 入口（设计允许）。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| CR-1 | major | 缓存页关闭只 `Hide()`，GameObject 仍激活，会留在屏幕上 | `CloseEntry` 增加 `SetActive(false)`；测试加断言 | 已修 |
| CR-2 | major | `BootNext` 无 EventSystem，真实按钮不可点 | T4 加 EventSystem + StandaloneInputModule；PlayMode 真实点击用例 | 已修 |
| CR-3 | minor | `GameEntry` 裸 `.Forget()` 触发 TaskObservationGuard | 改带日志回调的 `.Forget(ex => ...)` | 已修 |
| CR-4 | minor | `PageSpec.Address` 用资源路径，P1 YooAsset 地址需统一 | 记录 P1 待办；P0 编辑器装载正常 | 待 P1 |
| CR-5 | minor | `AppFlow` 错误页重试/回登录 P0 只记录 | 记录 P1 待办；不宣称完整恢复 | 待 P1 |
| CR-6 | minor | 旧 AR1/AR2 守卫对 Next 误报 | 守卫读 `arch/client-roots.json` 跳过 Next；旧基线不变 | 已修 |

## 复测记录

- `python scripts/gate/test.py --suite L1 --profile PullRequest` → 1895/0。
- `python scripts/gate/unity-tests.py --mode editor --filter ClientNext` → 26/26。
- `python scripts/gate/unity-tests.py --mode playmode --filter ClientNextBootTests` → 1/1。
- `python scripts/gate/test.py --suite Doc` → PASS。
- `dir-flat`/`namespace-path`/`change-unit`/`hot-boundary`/`arch-check`/`client-next-change-budget --check` → 全绿。

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 备注：PASS 针对 P0 骨架（AC1/AC2 与 AC3 的 S1/S2/冷启动）；S3/S4 未实测，AC3 不能宣称全量通过，已在 build-log 与风险中登记。
