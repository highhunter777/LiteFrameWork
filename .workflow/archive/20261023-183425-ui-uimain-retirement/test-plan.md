# 测试计划：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 运行 ui-uimain-retirement · 创建于 2026-10-23T17:29:30+08:00

## 测试策略

- 测试层级：EditMode 契约/资产守卫为主（页面清单、生成常量、启动锚、载体路径、真实面校验）；PlayMode 保留真 prefab + 真 Lua + 真转场链路（测试载体改名后回归）。
- 关键风险与覆盖重点：
  1. 表行删除后生成链是否真断（页面清单/LuaKeys/tbuiform 数据）——用真实清单与反射常量断言，不读源码字面量；
  2. 启动锚是否仍指向样例页——`RegistryFiller.KnownUiKey` 常量断言 + 源码无 `LuaKeys.UI.UIMain` 兜底；
  3. 测试载体改名后 PlayMode 是否仍能经 `AssetService` 加载——保留 Screens 收集面 + 真资源链路用例；
  4. 真实面 Warning 是否只降 1（303→302）——真实 `UiValidator.Run()` 计数钉；
  5. 11 份产品契约不因 UIMain 退役变化——Wiring 写入 11 份、contractPresent 全真、无 UIMain 跳过。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UT-1 | EditMode | T4/AC1 | `UiPageManifestStore.Load()` | 11 页/12 行；无 `UIMain` |
| UT-2 | EditMode | T4/AC1 | 反射 `LuaKeys.UI` 常量 | 6 个 UI 常量；含 `UI.Page_Login`；不含 `UI.UIMain` |
| UT-3 | EditMode | T2/AC2 | 反射 `RegistryFiller.KnownUiKey` + 读 `RegistryFiller.cs` | 字段存在且=`UI.Page_Login`；源码不含 `LuaKeys.UI.UIMain` |
| UT-4 | EditMode | T2/AC2 | 读 `Assets/LiteGame/Lua/main.lua` | 不含 `UIMain` / `UI.UIMain` require |
| UT-5 | EditMode | T3/AC3 | `AssetDatabase` + `File.Exists` | 旧 `UIMain.prefab`/`Lua/UI/UIMain.lua` 不存在；新 `Tmp_UiTestPage.prefab`/`Lua/Tests/UiTestPage.lua` 存在 |
| UT-6 | EditMode | T2/AC2 | `UiContractGenerator.IsContractPageName` | `UIMain` false；`Page_Login` true（常驻壳判据不回归） |
| IT-1 | EditMode | T5/AC4 | `UiValidator.Run()` 真实面 | Error=0；Warning=302；命名 Warning=0 |
| IT-2 | EditMode | T5/AC4 | `UiContractBatch.Generate(temp)` | 写入 11 份；`Skipped` 无 UIMain；Tmp_Dialog 多行告警仍在 |
| IT-3 | EditMode | T3/AC3 | `SafeAreaEditModeTests`/`UiBindingToolsEditModeTests`/`UiU0EditModeTests` | 新载体路径可加载；Lua 扫描命中 `Tests/UiTestPage.lua`；adapter 生命周期仍绿 |
| E2E-1 | PlayMode | T3/T5/AC3 | `LuaPagePlayModeTests`/`EnvRebuildPlayModeTests` | 真 prefab（`Tmp_UiTestPage`）+ 真 Lua（`Tests.UiTestPage`）+ 真转场仍绿 |

## 预期失败证据

- 命令：`python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests`（新守卫集）；随后 `wf verify --red` 汇总。
- 观察到的失败（实施前）：页面清单仍有 12 页/13 行且含 `UIMain`；`LuaKeys.UI` 仍有 `UI.UIMain`；`RegistryFiller.KnownUiKey` 不存在/仍指 UIMain；`main.lua` 仍 require UIMain；旧载体存在/新载体不存在。
- 为什么这是正确的失败：这些断言描述的就是目标态；实施前缺失目标态，失败由断言产生（不是编译/环境故障）。既有夹具改到新路径后在实施前会因旧载体缺失/旧清单计数失败，同属目标态红。

## 运行方式

- 全量测试：`wf verify`（L1 + 全部 UI EditMode 夹具 + PlayMode + doc/l0/toolchain）。
- 仅运行新增测试：`python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests`。
- 先红：`.workflow/config.json` 的 `ui-uimain-retirement-red` 命令 + `wf verify --red`。

## 豁免与说明

- `RegistryFiller` 的真实启动填充（读 tbuiform/require/env）不在 EditMode 单测面（缺 ConfigService 装配）；以 `KnownUiKey` 常量断言 + 源码无 UIMain 兜底 + 代码评审覆盖。若后续有 Boot/Preload PlayMode 冒烟，可补端到端。
- `Tmp_UiTestPage` 仍在 `Screens` 收集目录（PlayMode 真资源链路需要）；产品包含一个小测试载体是显式范围，不冒充“包内零残留”（候选 C 另批）。
