# UI 资源与热更缺口收口（收集组 / 目录归属 / 运行期增量重填）

> 2026-09-17。收口对象：《UI性能优化规划》**缺口 1/2/3**（见该文档末章缺口清单）。
> 用户拍板口径：① 迁界面进 LiteGame ② 改源表 + regen ③ 删除 RaceProbe ④ 重填做到「机制就绪 + 调试口」。
> 纪律：Unity 序列化内容一律经 Pipeline（`move_asset`/`delete_asset`/`run_script`），未手改任何 `.prefab`/`.asset`/`.meta`。

---

## 1. 缺口 3：目录归属收口

| 动作 | 结果 |
|---|---|
| `Assets/UI/UIMain.prefab` → `Assets/LiteGame/UI/Screens/UIMain.prefab` | ✅ `move_asset` 迁移，**GUID 保持不变**（`e49765fc90cbe14489a7b9c5ff8c4500`，已 `find_assets` 复核） |
| `Assets/UI/RaceProbe.prefab`（零引用孤立件） | ✅ `delete_asset --confirm` 删除 |
| `Assets/UI/` 空目录 + `Assets/UI.meta` | ✅ 经 run_script `AssetDatabase.DeleteAsset` 清掉（.meta 只能由 Unity 处理） |
| 表列 `Luban/Data/#uiform.xlsx` → `LiteGame/UI/Screens/UIMain.prefab` | ✅ openpyxl 改单格 + `Luban/gen.bat` regen |
| 生成物 | `Assets/LiteGame/Lua/Cfg/tbuiform.lua`（lua pass）+ `Assets/LiteGame/RawFile/Config/tbuiform.bytes`（bin pass） |

**为什么加 `Screens/` 子目录**：YooAsset 同组内两个 CollectPath 不得互相包含（否则同一 prefab 被双收、归属不确定）；界面与控件要用不同 PackRule（`PackSeparately` vs `PackDirectory`），路径必须是兄弟目录。

## 2. 缺口 1：YooAsset 收集组

经 `AgentScripts/CollectorPatch.cs`（run_script）写 `Assets/BundleCollectorSetting.asset`（走 YooAsset 官方 `BundleCollectorSettingData.Setting + SaveFile()`，非手改）：

```
LiteGameUI      CollectPath: Assets/UI            → Assets/LiteGame/UI/Screens   （PackSeparately，tag ui）
LiteGameWidgets 新增组     CollectPath: Assets/LiteGame/UI/Widgets              （PackDirectory，tag ui）
```

编辑器态采集核对（`AgentScripts/CollectorVerify.Run`）：

```
采集资源总数 43；UI 目录 26（期望 26），带 ui tag 26
  defaultpackage_assets_litegame_ui_screens_uimain.bundle ← 1 件   ← 单界面单包
  defaultpackage_assets_litegame_ui_widgets.bundle        ← 25 件  ← 单目录单包
→ PASS
```

运行时核对（Play，`CollectorVerify.RunRuntime` + 菜单 `LiteGame/UI/校验收集组`）：**可加载 26/26，tag=ui 清单 26 条 → PASS**。

**加载路径补齐**：`UIDemoPage.LoadTemplate` 真机分支从「return null」改为经 `AssetService` 加载（编辑器分支保留 AssetDatabase 快路径，未命中则回落运行时路径）；`LoadTemplate` 已 UniTask 化（`Awake → BuildAsync().Forget()`，禁原生协程）。

## 3. 缺口 2：运行期增量重填（不重建 env）

### 3.1 交付物

| 件 | 变更 |
|---|---|
| `LiteFramework/Core/DI/ILuaRegistry.cs` | + `int Generation { get; }`（实现早已存在；**只作读数/断言，不作失效判据**） |
| `LuaComponent` | + `RepreloadAsync(ct)`（重预载，不重建 env）、+ `ClearRequireCacheByRoots(roots)` |
| `LuaBehaviourAdapter` | + `Logic` 属性、+ `Release()`（解绑监听 + 释放逻辑表与七个回调的 Lua 引用；`LuaBase.Dispose` 幂等） |
| `UIForm` | + `NeedsReinit`：`EnterActiveFromRecycled` 在换表后**补跑 OnInit**（否则新适配器 `_index` 为空 → 按钮全不响应） |
| `UIService` | + `MarkLogicStale()` / `MarkLogicStale(id)` / `ApplyStaleLogic()` / `StaleLogicCount` / `SwapIfStale(form)`；`Snapshot` 加「待更新」 |
| `LuaRegistryRefillService`（新） | 重填编排 + `IModuleStats`（StatsName `LuaRefill`） |
| `GameEntry` / `ProcedureLaunch` | 装配 + 注册 |
| `LuaDevRefill`（新，Editor） | 菜单 `LiteGame/Lua/Refill Registries %&f`（Play only）+ 自检断言 |

### 3.2 顺序钉死（半更新窗口尽量短）

```
① UIService.MarkLogicStale()        ← 先标后清；窗口内界面仍用旧表跑完
② await LuaComponent.RepreloadAsync ← 改动的 .lua 进预载缓存（不重建 env）
③ Bridge.Data.ClearLuaCaches()      ← 放手 Bridge 缓存位持有的旧表
④ ClearRequireCacheByRoots(UI/Content/Strategies)  ← 清 package.loaded，否则 require 命中旧 chunk
⑤ 三注册表 Clear()                  ← Fill 重复抛，清是重填前置；Generation 各前进一位
⑥ RegistryFiller.FillAll()          ← 与启动期/DevReload 同一条路径
⑦ UIService.ApplyStaleLogic()       ← 池中界面立刻换表；Active 界面保留标记，等 Close 时换
```

### 3.3 生效时机语义（最终一致，不假装瞬时）

- **Active/Covered/Paused 界面**：保留旧逻辑表继续跑（§2.3 决策 B：已打开的界面换代码会「一半旧一半新」）；**关闭时**（`EnterClosing` 之后）换表，下次 Show 走 `NeedsReinit` 补 OnInit。
- **池中（Recycled）界面**：重填完成后立刻换表。
- 池中/关闭时被替换的旧适配器 → `Release()` 释放其持有的 Lua 引用。

### 3.4 实测（Play，Test.unity 启动场景）

```
[Lua] UIMain:OnShow / [UI] UIForm[1] 打开（Bottom@100）      ← 新表列 + 新收集组运行时加载成功
[Lua] UIMain:OnHide / [UI] UIForm[1] 关闭                    ← 关闭落池
[LuaRefill] 增量重填完成：清 require 2 项、填充 2/2、失败 0、纪元 6、待更新界面 0
[DevRefill] 纪元 2→6；填充 2/2（失败 0）；待更新 0→0 → PASS
[Lua] UIMain:OnShow / [UI] UIForm[1] 复用打开（Bottom@100）  ← 换表后复用（NeedsReinit 路径）无异常
[WidgetTemplateCheck] 模板自检完成 PASS=31 FAIL=0            ← 无回归
```

Play 全程 **0 error**。L1：`dotnet test Tests/Tests.slnx` → LiteFramework **141**（= 原 139 + 新增 2）/ LiteSim 68 / LiteNet 11，全绿。

## 4. 偏离与决策登记

| 项 | 设计原文 | 实现取舍 | 理由 |
|---|---|---|---|
| 旧 LuaTable 释放 | §2.3/§7.3「旧表必须显式 Dispose」 | **随适配器替换时 `Release()`**；Active 界面保留旧表至关闭后替换 | 精确到「不再被任何人持有」时释放，避免 use-after-free；DevReload 走 `env.Dispose` 兜底（`LuaBase.Dispose` 幂等，无二次释放） |
| `package.loaded` 清理粒度 | 原计划「逐 key nil」 | **按三个根前缀清**（`UI.`/`Content.`/`Strategies.`） | `ILuaRegistry` 无键枚举 API；根集是 `gen_lua_keys.py` 校验过的封闭集，前缀清对新模块自动生效。**已知边界**：只清三个根下的模块，跨根依赖（如 `Core.class`）仍走缓存 |
| Refill 服务依赖 | 原计划显式传 `LuaPreloader` | 折进 `LuaComponent`（`RepreloadAsync`） | `LuaPreloader` 本就归 `LuaComponent` 私有持有（loader 数据源），避免为它单开访问器 |
| `Generation` 上接口 | — | 上接口但**不作失效判据** | 逐项 Fill 也递增（抖动）；失效一律走显式 `MarkLogicStale()`，UIService 不为此持有注册表依赖 |
| 策略热更（§2.3 类别 A） | 「无状态可随时替换」 | **未做** | 三个策略件当前**未从注册表注入**（`UIService` 用壳内 C# 默认实现，`GameEntry.cs:80-81` 只注入 logicResolver）→ 无消费点，做了也是空转 |

## 5. 顺带修掉的既有问题（与本次缺口相关，一并记录）

1. **`Luban/gen_lua_keys.py` 缺 `import re`** —— gen.bat 第三步直接 `NameError` 退出（`re` 在第 17 行被使用）。该文件不在 git 跟踪内（本机状态），已补 import 并复跑通过（`LuaKeys.g.cs written: 2 paths`）。
2. **Luban cs-bin pass 会删掉 `Assets/GameData/Generated/Luban.Tables.asmdef`** —— 每次 regen 必现（早期已知坑位重现）。本次已从 git 恢复。**长期对策未做**（建议把 asmdef 移出 `outputCodeDir`，或 gen.bat 尾步自动恢复）。
3. **`UIDemoPage` 隐藏编译错误** —— `LoadTemplate`（静态方法）里调用了实例方法 `Log`，被 `#if UNITY_EDITOR` 掩蔽：**非编辑器平台必挂**。已把日志出口改静态。
4. **`LuaKeys.g.cs` 注释漂移** —— 仓库版头注释与本机脚本版本不同步（仅注释），regen 后取脚本真实输出。

## 6. 遗留（不在本次范围）

- **真机 YooAsset 分支仍 `throw NotSupportedException`**（`AssetService.cs:48-77`，M6 交付）——收集组与加载路径已就位，真机跑通待 M6。
- **重填触发点**：`ProcedureMain` 仍为占位空转，无 Match/Battle/Result 流程 → 「安全窗口」（回主城 / 战斗结束）由 M11 接；当前只有 Editor 调试菜单。
- **`Assets/Scenes/Boot.unity` 是空场景（仅灯光）**，真正的启动场景是 `Assets/Scenes/Test.unity`（含 `GameEntry`）——做 Play 验收时用后者。
