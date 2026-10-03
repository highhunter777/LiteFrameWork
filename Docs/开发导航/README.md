# 开发导航

> 面向"要在本仓库里改代码/资源"的人：先看[代码地图](代码地图.md)知道**东西在哪**，再用[修改指南](修改指南.md)查**我想改 X 应该动哪个文件、改完要跑什么**。
> 本目录只回答"在哪、去哪改"；设计契约、验收标准、执行口径分别归 `Docs/design/`、`Docs/待办总览.md`、`Docs/施工进度/`——不在本目录重复，冲突时以它们为准。

## 阅读顺序

1. **[代码地图](代码地图.md)**——仓库目录 → 领域 → 程序集（asmdef）三层对照；每个目录一句话说清"是什么、归谁"。找东西先看这张图。
2. **[修改指南](修改指南.md)**——任务导向：玩法数值 / 帧率 / 地图 / 输入 / 相机 / 协议 / 服务端 / UI / 配置表 / 测试……每条给出**改动文件**与**改完必跑的验证**（含 BuildHash 源集这类"不改生成器就握手拒进房"的红线联动）。
3. **[程序集引用图](程序集引用图.md)**——双端全量程序集引用关系（mermaid，2026-10-03 时点）：①双轨共享 ②客户端运行时 ③服务端宿主 ④编辑器/工具 ⑤Unity 测试 ⑥dotnet 测试；箭头=引用方向，`⊕`=程序集外依赖。新增/改引用程序集后当批同步。

## 三条最容易踩的全局红线（先记住再看地图）

| 红线 | 触发条件 | 后果/动作 |
|---|---|---|
| **BuildHash 源集** | 改动 `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol}` 或两端表数据 | 这些内容参与两端版本哈希——**改完必须重跑 `python scripts/codegen/gen-build-hash.py`**，否则客户端/服务器 buildHash 不一致，Join 被拒进房（这是设计内红线，不是 bug） |
| **R11 纯化纪律** | 在 `Assets/RoomServer/Runtime/` 下写代码 | 房间内核**禁** Console / 系统时钟 / 文件 IO / proto / LiteNet 引用——违反会被 L1 纪律扫描（`DisciplineScannerTests`）打红；扫描目标清单在 `Assets/Tools/DisciplineScanner/Scripts/ScanTargets.cs` |
| **Unity 序列化资产** | 场景 / Prefab / Material / AnimatorController / `.meta` / 工程设置 | **一律走 unity-pipeline CLI**（见 `UNITY-GUIDE.md`），直接改文件违规；纯文本（.cs/JSON/md）可直接编辑 |

## 维护约定

- 结构性改动（新增/搬移目录、新增程序集、改装配序）后**当批同步本目录**——过期地图比没有地图更害人。
- 文内只写"是什么/去哪改"，不写"为什么这样设计"——设计动机归 `Docs/design/`，引用即可。
- 路径全部实测核对（2026-10-01）；发现文档与代码不一致，以代码为准并顺手修文档。
