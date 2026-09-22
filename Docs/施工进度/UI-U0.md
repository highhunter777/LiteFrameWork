# UI U0 施工进度：正确性止血

> 依据：《UI框架总设计》§13 唯一实施路线 U0 行（交付：UI-01/02/04/07；初始化失败回滚；全关/DevReload 防旧引用；补完整页面回归。退出条件：真 Lua 首开/关闭/复用成功；列表可往返；Covered/Paused 可清；旧 env 零访问）与 §4/§5/§8/§10 目标契约、§12.1 分层用例。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

## 批次范围

| 项 | 范围 | 状态 |
|---|---|---|
| UI-01 | Lua 实例工厂 + 生命周期回调显式传 self + 所有权分开 | 已完成（见下） |
| UI-02 | 首次与复用合流：复位视觉、重新排序、复用同判 Replace、复用播入场 | 已完成（见下） |
| UI-04 | Close/CloseAllOpen 覆盖 Covered/Paused；遮盖源由全部打开的全屏推导 | 已完成（见下） |
| UI-07 | VirtualList 重写为窗口复用；隐藏项可回窗、节点有界、监听对称（顺带 UI-08 的重复绑定/监听增长） | 已完成（见下） |
| 回滚 | OnInit/首次 OnShow 失败撤销登记、释放 Lua 引用、销毁对象并报失败 | 已完成（见下） |
| env 重建 | 全关 + 逻辑落空（旧 Lua 引用释放）→ 复用按当前注册表重解析 | 已完成（见下） |
| 完整页面回归 | 真 Lua + 真 prefab + 真配置投影的 EditMode 集成用例 | 已完成（见下） |

## 施工记录

### 2026-09-23 · U0 全项交付（已完成，待提交）

**代码位置**：

- `Assets/LiteGame/Runtime/Shell/UI/LuaBehaviourAdapter.cs`——注册表存**模块**，适配器即**实例工厂**：构造期执行 `module.new()`（无 `new` 的旧式表退化为"模块自身即实例"）；七个回调显式传 self（预置数组复用，热路径零分配）；`self.ui` 挂实例；`Release()` 只释放实例/回调/ui 门面表，**不 Dispose 共享模块**（§5.1 所有权分开）；`CallRaw` 加 released 守卫（§10.2 旧 env 零访问）。
- `Assets/LiteGame/Runtime/Shell/UI/UIService.cs`——首次与复用合流为一条管线（`ReuseAsync` + 公共尾段 `FinishOpenAsync`：遮盖重算 → Replace 推导 → 播表现 → 关旧全屏）；复用走 `PrepareForShow` 复位 + `AssignDepth` 重排；ShowAsync 幂等口径扩到 Covered/Paused（不暗中重跑 OnShow）；打开失败回滚（撤登记 → `DisposeFailedOpen` → 抛失败）；`CloseAsync`/`CloseAllOpen`/`IsOpen` 覆盖 Covered/Paused；`RecomputeCovering` 遮盖源 = 仍打开的全屏（含 Covered/Paused）；新增 `DropAllLogic()`（env 重建前置）；新增可注入面 `loadPrefab`（fake loader 验收口，U1 换内容服务租约）。
- `Assets/LiteGame/Runtime/Shell/UI/UIForm.cs`——`PrepareForShow()`（SetActive + alpha/interactable/blocksRaycasts/位置基线复位，基线取实例化时刻）；`EnterClosing` 接受 Active/Covered/Paused；`EnterActiveFromLoading` 返回成败（经新 `SafeCall.TryInvoke`）；`DropLogic`/`DisposeFailedOpen`（EditMode 用 `DestroyImmediate`——`Object.Destroy` 在编辑态只记 error 不生效）。
- `Assets/LiteGame/Runtime/Shell/UI/Widgets/VirtualList.cs`——按 §8.2 重写为**窗口复用**：节点数 = ceil(视口/步长) + 2×Overscan + 1（不随数据量）；由 Content 偏移/视口尺寸/项尺寸算首末索引；Content 总尺寸按数据量维护（垂直路径只写垂直尺寸）；节点按索引重绑、先解绑（可选口 `IVirtualListUnbind`）再写数据；ScrollRect 监听只绑一次、OnDisable/OnDestroy 对称解绑；`HardCap` 改为超限**告警**（不做静默截断）；`RefreshWindow()` 公开口供无 ScrollRect 宿主与测试。
- `Assets/LiteGame/Runtime/Shell/UI/UIFormInfo.cs`——新增 `IUIFormCatalog` 契约（§3 可注入面），`UIFormCatalog` 实现之；`UIService` 改依赖接口。
- `Assets/LiteFramework/Scripts/Core/Util/SafeCall.cs`——新增 `TryInvoke`（成败可判的隔离调用，回滚路径用；行为与其余 SafeCall 同语义：异常落地 Log.Error）。
- `Assets/LiteGame/Editor/DevReload.cs`——顺序钉死新增 ⓪′：全关后、`lua.Shutdown()`（env.Dispose）**前**调 `DropAllLogic()`（释放 Lua 引用必须趁 env 存活）。
- `Assets/LiteGame/Runtime/Main/GameEntry.cs`——无需改动：`logicResolver` 闭包不变，实例工厂职责移入适配器。
- `Assets/Tests/EditMode/UiU0EditModeTests.cs`（新增，6 例）+ `LiteGame.EditModeTests.asmdef` 补 `xLuaMain` 引用。

**实现口径偏离与登记**：

1. **真 Lua 用例的 env 装配**：测试内自带 loader（磁盘读 `Assets/LiteGame/Lua/**.lua`）并绑定 `log` 全局表（与 `LuaComponent.BindLog` 同款）——真实页面脚本调用 `log.info`，裸 env 缺该全局会炸（实测踩到：`UI.UIMain:14: attempt to index a nil value (global 'log')`）。
2. **`UIService` 的 `DontDestroyOnLoad` 加 `Application.isPlaying` 守卫**——编辑态调用 DDOL 抛 Runtime Error（实测），L2 EditMode 集成用例依赖此守卫；Play 态行为不变。
3. **`Bridge.ui:GetLogic(id)` 仍返回注册表模块而非页面实例**（Bridge 无 UIService 通道，且当前无 Lua 消费点）——页面实例所有权在 `LuaBehaviourAdapter.Logic`；归 U1 所有权/接缝批处置。
4. **`Pause` 仅限 Active**（Covered 页手动暂停会在单枚举模型里丢失遮盖标记）——展示状态独立记录（Visible/Covered/Paused 同时成立）属 U1 §4.1 两层状态重构。
5. UI-03/05/06/09（并发操作结果、资源租约、Shutdown、转场取消/输入仲裁）**不在 U0 范围**，未动。

**验证证据**（2026-09-23 本机实测，工作区含并行 C0 线未提交改动）：

- `dotnet test Tests/Tests.slnx`：**416 通过 / 0 失败**（LiteTesting 7 + LiteFramework 205（含 SafeCall.TryInvoke 新增 2 例）+ LiteSim 83 + LiteNet 121）。
- `powershell -NoProfile -File scripts/l2-unity-gate.ps1`：**L2 通过，exit 0**——meta 扫描 11689 个全合法；Unity 编译 completed 无失败；控制台 0 条 CS 错误；**EditMode 36/36**（原 30 + 本批 6）。
- 退出条件逐条对账：
  - 真 Lua 首开/关闭/复用成功 ✅（`真Lua页面_首开_关闭_复用_实例与self契约`：真 tbuiform 投影 + 真 `UIMain.prefab` + 真 `UIMain.lua`，OnShow 计数首开 1 次/复用 1 次，`module.new()` 实例与共享模块非同表、`self.ui` 挂实例、BtnClose 点击经 Lua 闭包无错误）；
  - 列表可往返 ✅（`虚拟列表_窗口复用_首尾往返_缩容_刷新不重绑`：500 条 13 节点、滚尾滚回首条重新入窗、缩容解绑、窗口不变时 Refresh 5 次零重绑）；
  - Covered/Paused 可清 ✅（`三层全屏_Covered与Paused可关_CloseAllOpen全清`：三层全屏遮盖状态、遮盖中关闭、Paused 关闭、遮盖源消失立即 OnReveal、CloseAllOpen 全清）；
  - 旧 env 零访问 ✅（`env重建_逻辑落空后复用_按新注册表重解析且旧引用已释放`：DropAllLogic 后旧适配器 `Released=true`、env.Dispose → 新 env/新注册表 → 复用重解析出全新 Lua 实例并补跑 OnInit）。

**接手复核与提交（2026-09-23，Codely 会话按用户指示接管）**：

- 提交前工作区发现 `.github/workflows/ci.yml` 带有对已提交 C0-④ 接线的**重复追加**（L0 扫描×2、Player 构建/冒烟×2、超时 60→90）——属坏编辑，已回退到已验证的 HEAD 版本；本批提交不含该文件。
- 复核门禁（最终工作区态）：L1 `scripts/test.ps1 -Lane L1` **407 通过 / 0 失败**（=R0 后 405 + SafeCall.TryInvoke 2 例；记录中 416 为未过滤全量口径，含 EndToEnd/LongRunning 标记用例，两口径自洽）；Unity recompile `errors:[]`；**L2 EditMode 36/36 通过**；**L3 8 项通过**（含 30s 双端对跑——UI 批不触服务端面，回归确认无串扰）。
- 提交哈希：d72f0e1（feat(u0) UI 正确性止血，19 文件，已推送 origin/LiteGame；含本记录）。

## 已知边界（后续批次）

- §12.1 的 **L2 PlayMode 行未接**（PlayMode 测试程序集尚未建设，归 C0/C4 与 UI 计划）：本批"真页面"证据在 EditMode，资源加载为替身（`AssetDatabase` 直读），Player/真资源验收待 U1 租约与 Offline 包。
- 操作结果类型（成功/已打开/忙/取消…、requestId、队列上限/超时）＝ §4.3，属 U1；本批保持现有 `ShowAsync` 返回 UIForm / 失败抛异常的兼容口径。
- 输入协调者（转场锁 + 模态 + 暂停综合求解）＝ §6.3/§6.2，属 U1；本批 `PrepareForShow` 只回到输入基线。
- 缓存预算/淘汰/销毁入口（Resident/LRU/DestroyOnClose）＝ §5.2，属 U1；本批池语义不变（只增不毁）。
- 模板自检 36 条中 `VirtualList` 用例（`RealizedCount == 7`）在窗口复用语义下语义不变（7 条 < 可见容量），L2 实测未回归。
