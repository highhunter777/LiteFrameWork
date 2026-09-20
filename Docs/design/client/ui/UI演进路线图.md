# UI 演进路线图

> 状态：持续维护
> 版本：1.0
> 更新日期：2026-09-21
> 适用范围：UI 批次、依赖、缺口、退出条件和已知风险
> 维护责任：客户端 UI
> 类别：**规划/状态**——UI 线的全部批次、待办、缺口对账、前置、遗留与判据留档。规范见《UI 制作规范》、机制见《UI 运行时专项设计》、工具见《UI 编辑器工具专项设计》；历史施工记录见 `Docs/Archive/2026-09-20/`。

## 1. 已交付基线（2026-09-20）

- **25 件控件模板**全部生成（编辑态 + Play 断言 25/25）+ 确定性构建/校验菜单；受控 API 批⑦ P0（G1/G3/G7/G10）与 G20 动效口（2026-09-19）已交付，模板自检 **36/36**。
- **转场编排层**已交付（2026-09-19，`Shell/UI/Transition/` 四件 + `IReplaceTransition` + `UIService` 接线，EditMode 用例 11 条全绿）。
- **YooAsset 收集组**就位（Screens/Widgets，tag `ui`，运行时 26/26 可加载）；**运行期增量重填**机制已交付（见《UI 运行时专项设计》§7.1）。
- **样式 token** 12 色收敛零漂移；**VFX 服务**已交付（`LiteSim/View`，12 条用例）。
- **R8 淡出路径**已定案修复（`FlyTextPool` → `CanvasGroup.alpha`，L2 28/28）。
- 验证基线：L1 364 绿 / L2 EditMode 28/28 / CI 双平台矩阵绿（2026-09-20 实测）。

## 2. 性能治理（批①–④）

现状三瓶颈（读码实测）：① 高频元素每帧触发 Canvas 重建（飘字/倒计时/血条）；② 渲染面基础设施缺失（无 CanvasScaler、零图集、无 HUD 分层、raycastTarget 未成规范）；③ 确定泄漏与每帧分配（VirtualList 监听累积/池只增不毁/裁剪 SetActive、Lua `params object[]` 分配装箱、`RaiseUpdate` 闭包插值串）。预算红线见《UI 制作规范》§9。

**批① 热点修复（立即可做，不依赖 M11）**——A1–A3 一组（虚拟列表一次改完）、A4–A6 一组（高频元素去重建）、A7/A8 同批（Lua 每帧分配）：

| 项 | 改法 | 验收 |
|---|---|---|
| A1 监听累积 | 一次性挂 + 幂等守卫 | Refresh 20 次后滚动，ApplyCulling 计数 = 1 |
| A2 池只增不毁 | Refresh 按 n 收缩（保高水位；不动 UIForm 常驻池） | 池容量随数据量同步下降 |
| A3 裁剪 SetActive | 窗口复用：固定 N 实例换绑索引（或按滚动增量算首末） | 滚动 500 条 SetActive ≤ 视口容量×2；`Bind(index, rt)` 语义不变 |
| A4 Countdown 每帧写文本 | 秒数相同即 return | 3s 内写文本 ≤ 4 次 |
| A5 HpBar 收敛后仍跑 | 收敛早退 + `enabled=false` | 静止 60 帧内自禁用 |
| A6 FlyText 每条 async 循环+每帧改色 | 淡出走 `CanvasGroup.alpha` + 单 ticker 集中驱动（禁原生协程）；保留销毁期守卫 | 16 条飘字稳定帧 `SendWillRenderCanvases` ≤ 3 |
| A7 Lua `params object[]` | OnUpdate 专用非分配路径（参数数组复用） | 稳定帧 GC.Alloc = 0 B |
| A8 `RaiseUpdate` 闭包+插值串 | 直调 + tag 预缓存 + `_hasOnUpdate` 门控 | 无 OnUpdate 界面稳定帧 GC.Alloc = 0 B |

**批② 渲染面基础设施（M11 前置；B2 与 B3 必须同批——只分层不打图集 = 白增断点）**：

| 项 | 改法 |
|---|---|
| B1 | `[UIRoot]` 挂 Canvas + CanvasScaler（1920×1080，Match 0.5）；每界面 Canvas 保留（rebuild 隔离红利） |
| B2 | 建 `UI.spriteatlas` + 构建器打 packingTag，与 YooAsset tag `ui` 对齐 |
| B3 | HUD 定案：单 Canvas + 静态/动态两层子 Canvas，高频元素进动态层 |
| B4 | 动效元素独立子 Canvas：构建器规则 + 校验器断言 |
| B5 | `raycastTarget` 构建器默认全关、仅交互件开 + 校验器断言 |
| B6 | overdraw 编辑器侧人工检查清单 |

**批③ 数据面（M11 前定案，防返工）**：C1 = **HUD 数据由 C# 直驱**（架构定案见《UI 运行时专项设计》§1；纪律：事件化不停壳心跳须改调用点、高频事件禁逐事件过 Lua、每帧逻辑推进挪 C# 或降频）；C2 = `BindIndexBuilder` 按 prefab 缓存"名字→节点路径"（`Editor/BindGen/` 为生成侧落点，结算页打开 ≤32ms）。

**批④ 测量与护栏**：D1 ProfilerMarker（Show/Tick/ApplyCulling/FlyText ticker/Lua OnUpdate）；D2 `IModuleStats` 扩展（tween 数/飘字数/realized 数/Canvas 数/GC 累计，禁每帧分配）；D3 基线记档（Editor + Android 真机 30s 全指标）；D4 四项编辑态校验进 CI（raycastTarget/子 Canvas/packingTag/CanvasScaler）。

**更新架构落地顺序**（《UI 运行时专项设计》§1 的施工序）：① RaiseUpdate 直调 + adapter 门控（半天）→ ② 控件双模 AutoTick+Tick（1 天，自检 36/36 零回归）→ ③ HudModel+HudTicker（2 天，SimSandbox + 假 HUD 验证，不等 M11）→ ④ UpdateHz 分频 + 过桥护栏 → ⑤ 接 B3 动态层。

**排期与风险**：批① 立即可做（一次一件附自检）；批②③ 挂 M11 前置；批④ 批①②收口后补齐，D4 进 CI 为长期护栏。风险：虚拟列表改造不动 `Bind` 契约；图集/贴图合并必须经 Unity/Pipeline（序列化红线）；池收缩只动列表内部实例池；批量改控件源文件前先算文件名冲突（历史事故）；`await` 后复检 `this == null`（批⑦ 两例教训）。

## 3. 样式编辑器批次（设计见《UI 编辑器工具专项设计》§3）

| 批 | 内容 | 验收 |
|---|---|---|
| ① | 档位单源 + 30 处挂标记（经 Pipeline）+ 22 收敛 | 模板自检 **36/36** 不回退；文本对账零漂移 |
| ② | 工具双轴（刷新/对账扩展文本轴） | 幂等；收敛清单可复现 |
| ③ | 校验进 L2（规则 1–5+7） | 全绿；故意造违规 → 用例红 |
| ④ | 编辑器窗口（Odin） | 可应用/对账/跳转；不改其它字段 |

## 4. 编辑器排期

| 件 | 排期 |
|---|---|
| UI 标记工具（阶段一） | 随控件模板落地（模板已就绪，可开工） |
| 时间轴编辑器 | **M11 demo 前必须就绪**（消费者：Sim 判定轨 / M11 表现层 / 剧情演出 / UI 动效）；自研仅三件 ≈650–950 行一次性 |
| UI 编排器（阶段二） | M11 前后（标记工具 + 模板库就绪后） |

## 5. 扩展能力与应用模板

**依赖顺序**：

```
① 跳转 Go/Back + 转场编排（转场已落地，Go/Back 的 Lua 门面待做）——同批
② 本地化（表 + LText + 校验器 + 语言切换）—— M11 前必须
③ 富文本白名单校验（依赖 ②）   ④ 覆盖层补丁（独立，可并行）
⑤ 应用模板 P0（依赖 ①②③）
```

**应用模板清单**（控件件之上、带玩法语义的成品组合件）：

| 档 | 模板 |
|---|---|
| **P0**（M11 直接用） | **动态文本 `LTextLabel`**（key + 参数 + 格式化 + 语言切换自动刷新 + 打字机模式）；战斗 HUD：技能 CD 遮罩 / 击杀播报 Kill Feed / 命中标记 / 受击方向 / 弹药 / 队伍血条；骨架屏、空状态 |
| P1 | 弹窗类；Buff/Debuff 图标条、读条施法、延迟信号；货币栏、价格标签；自适应网格、返回+面包屑（依赖 Back） |
| P2 | 排行/好友/聊天气泡/组队头像；引导遮罩、成就弹窗、点击波纹 |

`LTextLabel` 要点：key 住 prefab、文案住表（改文案不出包）；订阅 `LanguageChanged` 自动重刷；打字机走 UniTask（禁协程），与 `silenceUntilFrame` 同族纪律。

## 6. 受控 API 缺口（批⑧）

清单以《UI 控件 Lua API 参考》§3 为准：已交付 G1/G3/G7/G10/G20；待办 **P1** = G8 星级 / G9 滚动数值 / G13 步进 / G14 输入取值 / G15–G17 输入三件 / G4 红点 / G6 列表协议（定案方案 A `SetList` 行数据驱动）/ G21 Lua 侧绑定区（`UIBindIndex.BindText` 已存在未挂 `self.ui`，与 G9 同批）；**P2** = G5 切页 / G11 动图 / G12 头像 / G18 点击区 / G19 引导。
另：**界面级命名约定文档**（实例化后子控件命名，防重名）待写。

## 7. 规范缺口对账（规范 ↔ 工程现状）

| 规范条目 | 现状 | 落地 |
|---|---|---|
| 《UI 制作规范》§1 UI 相机 + CanvasScaler | ❌ Overlay、无 Scaler | 性能批② B1/B3 |
| 图集（同纹理合批） | ❌ 零图集 | 批② B2（与 B3 同批） |
| 动效子 Canvas、raycastTarget 默认关 | ❌ 未成规范 | 批② B4/B5（进构建器+校验器） |
| TMP Sprite Asset | ❌ 无 | 用到再建 |
| UI 特效夹层 | ❌ Overlay 挡死 | 依赖批② B1/B3 |
| UI 特效调度主体 | ✅ 已定案（不建服务，经 `UiFx` 口） | 触发条件出现时重评 |
| 🔴 规则进校验器 | ❌ | 批② D4 + 新增《UI 制作规范》条目 |

顺序：批② → 🔴 规则进校验器（D4）→ 特效夹层可用 → M11 HUD 按规范施工。

## 8. 流程线前置（M10 批④/M11 共同前置）

- 现状：`ProcedureLaunch/Preload` ✅，`ProcedureMain` 空转占位，`Match/Battle/Result` 三阶段**不存在**——但 M10/M11 多处设计已假定其存在（客户端接缝、HUD 挂点、安全窗口）。
- 后果：运行期增量重填与语言切换重刷当前没有真实触发者（只有调试菜单）——根因在流程线，不在 UI 层。
- 排期：流程三阶段骨架（三件 + `ProcedureArgs` 的 `roomId/frameNo/BattleContext` 字段位 + 安全窗口挂点）应排在 **M10 批④ 之前或同期**。

## 9. 遗留与已知坑

- **真机 YooAsset 分支仍 `throw NotSupportedException`**（`AssetService`，M6 交付）——收集组与加载路径已就位。
- **Luban cs-bin pass 每次 regen 会删 `Luban.Tables.asmdef`**——每次必现，需从 git 恢复；**长期对策未做**（asmdef 移出 `outputCodeDir` 或 gen.bat 尾步自动恢复）。
- `Assets/Scenes/Boot.unity` 是空场景——真正启动场景是 `Test.unity`（含 `GameEntry`），Play 验收用后者。
- 样式编辑器实施须**冻结 `Assets/UI/**` 改动窗口**（与并行改动错开批次）。

## 10. 不做清单（判据留档，勿重复讨论）

- **不换 UI 框架、不自研合批**：UGUI 自动合批已够；合批与 rebuild 隔离是反向目标。触发自研判据：图集+分层落地后 drawcall 仍超预算 → Frame Debugger 定位断批 → "多图集按页切换" → 自研 batcher 是最后手段（重度 UI + 确认 UGUI 为瓶颈 + 有渲染专责）。
- **不优化 `UIService.Tick` 字典遍历**：600 次/秒纳秒级；实测占 >5% 才改（Active 界面单独列表 ~10 行）。触发条件 = 界面数上千。
- **不用 LayoutGroup 排控件模板与列表**（与池化/裁剪对冲、开销响应式、构建器已确定性排布）；**该用判据**：内容不定长自适应 + 变化低频 + 数量小（背包/商城/聊天命中可用）。
- **不做通用 UI 数据绑定框架**（跨语言天花板、双向 = 循环地狱、按控件混用才是正解、所有权互斥断言更可观测、收益已被 L1/L2/命令式拿走）。升级判据：同一 Model 被 5+ 界面不同格式展示且频繁变，或纯 C# 界面内需要双向（仍限路径 B）。
- **不做 UI 分帧加载器**：加载已异步、Instantiate 不可切帧、半成品态 bug 面大；池化 + 索引缓存 + 先骨架后数据已够。判据：都试过仍超 16ms → 做"子块异步加载"。
- 不动 DOTween/动效时长（表现决策；降级走策略换短时长）；不启用调度层（`IUIScheduler` 已注册刻意不消费——一个 HudTicker 就够）。
- 扩展能力侧：历史栈/深链/路由（"任意直达+多步回退"需求出现再议）；ICU 复数（接俄/阿语）；运行时机器翻译/自动字库子集（语言 >5 或包体压爆）；补丁改结构/进热更包（编排器承接）。
- 本地化侧：不借鉴 GF 本地化模块（判据见《UI 运行时专项设计》§3）；艺术字/第二字体不进样式编辑器（撞字体资产唯一 + 中文 SDF 无 fallback）。
