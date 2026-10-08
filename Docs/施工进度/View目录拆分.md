# View 目录拆分（LiteSim.View → Assets/LiteView）

> 状态：**已完成（附验证证据）**
> 范围：表现层程序集 **LiteSim.View** 整目录迁移 `Assets/LiteSim/View` → `Assets/LiteView`（表现与 Sim 目录级分离——Sim 根纯模拟域，表现层独立顶层目录）；**程序集名与代码命名空间不变**（`LiteSim.View`/`LiteSim.View.*`——asmdef 引用按名解析零改动，命名空间对齐另批裁决）
> 证据时点：2026-10-09

## 改动内容

| 位置 | 处理 |
|---|---|
| `Assets/LiteSim/View` → `Assets/LiteView` | Pipeline `AssetDatabase.MoveAsset` 整目录迁移（含全部 `.meta` 与目录 `.meta`）；**3 探针 GUID 前后保真**（AnimationLayerGraph/VfxService/SimView——场景与 prefab 按引用 GUID 不受影响） |
| `ScanTargets.cs`（纪律扫描登记） | ①扫描目标 `Assets/LiteSim/View`→`Assets/LiteView`（UnityRules，只守 R6）②`ConfigPortExcludes`/`SimLayerExcludes` 摘除 View 条目（LiteSim 根纯 Sim 后排除面失效——表现层独立目标不经 R13/Sim 规则集）③`ClientSideProjectMarkers` 的 View 标记从路径片段 `LiteSim/View` 改为**程序集名** `LiteSim.View`（目录无关，新旧路径皆命中） |
| `VisualSingleSourceEditModeTests` | 视觉构建扫描目录 `Assets/LiteSim/View`→`Assets/LiteView` |
| `Tests/LiteSim.Core.Tests/LiteSim.Core.Tests.csproj` | **6 个源链接路径修正**（ViewTransformMath/HitLocalRole/DamageNumbers×4——L1 源链接直编 View 纯逻辑件；不改则 L1 断链） |
| 设计/导航/项目说明文档 ×10 | `Assets/LiteSim/View` 与裸 `LiteSim/View` 形态全量替换（动画模块/命中反馈/Sim组织/对象池/共享代码范围/性能工程/程序集引用图/修改指南/项目总览/技术细节-客户端）；归档与历史施工记录不动 |

**本批实测教训**：初次提交跑 L1 翻红——`纪律_代码注释不含施工痕迹` 守卫抓出我在迁移注释里写的**日期戳与迁移叙述**（"2026-10-09 已迁/原 LiteSim/View"）。去痕后复跑全绿：注释只描述现有设计与实现，迁移事实归本记录。

## 验证证据

- L1：`./scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **1532/1532**（0 失败；含迁移后源链接 6 件直测与扫描目标存在性守卫）。
- Unity 编译：pipeline `refresh` → 0 错误（13 警告全为既有项）。
- 纪律扫描：`RunDisciplineScan.All` → **lint OK（0 violations）**（新目标根 `Assets/LiteView` 生效、R13/Sim 排除面语义随纯 Sim 根收口）。
- GUID 保真：迁移前后 3 探针 GUID 逐字符一致（`AssetDatabase.MoveAsset` 随 `.meta` 迁移——引用面零破坏）。
- L2 EditMode/PlayMode 留夜间流水线。

## 遗留登记

- **buildHash 开发期不重跑**（用户裁决）：路径参与哈希——源集含 View 路径的成员在下次出包时由 `build-player.ps1 --check` 门禁拦截重录（设计内流程）。
- **程序集名/命名空间对齐**（`LiteSim.View` → 如 `Lite.View`）：未裁决——名字迁移是独立批（asmdef 4 消费者引用按名不变，命名空间 40+ 文件面），随通用框架提取（G6/C5）或用户裁决时做。
- AgentScripts 下 `patch_vfx_p1/p2.py` 为历史一次性补丁脚本（gitignore 本地件），路径引用未同步——不复活即无影响。
