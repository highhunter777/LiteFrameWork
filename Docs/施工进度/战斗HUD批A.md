# 战斗HUD批A（prefab 化 + 引用件 + 收集 + 场景退役）

《战斗HUD专项设计》/《战斗HUD层级设计》批 A 执行（2026-10-11）。批 B 装配拆分已由并行 BattleViewRig 批先行完成——本批与其合流，**HUD 全链按新规范落位**。

## 状态

**已完成（pipeline + Unity 证据齐全）**。

## 交付

| 面 | 落点 |
|---|---|
| 模板 prefab | `Assets/ShareResource/UI/Hud/Hud_Battle.prefab`——训练场现役树 1:1 转制：§9 词缀化改名 23 处（`Hud_Battle/Cot_Crosshair/{Icon_Hip,Icon_Ads}`、`Cot_Panel/{Cot_Vitals,Cot_Weapon,Cot_Skills}`…）＋ **Icon_Ads 38→64（用户裁决）** ＋ 根挂 `BattleHudView`（SerializedObject 接线 8 字段） |
| 引用件（新） | `App/Presentation/BattleHudView.cs`——8 个序列化字段（CrosshairRoot/Hip/Ads/PanelGroup/HpFill/HpText/AmmoText/ReloadLabel）+ `PrefabLocation` 单源；零 Find 契约 |
| 收集 | collector 新组 **UIHud**（`Assets/ShareResource/UI/Hud`，PackSeparately，ui tag）——HUD 件级热更 |
| 场景退役 | `TrainingGround.unity` 删 `/Battle HUD` 对象（场景不承载 HUD 视觉）；HUDDesign 同日改造为 **UiWorkbench**（UI 工作台场景，见《UI工作台设计》——HUD 预览随工作台 W2 可选） |
| 接线（Rig） | `BattleViewRig`：`AcquireHudAsync`（租约→_viewScope→实例化→取引用件）+ `AttachCrosshair/AttachHud` 改 View 引用 + Dispose 回收 HUD 根件；**六条路径常量与 FindChildGameObject 退役——Find 归零** |
| 退役 | `AgentScripts/BuildBattleHud.cs`（构建器双写根源消除；本地未入库件留待删） |

## 迁移过程（如实，三段收敛）

1. v1 顺序改名 bug：父节点先改名致子路径失配，ABORT 未拦住保存——产出半改名 prefab 且场景对象已退役（场景退役本为目标 ✓，树完整在 prefab 内）；
2. v2 fail-fast 生效拦截（接线不全不保存），但改名表仍是旧路径；
3. v3 改「**先收集后统一改名**」模式对齐盘上状态——×23 全中、结构校验 10/10 OK、ADS=64、保存成功。

## 验证证据

| 项 | 结果 |
|---|---|
| 结构校验 | 10/10 OK（层级树全节点按新名在位）＋ `ADS size=64` ＋ `root=Hud_Battle` |
| collector | UIHud 组（Assets/ShareResource/UI/Hud，PackSeparately，ui） |
| Unity recompile | `completed / failed:false / 0 错误`（含 BattleHudView/BattleViewRig 接线） |
| `BattleCrosshairEditModeTests` | **4/4** |
| `VfxServiceEditModeTests`（回归面） | **17/17** |
| Find 归零 | `BattleViewRig` grep `GameObject.Find/FindChildGameObject` = 0 |

## 遗留（如实）

- 实机观感手测（F9：血条/弹药/换弹标签/准心双形态）待本机——逻辑面测试已绿；
- `BuildHudPrefab/2.cs` 迁移脚本留 AgentScripts（未入库）供复跑；
- 技能栏数据面（批 C）按 UI 框架 U2 节奏另行点单。
