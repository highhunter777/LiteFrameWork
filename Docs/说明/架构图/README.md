# 架构图（生成物）

> **定位**：`说明/` 层的可视化入口——用可交互图回答"系统由哪几块组成、一次对局的数据怎么走、某个模块自己怎么转"。
> **权威边界**：本目录只放图的产物与图源，不裁决架构、不记批次；图与文字章节或代码冲突时，以文字与代码为准，改图不改文。
> **来源**：Archify `architecture` 图（showcase 档）由仓库源码生成；每个节点带仓库相对 `sources`（路径 + 行号），钉在生成时的提交上。 图源和 HTML 保留生成时快照；现行帧职责见[依赖注入设计 §5](../../设计文档/架构/客户端依赖注入专项设计.md#5-帧驱动)。
> **关联**：[01 项目定位与技术栈](../01-项目定位与技术栈.md) · [02 架构总览与程序集分层](../02-架构总览与程序集分层.md) · [03 客户端框架与运行时](../03-客户端框架与运行时.md) · [04 服务端与房间内核](../04-服务端与房间内核.md) · [06 联机同步与网络协议](../06-联机同步与网络协议.md)

## 1. 图列表

| 图 | 产物 | 图源 | 讲什么 |
|---|---|---|---|
| 全栈架构总览 | [全栈架构总览.html](全栈架构总览.html) | [图源-全栈架构总览.json](图源-全栈架构总览.json) | 三进程拓扑、联机协议、一次对局的数据流 |
| Bootstrap 进程壳 | [Bootstrap进程壳.html](Bootstrap进程壳.html) | [图源-Bootstrap进程壳.json](图源-Bootstrap进程壳.json) | 场景入口 → 宿主 → 反射装载 → 容器 / 帧泵，以及 AOT 壳的边界与失败面 |
| 产品组合根 | [产品组合根.html](产品组合根.html) | [图源-产品组合根.json](图源-产品组合根.json) | App 怎么装配 18 段：唯一 new 点、密封前跨域装订、容器与流程机 |
| 战斗域 | [战斗域.html](战斗域.html) | [图源-战斗域.json](图源-战斗域.json) | 一局对局的帧循环：输入上行 / 快照和解 / Sim→表现 |
| 应用壳 | [应用壳.html](应用壳.html) | [图源-应用壳.json](图源-应用壳.json) | 六阶段流程机（workflow 形态）：启动链 → 主界面 → 对局，失败落点与页面流 |
| 战斗数据流 | [战斗数据流.html](战斗数据流.html) | [图源-战斗数据流.json](图源-战斗数据流.json) | 数据契约视角（dataflow 形态）：SimInputFrame → 快照 → 和解 → 表现，谁生产、谁消费 |
| 战斗子模块 | [战斗子模块.html](战斗子模块.html) | [图源-战斗子模块.json](图源-战斗子模块.json) | 九个 SubModule 的分工：装配面只在 Setup，数据自上而下，事件走单通道 |
| LiteSim | [LiteSim.html](LiteSim.html) | [图源-LiteSim.json](图源-LiteSim.json) | 判定内核八块：九系统固定顺序、确定性三件、快照环与回滚复盘 |
| LiteNet | [LiteNet.html](LiteNet.html) | [图源-LiteNet.json](图源-LiteNet.json) | 协议与传输：信封 → 端口 → 安全装饰 → KCP，尺寸预算与准入常量 |
| LiteSync | [LiteSync.html](LiteSync.html) | [图源-LiteSync.json](图源-LiteSync.json) | 会话与编解码：一个门面 + 六个专件，快照 / 输入 / 事件双端同一份 |
| LiteFramework | [LiteFramework.html](LiteFramework.html) | [图源-LiteFramework.json](图源-LiteFramework.json) | 四大块与依赖梯：Core 零依赖零引擎 → Services → Adapters → Hosting |

- **产物**：单文件 HTML，离线可开、无外部依赖；点节点可聚焦并查看它的来源（路径 + 行号）；右上切浅/深色与视觉档、缩放、导出图片与 SVG。
- **图源**：节点、连线、边界、卡片与 `sources`。改图只改这里，再重出 HTML。
- [交付证据/](交付证据/)：每张图 `finalize` 四道门（validate / deliver / check / browser-check）的 JSON。

## 2. 图里有什么

### 2.1 全栈架构总览

- **三个进程**：Unity 客户端（Bootstrap → App 18 段装配 → Procedure 流程机 → Battle，下挂 LiteSim / RoomClient）、RoomServer（宿主 → Runtime 60Hz → 快照 30Hz）、MetaServer（+ MongoDB）。
- **一次对局**：输入上行（KCP / UDP :17777）→ 房间权威步进 → 30Hz 差分快照 + AOI → 回程到客户端 RoomClient。
- **跨进程三条线**：KCP 传输、客户端 HTTP（登录 / 房间 / 门票）、房间服注册心跳 1s。
- **四张卡片**补图外事实：四层单向依赖、双端共编与确定性、内容与配置、云侧形态。

### 2.2 Bootstrap 进程壳

- **壳的四件事**：场景入口（Boot.unity 上唯一引导件、执行序 -1000、重复引导静默销毁）、宿主（`new ClientHost()` + 同一 GameObject 上 `AppLifetime.Bind`）、入口装载（反射取 `IClientEntry` → `Configure` → 宿主初始化 → `StartAsync`）、错误面（内建 uGUI，锚 `[BootstrapError]`）。
- **三条去向**：ClientHost 初始化并密封 → ServiceContainer 供取；独立 `FrameSchedule` 每帧被 `Update` 喂 `Time.unscaledDeltaTime`；任一异常统一进 BootstrapErrorPresenter 分类展示。
- **边界**：`LiteGame.Bootstrap` 只引 `LiteFramework.{Core, Services, Hosting}` + UniTask，不引 `LiteGame.App` / Modules / Adapters；产品的 `ClientEntry` 由 `ClientEntryLoader` 反射装载——AOT 壳不认识产品代码。
- **四张卡片**：壳的四件事、AOT 壳的边界、启动时序与帧泵、失败与重入。

### 2.3 产品组合根

- **App 只留装配根**：ClientEntry（登记 18 段 + 启动续体）、AppComposition（11 个适配器工厂 + 跨域装订）、Probes；18 段全在 `Modules/**`。
- **两条装配线**：ClientEntry 按依赖序 `RegisterSegments ①…⑱`；AppComposition 把适配器工厂注入各段，段经 `SegmentContext.Put / Require` 产出到容器。
- **密封前一次装订**：`ComposeAfter` 跑 8 步（页面服务 / 身份 / 模态 / 输入门 / Meta 入口 / 产品设置 / 判定参数覆写 / 开发装饰）→ `DeclareFrameOrder` → `Services.Seal()`。
- **收尾**：`StartAsync` 从容器取 `StageMachine`，`fsm.Start(Launch)` 把流程机点着；开发调参与两个探针在这里武装。
- **四张卡片**：App 只留装配根、唯一 new 点、ComposeAfter 装订、启动续体。

### 2.4 战斗域

- **一条帧循环**：`BattleClient`（收发包）→ `BattleContext.Tick` 六步（收包 → 采样入史与上行 → 出包 → 预测步进 → 冗余补发 → 表现）→ `SimView` → `BattleViewRig`。
- **两条旁路**：输入管线把同一份输入既写进预测史又上行（`EntityId` 由服务器 InputGate 覆写防伪）；快照经 `BattleSnapshotMirror.ApplyAndReconcile` 比对 checksum 后覆盖 + 重放，不一致就 `SendMismatch` 并抓现场包。
- **一条边界**：`RollbackSim` 是 LiteSim 的共编件（域外），客户端以 `resolveCombat:false` 只预测开火/弹药/位移，命中、伤害、死亡只认权威。
- **四张卡片**：一帧的六步、快照与和解、输入三件与预测边界、装配段与表现装配（`BattleSegment ⑯` 的六个端口工厂、`BattleViewRig` 的四个装配器、`PublicCombatFeed` 事件批）。

### 2.5 应用壳（workflow）

- **一张阶段机**：`Launch → Patch → Preload → Main → Match → Battle`，任一阶段抛异常即 `Request(Error)`——`Error` 是终态，不自动重试。
- **三条泳道**：启动链（进程内，不进对局场景）、联机与对局（Match / Battle）、失败落点（Error）。
- **回边**：Battle 结束（完成 / 离场 / 会话失败）回 Main；Match 的 Join 失败/取消同样回 Main（图里并入 Match 子标签与「失败路径」卡片）；页面流在 Main 只开一次入口页。
- **四张卡片**：六阶段分工、失败路径（含启动期 BootstrapErrorPresenter）、页面流五页与账号重入、端口与单写者（`IAppFlow` / `UINavigationController`）。

### 2.6 战斗数据流（dataflow）

- **五段**：采集 → 上行 → 权威 → 判定与和解 → 消费；三行分别是输入/预测、权威与快照、事件与 HUD。
- **三份关键契约**：`SimInputFrame`（输入）、`StateSnapshot`（权威态）、`SimWorldState`（世界态：预测一份、镜像一份，互不回灌）。
- **校验线**：`checksum` 不符 → 覆盖 + 重放（红色），并 `SendMismatch` + 抓现场包。
- **图里省略的两条**（卡片已写明）：HUD 的权威读口（`AuthorityState → BattleReadModel`）未画线，`SimView` 的帧事件并入「世界态」一条；标签按阶段间隙长度做了压缩（如「快照」「世界态」）。

### 2.7 战斗子模块

- **一局谁持有**：`SessionSubModule`（`BattleSession`）持有 `MatchSession`（`BattleClient` + `BattleContext`）与本局视图组。
- **装配面只在 Setup**：`BattleViewRig` 唯一 new 表现件（场景 / 反馈 / HUD / 视角四个装配器），五个能力子模块只提供部件。
- **数据自上而下**：`BattleContext → ArenaSubModule`（Sim 态 → 视图）→ `Characters / Combat / Hud / Aim`；`PublicCombatFeed` 把命中/击杀/开火单通道分发给 Combat 与 Hud。
- **两条边**：`CommandsSubModule` 出命令（可撤销、帧边界派发），`SettlementSubModule` 出终局行表（`ISettlementSummary`）。
- **一处刻意的省略**：`SetupSubModule` 指向五个子模块的五条线不带标签——装配语义由 Setup 的副标题与卡片承担，避免同一走廊五条标签互相压线。

### 2.8 LiteSim

- **八块**：`Pipeline`（SimStep / RollbackSim / 输入史）、`Systems`（九系统）、`Config`（数值与武器表）、`Map`（判定地图与三角表）、`Rules`（障碍射线 / 散布 / 规则位）、`Kernel`（SimMath / SimRng / SimChecksum）、`World`（世界态 / 快照环 / 帧事件）、`Diagnostics`（离线复算 / 字段 diff）。
- **主链路**：`Pipeline → Systems → World`（就地读写）；数值、地图、规则从外部注入；`Kernel` 提供确定性原语与逐帧摘要。
- **九系统顺序**（卡片 1）：复活 → 输入 → 移动 → 武器 → 射击 →（伤害 + 计分，命令缓冲最多 3 轮）→ 清理 → 帧号 +1 → 对局裁决。
- **纪律**：禁超越函数 / FMA / float 等值比较；输入稳定插入排序零分配；双轨共编（Unity asmdef + dotnet csproj）。

### 2.9 LiteNet

- **一条出站包的路**：`RoomClient → PacketCodec.Encode`（1 字节 `PacketType` + protobuf）`→ IClientTransport → SecureEnvelope → KcpTransport → RoomServer:17777`；入站反向 `TryDecode`。
- **载荷单源**：`Battle.cs` 由 `.proto` 生成、双端同编；`PacketType` 是唯一类型字节；`BuildHash` 写进协议常量，Join 时随票据上行做准入。
- **端口与装饰器**：`IClientTransport` / `IRoomTransport` 可替换（本地服 / 弱网装饰器 / 测试假件）；安全信封是装饰器，会话代码不感知。
- **尺寸与观测**：`WireBudget` 约束快照 / 信封 / wire 三种计数，超预算快照丢弃并记 `SnapshotDropped`；`NetworkQualityProbe` 与 `DiagTrace` 供 HUD 与分包追踪。
- **边界**：`LiteSync` 的会话状态机不在本图范围，`RoomClient` 只作为端点出现。

### 2.10 LiteSync

- **一个门面 + 六个专件**：`RoomClient` 只发事件与相位；`RoomInbound`（解码分发 / 快照水位）、`RoomOutbound`（C→S 发包）、`RoomSession`（JoinAck 权威事实）、`ReconnectState`（相位 / 票据 / 版本基准）、`InputWindow`（未确认段环 16 帧）。
- **双端共用编解码**：`SnapshotCodec`（两端同编）、`SnapshotReassembler`（客户端解码入镜像）、`SnapshotDiffer + AoiFilter`（只在服务端造差量）、`InputPacker`（≤16 帧冗余，两端同一常量）。
- **重连与恢复**：`ReconnectResponse` 只在 `Restoring` 相位可应用，应用完毕必须 `CompleteRestore` 回报；预算 10s，超时或拒绝进 `Failed`。
- **边界**：本包不认识地图表、场景、UI；`NetworkQualityProbe` 只喂表现与诊断。`CombatEventCodec` 未画成节点（独立包型，卡片「这个包是双端共用的」已写明）。

### 2.11 LiteFramework

- **依赖梯（asmdef 实证）**：`Core`（`references=[]`、`noEngineReferences=true`）← `Services`（只引 Core + DOTween/UniTask/InputSystem/TMP）← `Adapters`（引 Core + Services）← `Hosting`（三者全引）。
- **份量（357 个 .cs）**：Core 16 件 / 83、Services 12 域 / 228（UI 112、Content 58、Animation 30）、Adapters 5 组 / 23、Hosting 23。
- **为什么 Adapters 画在 Services 上面**：这是依赖方向不是运行时方向——Adapters 实现 Services 声明的端口，编译期必须引用 Services。
- **装配与封印**：ClientHost 建容器 → 段按序 Compose → Seal（注册冻结、帧计划不可改）；`DeclareFrameOrder` 向 `FrameSchedule` 声明每帧顺序。

## 3. 怎么重出

```powershell
node <archify 包>/bin/archify.mjs finalize architecture "Docs/说明/架构图/图源-<主题>.json" "Docs/说明/架构图/<主题>.html" --repo-root . --quality showcase
```

- `finalize` 内置四道门（validate / deliver / check / browser-check），全 pass 才算成功，回执落在产物旁。
- 节点上的「来源 N」会校验 `sources` 的路径与行号是否落在生成时的提交上；**代码行号漂移后要重出或改 `line`**，否则该节点等于没有依据。
- Windows 上 Node 的 `lstat` 报 `st_dev = 0`、`fstat` 报卷序列号，会让发布步报 `changed-during-inspection`；本地副本需对设备号做归一（只影响回执写入，不影响图）。

## 4. 维护约定

- 架构变了（进程 / 端口 / 装配段 / 系统顺序）→ 同批改图源并重出，`交付证据/` 一起更新；**不要只改 HTML**。
- 图只画到「能看懂主链路」的粒度；细分机制写在 [02](../02-架构总览与程序集分层.md)、[03](../03-客户端框架与运行时.md)、[05](../05-Sim与战斗判定.md)、[06](../06-联机同步与网络协议.md)。
- 新增图命名：`图源-<主题>.json` + `<主题>.html`，并在本页「图列表」登记一行。
