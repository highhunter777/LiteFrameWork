# UI 编辑器工具链专项设计

> 状态：现行专项设计（目标形态；工作台/契约/校验为核心，部分工具为待建）
> 版本：1.0
> 日期：2026-10-22
> Owner：客户端工具；运行时/绑定契约由客户端 UI 负责
> 定位：回答“美术与工程如何用一套工具完成 UI 的起页、布局、绑定、样式、文案、契约、校验、预览与登记”。本文只裁工具形态、输入输出与门禁，不重复运行时的生命周期/导航契约。
> 不做的事：不做第二套画布/布局格式；不做 Figma 导入；不做运行时特效播放器；不替代 Prefab Stage；不把目标工具描述成已有工具；不记录施工状态。
> 冲突裁决：资产写入以 Unity 序列化/Pipeline 为准（`Docs/工具链/Unity操作规范.md`）；工具入口/清单以 `scripts/gate/toolchain.json` 与[开发工具链平台专项设计](../../架构/开发工具链平台专项设计.md)为准；页面契约字段以本文 §5 与 `UiPageContract.schema.json` 为准；角色/交接以[UI 美术协作与交接规范](UI美术协作与交接规范.md)为准。
> 关联：[UI 系统总设计](UI系统总设计.md) · [UI 运行时调试工具链专项设计](UI运行时调试工具链专项设计.md) · [UI 制作规范](UI制作规范.md) · [UI 命名与控件词表参考](UI命名与控件词表参考.md)

## 1. 定位与工具边界

### 1.1 一条主线

```text
起页/打开 → Prefab Stage 布局 → 命名/暴露 → 样式/文案 → 生成契约
→ 增量校验 → 布局预览/截图 → 工程登记 → 绑定生成/接线
→ 全量校验 → 运行时调试/真机验收 → 契约基线
```

- **唯一画布**：Unity Prefab Stage。工具只做约束、生成、校验、预览与跳转。
- **唯一入口**：Hub `LiteGame/工具链` 的 `ui` 域 + `python scripts/toolchain.py`；不新增顶层菜单、不建第二份索引。
- **唯一真相**：prefab 是视觉真相；`tbuiform` 是运行时目录；契约/清单是生成物，禁手改。
- **一条主线**：美术与工程共享同一上下文（目标页、设备档、语言、选中节点），不各存一份。

### 1.2 工具分类

| 类 | 做什么 | 形态 | 典型 ID |
| --- | --- | --- | --- |
| 制作 | 工作台、起页、页面登记、控件插入 | window/page | `ui.workbench`、`ui.page-scaffold`、`ui.page-register` |
| 绑定 | 标记编辑、改名、引用扫描、生成、hash | window/action | `ui.binding`、`ui.bind-generate`、`ui.binding-hash` |
| 契约 | 契约生成、diff、引用对账 | action/page | `ui.contract`、`ui.contract-diff` |
| 样式 | 令牌刷新、应用、漂移报告 | action | `ui.style.*` |
| 校验 | 增量/全量规则、CI 报告 | action | `ui.validate`、`ui.validate-art` |
| 预览 | 布局档、设备档、截图矩阵 | window/action | `ui.preview` |
| 资源 | 图集/字体/收集组/第三方引用检查 | action | `ui.assets.*` |
| 调试 | 运行时深检、命令、写回 | window/action | `ui.runtime-debug`（见调试专项） |

> 具体 ID 在实现批才进 `toolchain.json`；本文不提前伪造清单行。工具完成判据是“有消费者 + 有产物 + 有正负例”，不是“菜单存在”。

### 1.3 平台接缝

每个工具必须声明：

- `id` / `kind`（window/page/action/cli）/ `plane`（editor/runtime）/ `purpose`（dev/test/release）。
- `writes`（asset/source/generated/none）与 `outputs`（路径/类型）。
- `safety`（dryRun/undo/confirmation）与失败语义。
- `docs`（本文对应章节）。
- 结果输出 `[ui/<tool>]` + 变更/失败计数；失败显式，不静默成功。

资产写盘只走 Unity 序列化/Pipeline 与 `ToolInteractionSession`；一次性写盘走 `ToolWriteScope`。窗口关闭给“本次变更 + 可回滚项”。

## 2. 工作台

<a id="ch-workbench"></a>

### 2.1 形态

**一个宿主窗口 + 多个透视（Perspective）**，共享顶部上下文：

```text
┌──────────────────────────────────────────────────────────────┐
│ 页面选择器 │ 目标 prefab │ Lane: 美术/TA/工程 │ 设备档 │ 语言 │
├──────────────┬───────────────────────────────┬───────────────┤
│ 页面/控件树   │  当前透视                      │ 属性/契约/报告 │
│ 控件库        │  (绑定/样式/校验/契约/预览)     │               │
├──────────────┴───────────────────────────────┴───────────────┤
│ 问题列表 / 生成物 hash / 最近操作 / 变更摘要                  │
└──────────────────────────────────────────────────────────────┘
```

- 透视是**同一窗口的页签/模式**，不是多个窗口；避免“Stage 跟随/显示根解析/预览作废”上下文重复实现。
- 选中页面/prefab/节点是共享上下文；切换透视不丢上下文。
- 工作台不接管几何/锚点/层级；这些由 Prefab Stage 原生编辑。
- 工作台可关闭；不持有运行时状态；不成为运行期依赖。

### 2.2 透视

| 透视 | 内容 | 主要使用者 |
| --- | --- | --- |
| **页面** | 页面清单、起页、登记、页面元数据、契约入口、预览入口 | 美术/工程 |
| **控件库** | `Tmp_*` 列表、WidgetSpec、插槽说明、插入到 Stage、影响面 | 美术/TA |
| **绑定** | BindNode 树、命名预设、批量改名、引用扫描、生成、hash | 工程/TA |
| **样式** | 令牌列表、引用/裸值、应用/对账、字体/图标检查 | TA/美术 |
| **契约** | 生成、diff、controls/text/styles/templates、引用对账 | 工程 |
| **校验** | 增量/全量报告、按规则族过滤、定位节点、例外登记 | 双方 |
| **预览** | 布局档、设备档、语言/长文本/安全区、截图矩阵 | 美术/工程 |

### 2.3 角色 Lane

| Lane | 可用 | 禁止 |
| --- | --- | --- |
| **美术** | 起页（仅 prefab）、控件库、布局、令牌、静态 key、暴露标记、契约生成、增量校验、预览/截图 | 页面登记、Lua/表写入、绑定生成、逻辑接线 |
| **TA** | 令牌/字体/图集/材质/控件模板、控件规范、性能预算、全量校验 | 业务逻辑、页面导航 |
| **工程** | 页面登记、绑定生成、契约评审/消费、逻辑接线、全量校验、运行时调试 | 未经美术评审改视觉/字号/间距/切图 |

- Lane 是 **UX 分层与默认动作集**，不是权限系统；同一 owner 仍遵守“一个 prefab 同一时间一个 owner”。
- 所有 Lane 共享 Stage、报告、预览与上下文；不各存一份。
- Lane 切换不改变资产内容；只改变可见动作与默认校验集。

## 3. 制作流水线

### 3.1 起页（美术）

输入：页面名（`Page_<Semantic>`）、视觉基线（`Tmp_BaselineA/B`/`Tmp_ListScreen`/空白骨架 `Tmp_Page`）。页面类型（全屏/浮层/HUD）由登记时的 `layer`+`full_screen` 表达（HUD 走独立轨，不登记 `tbuiform`）；目标模块由资源收集目录/模块目录表达，不是起页输入。

动作：

1. 从视觉基线**复制**为自包含 prefab（不生成 Variant，避免跨 owner 继承）。
2. 改根名与文件名一致；`EnsureRootContract` 幂等补齐根组件：`Canvas`、`CanvasScaler`（`ScaleWithScreenSize`，1920×1080，`match=0.5`）、`GraphicRaycaster`、`CanvasGroup`；安全区优先挂**现有拉伸 `Cot_*` 内容容器**（`Tmp_BaselineA/B` → `Cot_Baseline`），已有 `SafeAreaReceiver` 则保留（`Tmp_ListScreen` 内容节点已带），都没有才新建 `Cot_SafeArea` 并把原内容按序搬入。
3. 不写 `tbuiform`、不生成 Lua、不生成绑定类。
4. 生成页面草稿元数据（`UiPageManifest` 条目：Owner=美术、状态=草案）。
5. 草稿契约：美术跑 `ui.contract` 时，批量把草稿页纳入（`formId=0`，见 §5.2），作为交接前可评审产物。

产物：`Assets/ShareResource/UI/Screens/Page_<Semantic>.prefab` + 草稿清单条目 + 草稿契约。

失败语义：预检失败零产物；复制/根契约/清单写盘任一步失败都删除新建 prefab，并把清单恢复为操作前状态（不静默、不留半成品）。

### 3.2 布局（美术）

- 在 Prefab Stage 完成；使用控件库插入 `Tmp_*` 实例（嵌套 prefab），不复制内部结构。
- 使用锚点/布局组件/安全容器；遵守[UI 制作规范 §布局](UI制作规范.md#ch-layout)。
- 控件库插入后**不自动改名**；焦点落到命名输入，由作者确认；避免工具自动重名掩盖冲突。
- 影响面只读：选中 `Tmp_*` 时显示引用该模板的页面/节点数量；实例更新走 Unity 原生 Overrides，工具不代写。

### 3.3 命名与暴露（美术）

- 节点名按[UI 制作规范 §命名](UI制作规范.md#ch-naming)文法；前缀预设与语义词来自[UI 命名与控件词表参考](UI命名与控件词表参考.md)。
- 需要工程接线的节点挂 `BindNode`，填 `BindName`；`BindName` 默认等于节点名，也可为 API 稳定性独立命名；页面内唯一。
- 仅暴露“逻辑需要操作”的控件；装饰节点、模板内部件不挂 `BindName`。
- 静态文案节点挂 `LTextKeyRef`（目标组件）或填 key；`LTextKeyRef` 只存 `Key`，不带参数/复数/富文本替换；节点必须自带 TMP/UGUI 文本。动态数值不挂静态 key。
- 批量改名：默认同步节点名与 `BindName`；若 `BindName` 为 API 稳定性独立命名，则只改节点名并重生成契约；同时列出 Lua/C# 引用点。

### 3.4 样式与文案（美术/TA）

- 颜色/字号/间距/圆角/动效优先挂令牌引用；裸值被校验器报告。
- 文字角色（Title/Body/Caption/Label/Emphasis）来自令牌；不按页面随手改字号。
- 静态文案 key 必须存在；中/英预览；占位符校验；长文本/伪本地化预览。
- 图集/九宫格/Image.Type 按用途设置；切图来源与压缩进资源检查。

### 3.5 生成契约（美术触发，双方消费）

- 从 prefab 生成 `UiPageContract`（见 §5）；无契约不进入工程交接。
- **草稿契约**：`ui.contract` 批量在按表行生成之后，纳入清单中 `forms` 为空、状态 `Draft` 的页面（`formId=0`、`luaPath=null`、草稿层/全屏默认，维护字段取清单）；同一 prefab 已在表行集合中则不重复。缺清单时显式告警“草稿契约未纳入”，不伪造。
- 漂移对账：草稿走 `Regenerate(allowDraft:true)`，仍逐字节比较判 Error（布局改动后必须重生成）。
- 契约生成是确定性动作：深度优先、无时间戳、字段排序稳定；重复生成无 diff。
- 契约 diff 视为 UI API 变更；破坏性变更需双方评审。页面段破坏性口径：`formId 0→真实`、`layer`/`fullScreen`/`cacheStrategy` 变化、`luaPath null→路径` 非破坏；真实 `formId` 变更、路径→null/换路径仍破坏（草稿晋升不应被误判）。
- 契约生成后，美术跑增量校验并出截图矩阵。

### 3.6 页面登记（工程）

输入：草稿契约（`formId=0`，见 §3.5）、页面名、id、layer（0–3）、full_screen、缓存策略（维护字段，默认 `Lru`）。逻辑语言本版固定 Lua（C# 骨架依赖 `BindRoot`/命名空间配置，归 §3.7 接线前置；窗口显式显示该边界，不静默降级）。

动作：

1. 预检：契约存在/可读、`page.name` 匹配、`formId=0`、`prefabGuid` 与当前 prefab 一致（防同名旧契约）；页面清单条目为 `Draft` 且无登记行；id 不冲突/不在保留区；layer 0–3；prefab 存在。全部在写面之前。
2. 生成 Lua 最小骨架（已存在则保留，不覆盖作者逻辑）。
3. 写 `Luban/Data/#uiform.xlsx` 一行（id/lua_path/prefab/layer/full_screen；**备份先于写**，失败也回传备份句柄），随后跑 `codegen.luban` → `codegen.build-hash`（红线 1）。
4. 更新 `UiPageManifest`：对账 → 状态 `Draft → Registered`、写入缓存策略、刷新 `contractPresent`。
5. 重生成契约填真实 `formId`/layer/fullScreen/luaPath（`ui.contract` 判据同源）；失败显式报红，不回滚已登记的清单，提示重跑 `ui.contract`。
6. 绑定 hash 清单条目：仅页面挂 `BindRoot`（C# 生成类）时记录；无 `BindRoot` 不伪造条目。

安全：
- 契约/清单/表任一预检失败都零产物（不建 Lua、不写表）。
- 表写失败：按备份回滚并**复核行不存在**；进程在保存后被杀/超时也走同一核验（假端口可注入该场景）。
- Luban/BuildHash 失败：回滚表 → 复核 → 重跑 Luban 复原生成读码面；清单仍 `Draft`。
- 清单保存失败（表已写）：显式错误 + 重跑 `ui.page-manifest`，不回滚表（避免生成物与表再次错位）。
- 表写入仍由 Python 助手脚本承担；Unity 窗口只编排与呈现。

### 3.7 接线（工程）

- 消费契约 `controls[]`：只允许使用契约内 `bindName`；新增暴露需回美术补标记并重生成契约。
- 数据流按[UI 系统总设计 §数据流](UI系统总设计.md#ch-data-flow)：UseCase/ViewModel → 页面逻辑 → 绑定索引 → 控件。
- 页面关闭清理订阅/异步；列表整表替换；高频数据不跨 Lua。
- 全量校验 + PlayMode/Player 验证；视觉由美术签收，行为由工程签收。

## 4. 绑定工具

<a id="ch-binding"></a>

### 4.1 标记模型

| 标记/组件 | 作用 | 规则 |
| --- | --- | --- |
| `BindRoot` | 生成根：ScriptName/Namespace/ScriptsFolder/BaseClass | 一个生成单位一个根；嵌套 `BindRoot` 独立生成 |
| `BindNode` | 暴露节点：BindName/CustomTypeName/Comment | 只挂暴露节点；BindName 唯一；空名=仅代码生成不进运行时索引 |
| `BindName` | 运行时索引名/契约 API 名 | 默认等于节点名，可独立命名；PascalCase + 前缀_语义；禁空格/中文/重复 |
| `CustomTypeName` | 手动指定类型 | 留空自动检测；`GameObject` 表示引用节点；其余填完整类型名 |
| `Clone_` | 条目模板 | 不暴露；列表条目内部件不设 BindName |

### 4.2 编辑面

- BindNode 树：按页面展示 BindRoot/BindNode、类型、驱动方式、是否有契约。
- 命名预设：词表下拉 + 语义输入 → 组合 `前缀_语义`，默认同步节点名与 BindName；可锁定 BindName（API 稳定性）。
- 批量改名：选中多个节点 → 预览 → 应用（Unity Undo）；列出 Lua/C# 字符串字面量引用点。
- 类型候选：根据节点组件自动推荐；类型不符/缺组件/重复标记/失效引用报错。
- SceneView 叠加：显示绑定节点与名字；可开关。
- 未暴露交互节点提示：Button/Toggle/Slider 等可交互件未挂 BindNode 时给 Warning（不强制，静态装饰按钮可能有其他入口）。

### 4.3 生成

- **运行时索引**：`BindNode.BindName` → `BindIndexBuilder`；不需要生成类。Lua 通过 `self.ui:SetText("Button_Ok", ...)` 等调用。
- **C# 生成类**：`BindRoot` → `BindGenerator` 生成 `Xxx.cs`（可手写 partial 主类）与 `Xxx.Designer.cs`（字段）；字段是强类型引用，**不参与运行期索引登记**（`UIBindBase` 仍经 `BindIndexBuilder` 取控件）。
- **选择规则**：
  - 高频/复杂/需要强类型/需要 C# 服务调用 → C# 页面（`UIBindBase` + 生成类）。
  - 低频/纯展示/由 Lua 业务编排 → Lua 页面。
  - 同一页面只选一种主逻辑语言，不混用两套生成。
- **生成物纪律**：生成文件禁手改；主类可手写；生成器确定性、重复执行无 diff。
- **hash/清单**：记录源 prefab 绑定面 hash + 生成物路径；CI 复算；改了 prefab 未重生成/未记录 → 红。
- **引用扫描**：扫描 Lua/C# 字符串字面量中对 BindName 的引用，输出 `file:line`；用于改名同步与“未消费/越界引用”检查。

### 4.4 运行时索引与契约

- 运行时索引只保存名字→组件，不缓存上一实例引用；实例化后解析并校验一致性。
- 契约 `controls[]` 是绑定生成与引用对账的输入；`bindName` 必须在契约内。
- 改名/删除标记后重生成无残留；生成索引与运行时扫描对同一实例得到相同名字/类型。
- 缺类型/重名/路径失效有负例；报告定位资产与节点，不静默映射错控件。

## 5. 页面契约

<a id="ch-contract"></a>

### 5.1 定义

`UiPageContract` 是从 prefab + `tbuiform` 行生成的**只读页面接口描述**，是美术→工程的唯一交接物：

- 真相：prefab（BindNode/LTextKeyRef/样式引用/嵌套模板）+ `tbuiform` 行。
- 产物：`Assets/LiteGame/Editor/UiTools/Contracts/<PageName>.contract.json`；不进运行时/不进包。
- 写入：生成器写；禁手改；编辑器显式触发。
- 版本：`schemaVersion` 主.次；主版本不兼容，次版本只做向后兼容追加；废弃字段至少保留一个次版本。
- 消费：工作台契约透视、校验规则、引用对账、CI diff、工程接线。

### 5.2 字段模型

```json
{
  "schemaVersion": "2.0",
  "generator": { "name": "ui-contract", "version": "1.0" },
  "page": {
    "formId": 12,
    "name": "Page_Lobby",
    "prefabPath": "Assets/ShareResource/UI/Screens/Page_Lobby.prefab",
    "prefabGuid": "…",
    "luaPath": "UI.Page_Lobby",
    "layer": 1,
    "fullScreen": true,
    "cacheStrategy": "Lru",
    "owner": "art.ui",
    "designRef": "…"
  },
  "controls": [
    { "bindName": "Button_Start", "nodePath": "Cot_Body/Button_Start", "type": "UnityEngine.UI.Button", "kind": "Command", "required": true },
    { "bindName": "List_Rooms", "nodePath": "Cot_Body/List_Rooms", "type": "LiteFramework.UI.VirtualList", "kind": "Command", "required": true }
  ],
  "text": [
    { "nodePath": "Cot_Header/Txt_Title", "key": "UI.Lobby.Title", "args": 0 }
  ],
  "styles": [
    { "nodePath": "Cot_Header/Txt_Title", "role": "Title", "token": "font.title" }
  ],
  "templates": [
    { "nodePath": "Cot_Body/List_Rooms/Content", "template": "Tmp_RankCell", "bindingHash": "…" }
  ],
  "events": [
    { "bindName": "Button_Start", "event": "onClick", "handler": "OnStart" }
  ],
  "sourceHash": "sha256:…"
}
```

- **契约不含时间戳字段**（无 `generatedAt`）；需要审计时由工具日志记录。
- `controls[].kind`：`Bound`/`Command`/`None`，用于运行时所有权与调试读模型。
- `controls[].required`：页面逻辑必须消费；未消费在引用对账里报 Warning。
- `templates[]` 只记录模板使用与 bindingHash，不展开模板内部件。
- `events[]` 可选；事件绑定仍以代码为准，契约只做对账提示。

### 5.2.1 生成口径（v2.0 冻结）

<a id="ch-contract-rules"></a>

- **`sourceHash` 输入**：只覆盖 prefab 的接口面，按域分隔的规范行计算 SHA-256：
  - `control`：`bindName/nodePath/type/kind/required`
  - `text`：`nodePath/key/args`
  - `style`：`nodePath/role/token`
  - `template`：`nodePath/template/bindingHash`
  - `event`：`bindName/event/handler`
  - **不含**页面元数据（formId/luaPath/layer/fullScreen/cacheStrategy/owner/designRef）、生成器版本、时间戳。
- **模板边界**：嵌套 prefab 实例根只记一条 `templates[]`；其内部件不进 `controls[]`/`text[]`/`styles[]`。只有 `Tmp_*` 模板进入 `templates[]`；其他嵌套 prefab 作为不透明节点跳过并给 Warning。模板无 `BindRoot`/`BindNode` 时 `bindingHash` 省略（不写所有空模板相同的常量 hash）。
- **过滤**：`Clone_` 子树、嵌套 `BindRoot` 子树不进页面接口面；根节点上的 `BindName` 仍可暴露（`nodePath` 为空串）。
- **`kind` 初始推断**：交互/列表/进度类组件（Selectable、ScrollRect、VirtualList、SimpleListView、UIWidget、ProgressBar、Stepper、UIDropdownWrap）记 `Command`；其余记 `None`。`Bound` 需静态绑定标记出现后再产生，当前生成器不臆造。
- **`required` 初始推断**：`Command` 记 `true`，`None` 记 `false`；后续引用对账可细化。
- **`cacheStrategy`**：`tbuiform` 尚无缓存策略列；它是**页面清单维护字段**（默认 `Lru`，可在工作台/登记窗口选 `Resident`/`Lru`/`DestroyOnClose`），对账保留、契约投影同值，运行时暂不消费。
- **草稿契约**：未登记页（起页后、`tbuiform` 无行）可生成契约，`page.formId=0`、`luaPath=null`、`layer/fullScreen` 取单源默认（`UiContractGenerator.DraftLayer=0`/`DraftFullScreen=true`）；登记后重生成填真实身份。`UiPageContract.schema.json` 的 `formId` 最小值为 0 即此义。
- **批量与多行**：`ui.contract` 按 `tbuiform` 全量生成——表只读一次、按 prefab 去重、输出确定性。根名不在 `Page_/Hud_/Tmp_` 契约面的非常驻壳（若出现）显式跳过并告警，不计失败；同一 prefab 多行登记取**首行**，其余行由页面清单 `forms[]` 承担并告警。
- **`LTextKeyRef`**：只存 `Key`；`text[]` 的 `args` 固定 0。参数/复数/富文本替换属于工程动态文案，不进静态契约。
- **确定性**：深度优先、按 `nodePath`（事件按 `bindName|event`）稳定排序；JSON 固定字段序、LF 换行、UTF-8 无 BOM；负例只返回错误，不写盘。

### 5.3 生成规则

1. 只收非空 `BindName`；页面内唯一；类型可解析。
2. 深度优先、稳定排序；无时间戳/随机值；重复生成无 diff。
3. `Clone_` 条目模板不暴露；嵌套 `BindRoot` 独立生成；模板内部件只进 `templates`。
4. 文本 key 必须存在；静态文案节点不得同时被 Lua 动态写。
5. 纯视觉属性（颜色/尺寸/锚点）不进契约。
6. 契约 `sourceHash` 与 prefab 绑定面/文本/模板引用一致；漂移即红。

### 5.4 交接判据

- 契约已生成且 `sourceHash` 与 prefab 一致。
- 美术增量校验全绿；截图矩阵齐全（中/英/长文本/安全区）。
- 静态文案全部走 key，无 Lua 双写。
- `controls[]` 与工程接线一致；无越界引用。
- 根 Canvas/CanvasGroup/GraphicRaycaster/SafeArea、图集/模板依赖通过。
- PlayMode/Player 可开、可点、可返回；视觉/行为双签。

## 6. 校验核心

<a id="ch-validation"></a>

### 6.1 形态

- 同一校验核心供工作台、菜单、CLI、EditMode 测试调用。
- 输入：资产集合 + 源码面（Lua/C# 引用）+ 规则配置。
- 输出：`UiIssue { ruleId, severity, asset, node, message, fixHint }` + 汇总。
- 报告不依赖打开窗口；CI 可非零退出；正例与违规负例同库。

### 6.2 规则族

| 族 | 规则示例 | 级别 | 执行面 |
| --- | --- | --- | --- |
| 结构 | 根缺 Canvas/CanvasGroup/GraphicRaycaster；SafeArea 缺失；页面根不是 prefab 根 | Error | EditMode |
| 命名/绑定 | 文法不合、前缀未登记、BindName 重复/为空、类型不符、生成物 hash 漂移 | Error/Warning | EditMode |
| 契约 | schema 不符、sourceHash 漂移、controls 越界/未消费、文本双写 | Error/Warning | EditMode/CI |
| 样式 | 裸颜色/裸字号、令牌不存在、字号不在阶梯、状态色硬编码 | Warning/Error | EditMode |
| 文本 | key 缺失、占位符不符、语言列缺失、富文本越界、缺字 | Error/Warning | EditMode + 表校验 |
| 布局/适配 | CanvasScaler 不符、锚点漂移、安全区缺失、超宽屏溢出、布局嵌套过深 | Warning（页面根 CanvasScaler 偏离基线/第二套缩放＝Error——UI-LAYOUT-01 冻结值） | EditMode |
| 资源 | 缺引用、收集组缺失、图集/字体/sprite 名册、第三方直引、debug 资产入包 | Error/Warning | EditMode + 构建 |
| 性能 | raycastTarget 过多、LayoutGroup/ContentSizeFitter 过深、Animator、图集依赖过大、粒子/材质实例超预算 | Warning（页面根 Animator＝Error——UI-PREFAB-03） | EditMode + Player |
| 行为 | 复用视觉残留、输入穿透、焦点、列表重绑、语言切换、取消 | Error | PlayMode |

### 6.3 严重级别与例外

- `Error`：阻断提交/CI；必须修复或登记例外。
- `Warning`：可提交但必须可见；同一页面 Warning 数量进入质量度量。
- `Info`：提示优化，不阻断。
- **例外登记**：`UiRuleExceptions`（`Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`，手工维护的配置、不是生成物；schema：[UiRuleExceptions.schema.json](UiRuleExceptions.schema.json)）。
  - 条目必填：`ruleId` / `asset` / `owner` / `reason` / `expiry`（`yyyy-MM-dd`）；`node` 可空＝该资产任意节点；`asset`/`node` 支持任意位置 `*` 通配。
  - 命中且未过期：问题**仍然出现**，级别降一级（`Error→Warning`、`Warning→Info`）并在报文末尾标注「例外：Owner，到期 expiry」——不静默、不删除问题。
  - 过期：不再降级，问题回到原级别；同时报 `ui.rule-exception.expired`（Warning）要求清理。
  - 非法（缺字段/日期格式错）：报 `ui.rule-exception.invalid`（Error），该条不生效。
  - v1.0 不含：版本号到期（`v1.3` 形态）、条目级评审流程；到期统一按日期。
- 规则阈值集中配置，不写死在扫描器；规则变更走评审。

### 6.4 增量与全量

| 模式 | 范围 | 目标耗时 | 用途 |
| --- | --- | --- | --- |
| 增量（美术 Lane） | 本次改动的 prefab + 直接依赖 | <10s | 提交前自检，中文可执行提示 |
| 全量（工程/CI） | `Screens/Widgets/Hud` + 契约 + 引用 | 可较长 | 合并门禁/夜间 |
| 运行时（PlayMode） | 行为/清理/可点性 | 按用例 | 真实生命周期 |

- 增量不替代全量；增量全绿不等于门禁通过。
- 报告按规则族/严重度/资产过滤；行内“定位节点”反查 prefab。
- 修复建议只给安全动作；自动修复必须预览、Undo、可回滚。

## 7. 预览

<a id="ch-preview"></a>

### 7.1 三档

| 档 | 做什么 | 不做什么 |
| --- | --- | --- |
| **布局档** | 实例化目标 prefab；保存即热重载；看布局/边界/安全区/语言 | 不跑 Boot/Lua/真实数据 |
| **数据档** | 通过运行时调试注入 `UiDebugState`（空/大/长文本/错误/加载/伪本地化） | 不在预览工具里复制第二套运行内面板 |
| **真链路** | Boot → 登录 → 目标页 / PlayMode；真实数据与行为 | 不用布局档冒充运行时验收 |

- 布局档场景是编辑器工具资产，不进收集组/不进包。
- **现行口径（P2-3 B3 冻结）**：布局档由 `UiPreviewDriver` 实例化目标 prefab；设备档/语言/字体缩放在 Play 内免重启切换（编辑器写 `SessionState` → 驱动轮询应用；安全区经 `SafeAreaReceiver.SetStrategy` 注入）。
- **设备档单源**：`Assets/LiteGame/Editor/UiTools/UiDeviceProfiles.json`（schema [UiDeviceProfiles.schema.json](UiDeviceProfiles.schema.json)）——六横屏档＋竖屏预留、`inMatrix` 标记；驱动/工作台/矩阵三方共用，不允许第二份分辨率/inset 字面量。GameView 分辨率经内部 API 反射切换（`SetCustomResolution`），失败降级为仅模拟安全区并在矩阵索引记 `resolutionApplied=false`。
- **语言**：tbltext 文件直载（`ConfigTableFileSource` 全表字节模式 + `LTextCatalog`）构建 `LocalizationService`；`PseudoLocale` 装饰器产出 ≥30% 长文本与音标符；不改表、不写盘。直载失败时驱动上报 `LocaleStatus=unavailable`（静默保持作者文本），矩阵据此把该组合记失败并写降级记录，**不假绿**。
- **字体缩放**：1.0/1.15/1.3（与总设计 §7.5 同源），对页面 TMP 记录原值后乘档，切档/退出恢复。
- **交互切档**：预览工具条提供设备档（含分辨率）/语言/字体三组轮换按钮，写 `SessionState`＋修订号，驱动轮询免重启应用；退出预览还原 GameView 分辨率与页面状态。
- **截图矩阵**：`ui.preview-matrix`（工作台 → `LiteGame/UI/预览/截图矩阵`；运行器亦可 API 启动）——页×设备档×语言×字体缩放组合，Play 内逐组合应用→稳定帧→`ScreenCapture`→`Artifacts/PreviewMatrix/<页>/`＋`matrix.json`（组合/分辨率应用结果/降级记录/中断标记）。**交接证据，不是门禁替代**；`.gitignore` 已排除 `[Aa]rtifacts/`。
- **减少动效维度延后**（P2-3 B3 显式非目标）：布局档无质量档/系统设置基建，P3/P4 复议；矩阵维度保留扩位。
- 竖屏预览、数据档注入与真链路验收不在 B3（分别归 P4、P3、既有 Boot/PlayMode）。

### 7.2 预览与调试的边界

- 布局档预验收视觉；数据档/真链路预验收行为。
- 运行时调试的深检/命令/写回归[UI 运行时调试工具链专项设计](UI运行时调试工具链专项设计.md)，预览工具不复制。
- 预览中隐藏 DevHUD/F8，避免遮挡；退出 Play 恢复进入前场景。

## 8. 样式与资源工具

<a id="ch-style-tools"></a>

### 8.1 样式工具

- 令牌源：JSON → 生成 `UiThemeAsset`（SO）；工具只读生成物与 prefab 引用。
- **现行口径（P2-3a 冻结）**：源＝`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`（设计态、不进包；格式以 [UiStyleTokens.schema.json](UiStyleTokens.schema.json) 2.0 为准）；生成物＝`Assets/GameData/Config/UI/UiTheme.asset`（随 `Assets/GameData/Config` 收集组进包）；入口＝`ui.style.tokens`（幂等：同源重跑 `changed=false` 且不触碰资产；校验失败零写盘；手改资产被覆盖并报告变更）。
- **组按消费者落地**：P2-3a 写 `color`（12）与 `typography`（5）；P2-3 B2 补 8 枚 `color.state.*`；P2-3 B4 补 44 枚精确色（含 2 枚 `color.debug.*` 夹具色）与 5 枚 `color.state.selectable.*`，共 **69 个唯一 ID**（值允许按状态语义重值，显式 ID 消费）；`spacing/radius/elevation/motion/icon` 仍空集，随首个应用器/解析器批次补值——不预造无消费者的令牌。
- **读面**：编辑器侧经 `UiTokenCatalog`（每次操作装载一次、按参数下传，禁静态缓存）；`UiStyle`/`UiTextStyle` 的旧名保留为只读属性（调用点源兼容），值一律来自生成物。
- **漂移判定**：`ui.style.token-drift` 按 JSON 重算快照（ID/值/`sourceHash`）与 SO 逐字段比对，不一致即 Error 并提示重跑生成；生成器的"是否写盘"用同一个快照比较（单源）。
- 刷新/应用：`ui.style.colors-refresh` **三段式**：① refs 段——按 `UiStyleRef` 声明精确应用（权威写者；跳过 `IThemeGraphicWriter` 认领的目标 Graphic）；② widget 迁移段——三 widget（StateButton/TabGroup/StarRating）状态色字段按最近匹配填 token ID（已填跳过＝幂等、不命中不填＝留空合法；**不填 `color.state.selectable.*`**）；③ 裸值段——未声明节点最近匹配收敛（跳过已声明与 writer 子树）。幂等、报告分段（应用/跳过/未知令牌/填入/未命中）；"可回退"＝幂等重跑＋变更仅经工具落盘（版本管理可回退；总设计 §6.2 同批澄清）。字号/间距应用待对应引用组件批次。
- **Selectable 状态色迁移**（P2-3 B4）：`UiSelectableThemeApplier.EnsureAll(dirs)` 只给直挂 ColorTint Selectable 补 `UiSelectableTheme`，跳过嵌套 prefab 实例（源模板继承），**不写任何颜色/ColorBlock**；报告 Added/Direct/Inherited/Skipped，幂等。真实面 33 直挂 + 13 继承 = 46 覆盖。
- **文字档精确迁移**（P2-3 B4）：`UiTextStyleTool.MigrateExactRoleRefs(catalog, dirs)` 只给属性与角色档逐项一致、无标记、非嵌套实例的文本补 `UiTextStyleRef`；只加组件不改属性；多档歧义跳过并报告；实例合法覆盖由 `UiTextStyleRef.UseInstanceOverrides` 标记（不应用/不判 Mismatch，仍计未标记债）。真实面新增 23 组件/覆盖 37 节点（14 继承）。
- 漂移报告：区分“引用缺失/解析值不符/裸值/令牌不存在”；运行时实例变化不进资产报告。
- 字体检查：字体族/fallback/语言覆盖/缺字/atlas 容量。
- 图集检查：引用 sprite 是否进图集、图集分组、九宫格边界、Image.Type。
- 材质/Shader：能力校验（遮罩/裁剪/透明/目标平台），不做“一键换 shader”。

### 8.2 资源工具

- 收集组检查：页面/模板/字体/图集是否在正确收集路径；收集路径不互相包含。
- 第三方引用：产品资产直接引用第三方包路径 → Error；必须复制到 `ShareResource`。
- 依赖检查：页面依赖模板/图集/字体；模板改动的影响面只读。
- 构建检查：debug 资产（UIPatch/dev UITK/测试 prefab）不得进 release 包。

### 8.3 契约/清单生成物

<a id="ch-artifacts"></a>

| 生成物 | 路径 | 消费者 |
| --- | --- | --- |
| `UiPageContract` | `Assets/LiteGame/Editor/UiTools/Contracts/*.contract.json` | 工程接线、校验、CI、调试读模型 |
| `UiBindManifest` | `Assets/LiteGame/Editor/UiTools/UiBindManifest.json` | 绑定 hash 对账 |
| `UiPageManifest` | `Assets/LiteGame/Editor/UiTools/UiPageManifest.json` | 工作台页面清单/Owner/状态/设计稿 |
| `UiWidgetManifest` | `Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json` | 控件库/文档/校验 |
| `UiThemeAsset` | `Assets/GameData/Config/UI/`（SO） | 运行时样式解析 |

> `UiThemeAsset`：P2-3a 起由 `ui.style.tokens` 从 `UiStyleTokens.json` 生成；P2-3 B2/B4 有运行期消费链（bootstrap 一次装载 → `UIForm.ThemeProvider` / HUD `BattleViewAssets` → 装配期 `UiThemeBinder` 注入），共 69 色（12 基础 + 8 B2 状态 + 44 精确 + 5 Selectable；映射表见总设计 §6.5）；其余五组仍空集（按消费者落地）。
> 例外登记不是生成物，单独记：`Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`（手工维护，见 §6.3）。
> 生成物禁手改；路径/格式变更与 schema 同批更新。

### 8.4 控件规格与控件清单（v1.0 冻结）

<a id="ch-widget-spec"></a>

`WidgetSpec` 是控件模板的能力声明（总设计 §1.4），挂在 `Widgets/Tmp_*.prefab` 根上；`UiWidgetManifest`
是它的投影（生成物，禁手改）。字段模型与口径如下：

```json
{
  "schemaVersion": "1.0",
  "generator": { "name": "ui-widget-manifest", "version": "1.0" },
  "widgets": [
    {
      "template": "Tmp_HpBar",
      "prefabPath": "Assets/ShareResource/UI/Widgets/Tmp_HpBar.prefab",
      "prefabGuid": "…",
      "usageSegment": "HpBar_",
      "specPresent": true,
      "owner": "art.ui",
      "summary": "血条",
      "slots": [{ "name": "Cot_IconBox", "required": true, "description": "血量图标槽（Icon_Health）" }],
      "states": [],
      "events": [],
      "sizeBehavior": "Expand",
      "constraints": ["数值文本可选（ValueText 可空）"],
      "properties": [
        { "nodePath": "", "component": "HpBar", "member": "ValueFormat", "kind": "String", "displayName": "数值文本格式", "tooltip": null }
      ],
      "bindingHash": "…"
    }
  ],
  "warnings": [],
  "sourceHash": "sha256:…"
}
```

- **字段口径**：
  - `Owner`/`Summary`：模板归属（RACI，默认 `art.ui`）与一句话用途（对齐《UI 命名与控件词表参考》§3「用途」）。
  - `Slots[]`：插槽容器路径（`Cot_*`/`Slot_*`），保持声明序（header/body/footer 语义序）；`Required` 表示页面使用该控件时必须填充。
  - `States[]`：`UiWidgetState` 词表（Normal/Hover/Pressed/Disabled/Selected/Loading/Empty/Error，总设计 §6.5 单源），按枚举值序输出。
  - `Events[]`：`类型.成员` 形式，只声明模板内**真实存在的公开事件或 UnityEvent 字段**（如 `Stepper.OnChanged`、`UIEventRelay.OnClicked`）；UGUI 内建事件按 `类型.成员` 同样写法（如 `Button.onClick`）。
  - `SizeBehavior`：`Fixed`（模板固定尺寸）/`Expand`（沿父容器拉伸）/`FitContent`（随内容自适应）。
  - `Constraints[]`：事实性使用约束（自由文本，如「固定尺寸单轴」「同时可见上限 MaxVisible」），不做结构化臆造。
  - `properties[]`：**不重复声明**——真相是控件字段上的 `UiWidgetProperty`（`UiWidgetPropertyCatalog` 枚举），清单只做投影。
- **生成口径**：模板清单＝目录（`Widgets/Tmp_*`，不另存清单文件）；每模板读根 `WidgetSpec` + 受控属性 + 绑定面 hash（`UiBindGenerationHash`）；按模板名稳定排序、无时间戳、LF、UTF-8 无 BOM、重复生成逐字节一致。
- **`sourceHash` 输入**：`template|owner|summary|size` + `slot(name/required/description)` + `state` + `event` + `constraint` + `property(nodePath/component/member/kind)` + `bindingHash`；**不含** `prefabPath`/`prefabGuid`/生成器版本/告警（模板移动不改指纹）。
- **校验口径**：缺 `WidgetSpec` 记 Warning（`specPresent=false`，清单仍可生成；Error 归 §6 校验规则族）；插槽名不合约定、插槽节点不存在、插槽重名、事件为空/重名/成员解析不到、状态重复、约束空串/重名、模板根名与文件名不一致 → Error，负例不写盘。
- **作者面**：Inspector 支持「从节点发现插槽」（复用绑定面遍历单源，排除 `Clone_` 子树/模板内部件/嵌套 `BindRoot` 子树），幂等并入且保留作者填写。
- **工具入口**：`ui.widget-manifest`（Hub/CLI）生成并落盘；生成物入库作为控件库/文档/校验的共享基线。

> v1.0 不含：状态与实现的语义一致性校验（归校验规则族）、变体/实例级声明、样式令牌引用（归 §8.1）。

### 8.5 页面清单与状态词表（v1.0 冻结）

<a id="ch-page-manifest"></a>

`UiPageManifest` 是**工具维护的页面注册表**（总设计 §5.1/§10.3：编辑器清单，不进运行时、不进包）：
身份字段从 `tbuiform` 刷新，维护字段（Owner/状态/设计稿/设备档/备注）由工具 API 更新并在对账时保留。
之所以不是纯生成物：起页可在表行之前产生草稿条目（§3.1），登记后再更新状态（§3.6）。

```json
{
  "schemaVersion": "1.0",
  "generator": { "name": "ui-page-manifest", "version": "1.0" },
  "pages": [
    {
      "name": "Page_Lobby",
      "prefabPath": "Assets/ShareResource/UI/Screens/Page_Lobby.prefab",
      "forms": [{ "formId": 12, "layer": 0, "fullScreen": true }],
      "luaPath": "UI.Page_Lobby",
      "cacheStrategy": "Lru",
      "owner": "art.ui",
      "status": "Registered",
      "contractPath": "Assets/LiteGame/Editor/UiTools/Contracts/Page_Lobby.contract.json",
      "contractPresent": false,
      "designRef": null,
      "deviceProfiles": [],
      "notes": null
    },
    {
      "name": "Tmp_Dialog",
      "prefabPath": "Assets/ShareResource/UI/Screens/Tmp_Dialog.prefab",
      "forms": [
        { "formId": 201, "layer": 3, "fullScreen": false },
        { "formId": 206, "layer": 1, "fullScreen": false }
      ],
      "luaPath": null,
      "cacheStrategy": "Lru",
      "owner": "art.ui",
      "status": "Registered",
      "contractPath": "Assets/LiteGame/Editor/UiTools/Contracts/Tmp_Dialog.contract.json",
      "contractPresent": false,
      "deviceProfiles": []
    }
  ],
  "warnings": [],
  "sourceHash": "sha256:…"
}
```

- **条目按页面（prefab）唯一**：一个 prefab 可在 `tbuiform` 登记多行（如 `Tmp_Dialog` 的 201 系统错误面 / 206 确认面），登记行进 `forms[]`（按 `formId` 升序）；`luaPath` 多行不一致时置 null 并告警。
- **状态词表（五态，单源）**：`Draft`（草案）/`InReview`（评审）/`Registered`（已登记）/`Wired`（已接线）/`Frozen`（冻结）。
  迁移表：`Draft→InReview|Registered`；`InReview→Draft|Registered|Frozen`；`Registered→Wired|InReview|Frozen`；
  `Wired→Frozen|Registered`；`Frozen→InReview`（解冻须评审）；同值＝无操作。
- **对账口径**（表 + 已有清单 + 契约存在性 → 清单）：
  - 身份字段以表为准刷新：`forms[]`/`luaPath`/`prefabPath`/契约路径与存在性；
  - 维护字段保留：`owner`/`status`/`designRef`/`deviceProfiles`/`notes`；
  - 表行新增 → 新条目（`status=Registered`、`owner=默认美术`）；清单有而表无：`Draft` 保留、其余告警（条目仍保留供清理）；
  - 错误（不写盘）：重复 `formId`、非法状态、清单不可读、`tbuiform` 读取失败（不伪造空表）、输出路径不在 `Assets/`；
  - 告警（可写盘）：非草稿条目表行缺失、prefab 不存在、多行 `luaPath` 不一致；
  - 确定性：按页面名排序、无时间戳、LF、UTF-8 无 BOM；`sourceHash` 覆盖身份+维护字段（不含告警/生成器版本/时间戳）。
- **编辑 API（纯内存 + 显式保存）**：`EnsureDraft`（起页草稿，幂等）、`SetStatus`（迁移校验）、`SetEditorial`（维护字段）；每次编辑后重算 `sourceHash`。
- **工具入口**：`ui.page-manifest`（Hub/CLI）对账并落盘；起页成功后由脚手架自动对账一次（失败只记结果日志、不回滚页面，可重跑工具补救）。
- 契约的 `formId` 取**主表单行**：多行登记的页面（`Tmp_Dialog`）契约只描述首行，批量生成对该差异显式告警；其余行由调用方按 `forms[]` 选择。

> v1.0 不含：手改检测规则（`sourceHash` 已备，规则归校验族）、状态与实现的一致性校验、清单 diff。

## 9. 工具清单与阶段

<a id="ch-phases"></a>

### 9.1 目标工具清单

| ID | 标题 | 形态 | 写面 | 说明 |
| --- | --- | --- | --- | --- |
| `ui.workbench` | UI 工作台 | window | asset/source | 唯一制作入口；页面/控件/绑定/样式/契约/校验/预览透视 |
| `ui.page-scaffold` | 起页（美术） | window | asset | 复制视觉基线为自包含 prefab；不写 Lua/表 |
| `ui.page-register` | 页面登记（工程） | window | asset/source/generated | 写 tbuiform、Lua/C# 骨架、Luban、hash |
| `ui.bind-generate` | 绑定生成 | action | generated | C# 生成类 + hash 记录 |
| `ui.binding-hash` | 绑定 hash 对账 | action | data | 记录/校验生成物 |
| `ui.contract` | 契约生成/刷新 | action | asset(data) | 生成 `UiPageContract` |
| `ui.contract-diff` | 契约 diff | action | none | 与基线比较；CI 可用 |
| `ui.widget-manifest` | 控件清单生成 | action | asset(data) | 生成 `UiWidgetManifest` |
| `ui.page-manifest` | 页面清单对账 | action | asset(data) | 生成 `UiPageManifest` |
| `ui.validate` | 全量校验 | action | none | 统一报告 |
| `ui.validate-art` | 增量校验（美术） | action | none | <10s，中文提示 |
| `ui.style.*` | 令牌刷新/应用/报告 | action | asset/none | 颜色/文字/间距 |
| `ui.preview` | 布局预览 | window | none | 设备档/语言/截图 |
| `ui.assets.*` | 收集组/图集/字体/第三方检查 | action | none | 资源门禁 |
| `ui.runtime-debug` | 运行时调试 | window | asset(patch) | 见调试专项 |

### 9.2 阶段

| 阶段 | 范围 | 退出条件 |
| --- | --- | --- |
| T0 基线 | 资产/页面/控件盘点；旧工具冻结；页面清单；校验规则盘点 | 清单可用；旧入口收口；基线报告可复查 |
| T1 契约与清单 | `WidgetSpec`、`UiPageManifest`、`UiWidgetManifest`、Page Contract、schema、diff | 契约可生成/对账；正负例齐 |
| T2 工作台 | 宿主 + 页面/控件/绑定/校验透视；起页/登记拆分 | 美术起页、工程登记；上下文共享 |
| T3 样式与预览 | 令牌工具、设备档、截图矩阵、增量校验 | 美术独立交接；预览覆盖中/英/长文本/安全区 |
| T4 CI 与全量 | 规则族全量、hash/契约/资源门禁、报告 | CI 能红；报告可定位 |
| T5 运行时调试接入 | 页面命令/状态注入/深检/写回 | 见调试专项 |

### 9.3 验收

- 同一页面：美术只做视觉/契约，工程只做登记/接线，接口只通过契约。
- 工具不制造第二套名字/样式/运行时真相。
- 生成物可重建、可对账；手改生成物被检出。
- 工具失败显式、可回滚；不覆盖未保存编辑。
- release 零工具/调试残留。
