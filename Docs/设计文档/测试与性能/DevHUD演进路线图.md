# DevHUD 演进路线图

> **版本**：0.1
> **定位**：裁决 DevHUD（`LiteGame.DevTool`，F1 Overlay）的目标形态、分批与退出条件——回答“后续做什么、做到什么算完成、什么不做”。只读仪表盘定位不变。
> **不做的事**：不重复 GM/调试分层（归[《GM 与调试工具分层专项设计》](../架构/GM与调试工具分层专项设计.md) §5）与平台形态契约（归[《开发工具链平台专项设计》](../架构/开发工具链平台专项设计.md) §1.1）；不登记批次完成状态（归 `Docs/施工进度/`）；不裁控制台/弱网/世界查看器（各为独立件，HUD 只接状态段）。
> **冲突裁决**：形态与写口纪律以 GM 分层为准；能力优先级以[《调试与开发工具链演进路线图》](调试与开发工具链演进路线图.md) T1～T3 为准；操作面以[13-调试手册](../../项目说明/13-调试手册.md) §2 为准。
> **关联**：[13-调试手册](../../项目说明/13-调试手册.md)、[14-客户端开发能力地图](../../项目说明/14-客户端开发能力地图.md)、[施工进度/GM与调试线](../../施工进度/GM与调试线.md)、[修改指南](../../修改指南.md)。

## 1. 现行基线

- 宿主 `DevHUD`（约 455 行）只持生命周期/输入/持久化/编排；七段由 `DevHudSectionSet` 构建（错误 / 联机诊断 / 性能 / 模块 Stats / Log Recent / DiagTrace / 日志 sink），Stats 发现/过滤/性能采样/网络历史/JSON 导出各自独立件。0.25s 节流、F1 总开关、F4 复制、F5 级别、F6 本局、F7 现场包、F8 JSON、F9 过滤。
- 性能段：240 帧环 → `FPS / p50 / p95 / p99 / 最差 / 长帧`；`ProfilerRecorder` 读 `GC Allocated In Frame`、`Draw Calls Count`、`Batches Count`（marker 缺失显示 `n/a`）；`HUD: x ms` 自报聚合耗时（指数平滑）。
- 联机段：相位/RTT/丢包/和解 + `回滚/延后/输入领先/快照年龄`；RTT/丢包 60s sparkline（240 采样 @4Hz，换局 Reset）。
- 生命周期：`[RuntimeInitializeOnLoadMethod]` 自创建 + `DontDestroyOnLoad`；程序集 `LiteGame.DevTool` + 三宏剥离；不得移入 `Editor/`。
- 数据面：`GameEntry.Stats` + 场景 `IModuleStats`（场景加载后重扫、每轮剔除销毁件）；联机读数走 `BattleDiagnostics` 单读口；日志条目带 `[m/f/p]`；`DiagTrace`/`StructuredLogSink` 状态直读。
- 只读：不持规则、不写运行态；无控制台/命令面。
- 已知边界：tag 过滤用 `@tag` 文本（不做多选控件）；`hudScale` 只缩放字号；无 ThemeStyleSheet（标准控件无样式）；IMGUI 回退无色调/折叠；折叠/位置/过滤/缩放已持久化；初始展开集读 `DevStartupConfig.DevHudDefaultExpanded`（出厂精简四段），仅本机无折叠记忆时套用。

## 2. 目标形态

**宿主 + 段件**：宿主只做节流、可见性、输入、布局、持久化与导出；每段只做“读一个面、输出若干行”。

```csharp
interface IDevHudSection
{
    string Title { get; }
    int Order { get; }
    bool Visible { get; }
    void Append(IHudTextSink sink, in HudContext ctx);   // 零分配、异常不出段
}

interface IHudTextSink
{
    void Line(string text, HudTone tone, string tag = null);   // tag 供 @tag 过滤
}
```

- **段清单**：Errors、Net、Perf、Stats、Log、DiagTrace、LogSink（`Written/Dropped/LastError`）。
- **输入**：`DevHotkey` 抽象——优先 Input System、回退旧 `Input`；F1–F9 + 过滤模式的字符输入（`onTextInput`/`inputString`）。
- **过滤**：F9 进入过滤模式；空格分 token，`@tag` 为标签（OR），其余为文本子串（AND）；Enter 应用、Esc 清除。
- **布局**：段可折叠、半透明底板、`hudScale`（字号）、状态行拖动、Toast 夹取在屏内。
- **持久化**：`DevHudPrefs`（Editor = EditorPrefs，Player = PlayerPrefs）保存宽度/字号/左/顶/缩放/级别/仅本局/过滤词/折叠段。**初始展开集**由启动配置 `DevStartupConfig.DevHudDefaultExpanded`（段掩码；出厂＝错误/联机诊断/性能/模块 Stats）提供，仅在本机无折叠记忆时生效；GM「联机诊断」页可手动按它重排并写回折叠记忆。
- **数据面扩展**：`BattleDiagnostics.Snapshot` 增回滚次数、输入领先、快照年龄；`StructuredLogSink` 状态接入段。
- **渲染后端可替换**：**UI Toolkit 为现役后端**（P0-A 起，IMGUI 自动回退，§5）；段接口不绑定具体渲染器。

## 3. 分批与退出条件

### P0-A 宿主收口

段化（`IDevHudSection` + `IHudTextSink`）、**UI Toolkit 后端（IMGUI 回退）**、`DevHotkey`、折叠/DPI/拖拽/持久化、`DiagTrace` 段、日志 sink 状态段、F8 JSON 导出、过滤（`@tag` + 文本）、sink 轮转/清理用例、Toast 夹取。

**退出条件**：旧热键行为等价；Input System 与旧 Input 两种设置下热键均可用；任一段抛异常不影响其他段；重启 Play 后设置保持；F8 JSON 可被脚本解析；sink 轮转/保留数用例绿；Play 冒烟通过。

### P0-B 段能力（现行形态）

- 性能：p50/p95/p99、GC alloc/frame、draw calls/batches（`ProfilerRecorder`，不可用则 `n/a`）、预算着色（p95>16.6ms 黄 / p99>33ms 红）。
- 联机：RTT/丢包 sparkline（60s 环）、回滚/延后、输入领先、快照年龄。
- Stats：按名称排序、错误/丢弃/积压类非零值着色；Log：`@tag` 多选（文本 token）+ 关键字。

**验收口径**：性能读数为 `ProfilerRecorder` 同源值（独立 Profiler 窗口抽样对账未做）；HUD 自身 `HUD: x ms` 可读，4Hz 下约 0.6ms/轮（≈0.15ms/帧）作为当前基线；60s 窗口采样容量与 4Hz 匹配。

### P1 能力件（HUD 之外）

Player 控制台（`CommandCenter` 通道）、弱网注入面板、世界/实体查看器——各独立件；HUD 只增加“状态行”（如控制台开启、注入参数、dump 完成），不承载交互。

### P2 度量

会话汇总（帧预算、分配、回滚、现场包/回放采集率）写 `metrics-<session>.json`；阈值与夜间性能门禁对接。

## 4. 问题清单与处置归属

上一轮分析/实测暴露的问题与归宿（不含完成状态）：

| 问题 | 处置 | 落点 |
|---|---|---|
| stats 发现一次性、销毁件残留 | 场景加载重扫 + 每轮剔除 | 现行基线 |
| 隐藏即停采样、性能窗冻结 | 采样与可见性解耦 | 现行基线 |
| 每轮 `new StringBuilder` + net-diag 数组 | `_sb` 复用 + `AppendCurrent` | 现行基线 |
| IMGUI 长行裁剪 | 换行 + 滚动 + 字号 | 现行基线 |
| 无 DPI/拖拽/持久化 | `hudScale` + 状态行拖动 + `DevHudPrefs` | 现行基线 |
| 无文本/tag 过滤 | F9 过滤模式 + `@tag`/文本 token | 现行基线 |
| 旧 `Input` 硬绑 | `DevHotkey`（Input System 优先/旧 Input 回退） | 现行基线 |
| 段异常只护了 Stats | 段宿主统一 try/catch | 现行基线 |
| 段不可折叠 | 段标题 `[-]/[+]` 点击折叠（PointerDown） | 现行基线 |
| 折叠态不落盘 | `DevHudPrefs.collapsed` | 现行基线 |
| 读数不可结构化导出 | F8 JSON（与 F4/F7 并存） | 现行基线 |
| `DiagTrace` 无 UI 读面 | `DiagTrace` 段（最近 8 条 + total） | 现行基线 |
| 日志 sink 丢弃量游戏内不可见 | `LogSink` 状态段（`Written/Dropped/LastError`） | 现行基线 |
| sink 轮转/清理无测试 | 小上限轮转 + 保留数用例 | 现行基线 |
| Toast 可能画出屏外 | 位置夹取 | 现行基线 |
| 性能只有均值/最差/长帧 | p50/p95/p99、GC/Draw/Batches、预算着色、HUD 自报耗时 | 现行基线 |
| 联机无曲线/回滚/输入领先/快照年龄 | 60s sparkline + `Snapshot` 扩字段 | 现行基线 |
| Stats 无排序/阈值 | 名称排序 + 错误/丢弃/积压着色 | 现行基线 |
| 限频后逐次 Warning 刷屏 | 限频判定前移，每局一次 Info | 现行基线 |
| 包形态无 GM 面板 | 控制台（P1），非 HUD | P1 |
| 服务端 `HostLog` 未接同一 sink | 独立批 | T1 后续 |
| `_cache = ToString()` 每 0.25s 一次分配 | 接受（4 次/秒，可被 HUD 自身开销读数量化） | 不排 |
| IMGUI → UI Toolkit 重写 | 已迁移（段化/行色调/折叠/过滤/JSON） | 现行基线 |
| 每局 5 条 FSM“静态不可达”Warning | 级别/tag 过滤即可，非 HUD 问题 | 不排 |
| 多客户端聚合 / 远程 HUD | 不做 | 非目标 |

## 5. UI Toolkit 决策与落地（ADR）

### 5.1 决策

**DevHUD 迁移到 UI Toolkit**（用户裁决）。过渡期保留 IMGUI 作为**自动回退**（`renderWithUiToolkit=false` 或字体初始化失败时），不做长期双栈；最终以 UI Toolkit 为唯一渲染后端。段数据/读数/热键仍归 DevHUD，渲染只换后端。

### 5.2 备选与取舍

| 方案 | 结论 | 理由 |
|---|---|---|
| **A 迁移 UI Toolkit**（选中） | 采纳 | 接纳长期 UI 技术栈；布局/DPI/滚动/文本能力优于 IMGUI；实测可盖住 uGUI Overlay（§5.3） |
| B 保持 IMGUI（原推荐） | 否决 | 成本低，但技术栈停滞；HUD 的 DPI/折叠/长列表会持续靠手写 Rect 补 |
| C 长期双栈并行 | 否决 | 仅允许过渡期自动回退；两套渲染/输入/布局同时维护的复杂度反超收益 |

### 5.3 实测结论（决定可行性的四件事）

| 面 | 结论 |
|---|---|
| 字体 | 系统 TTF → TextCore SDF：`FontAsset.CreateFontAsset(path, faceIndex, 90, 9, SDFAA, 1024, 1024)` 可渲染中文；`CreateFontAsset(family, style, 90)` 产出的 face 不可用（实测 "Unable to load font face"），**不要用** |
| 层级 | `PanelSettings.sortingOrder = 10000` 高于产品 uGUI Overlay（当前最高 6000），实测可盖住结算页；该值依赖产品 canvas 排序，改 UI 排序时必须同步 |
| 主题 | 无 `ThemeStyleSheet` 时 Label 仍渲染（显式字体 + 内联样式）；内置控件（Foldout/ScrollView 滚动条/TextField）无样式——P0 用自定义滚动/折叠，控件与主题资产留后 |
| 资产 | `PanelSettings` 运行时创建，零 UXML/USS/PanelSettings 资产；字体来自 OS 文件（Windows/macOS/Linux 候选链） |

代价：主题缺失导致内置控件不可用、字体依赖 OS、sortingOrder 与产品 UI 排序耦合、启动创建字体有一次性开销。

### 5.4 实施结构（只换渲染后端）

```csharp
// 段件输出“行 + 色调”，与渲染后端解耦
section.Append(sink, ctx);        // IMGUI: ImguiHudSink(StringBuilder)
                                  // UITK : UIToolkitHudSink(VisualElement/Label)
```

- 过渡：`DevHUD.renderWithUiToolkit`（默认 true）+ 构造失败自动回退 IMGUI；稳定后删 IMGUI 分支。
- 剥离：`DevHudUiToolkitHost` 与 DevHUD 同程序集、同三宏；无资产，release 零残留。
- 输入：F1、F4–F9 仍在 DevHUD；滚动走 `WheelEvent`；过滤框待自定义输入（避免与 WASD 冲突）。

### 5.5 分段落地

| 阶段 | 内容 |
|---|---|
| 当前 | 七段 + 行色调/标签 + 折叠 + F9 `@tag`/文本过滤 + F8 JSON + 拖动/缩放/持久化 + P0-B 性能/联机/Stats 读数 |
| P1 | 控制台/弱网/世界查看器（HUD 只接状态行，不做交互） |
| P2 | 会话度量、`metrics-<session>.json`、夜间阈值；HUD 自身开销量化与优化 |

### 5.6 边界与复核

- 无主题 ⇒ 内置控件不可用；当前只依赖 Label + 自定义滚动/折叠/拖动。需要标准控件时再引入主题资产。
- tag 过滤是 `@tag` 文本 token（OR），不做多选控件；`hudScale` 只缩放字号，不做完整 DPI 布局缩放。
- 字体只从 OS 候选链取；无可用字体 → 自动回退 IMGUI（中文由 IMGUI 动态字体保证）。
- `sortingOrder=10000` 与产品 canvas 6000 的耦合写进代码注释与 13 手册；产品新增更高排序 canvas 时同步。
- 复核时点：P0-A、P0-B 结束各一次；若维护成本高于收益，保留回退开关。

### 5.7 与旧决策的关系

本节取代此前“保持 IMGUI”的决策；对比表保留为背景。能力分期（P0/P1/P2）不变，迁移作为 P0-A 的渲染层工作，不改变 HUD 的只读定位。

## 6. 验收与度量

- 每批：Unity 编译 0 错 + L1 + Play 冒烟（真实操作路径：F1、F4–F9、折叠、过滤、导出）；涉及资产/序列化时按 unity-pipeline 规范。
- UI Toolkit 后端：中文可读、`sortingOrder` 盖住产品 uGUI（当前 6000）、系统字体不可用时自动回退 IMGUI；滚动/折叠在真实输入下可用。
- HUD 自身：帧耗时与分配可读（P0-B）；sink `Dropped` 在段内可见（P0-A）。
- 不进入 release：三宏剥离 + 程序集边界；新增资产必须验证收集组排除。
- 度量口径沿用[调试与开发工具链演进路线图](调试与开发工具链演进路线图.md)（`change-to-verified`、回放采集率等），本文件不另立指标。

## 7. 非目标

- 不在 HUD 内加写操作/命令（控制台走 `CommandCenter`）；不把 HUD 变成 GM 面板。
- 不重写 Unity Profiler、不做全量遥测、不做多客户端聚合/远程 HUD。
- 不以“统一 UI 栈”扩大迁移范围：控制台/查看器仍独立件；DevHUD 迁移只换渲染后端。
- 不追求 release 可调试。

## 8. 关联

- [GM 与调试工具分层专项设计](../架构/GM与调试工具分层专项设计.md)：四层模型、DevHUD 只读定位、命令面纪律。
- [开发工具链平台专项设计](../架构/开发工具链平台专项设计.md)：§1.1 形态收敛判据、§11 L3 交互面。
- [调试与开发工具链演进路线图](调试与开发工具链演进路线图.md)：T1～T3 能力优先级。
- [13-调试手册](../../项目说明/13-调试手册.md)：DevHUD 操作面与已知不足。
- [施工进度/GM与调试线](../../施工进度/GM与调试线.md)：批次证据（本文件不记状态）。
