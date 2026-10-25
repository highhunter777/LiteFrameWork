# 代码评审（独立子代理）：UI 系统 P2-3 B2：令牌引用组件与应用

> 运行 ui-p2-token-refs · 创建于 2026-10-25T14:18:32+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 代码基线：git HEAD `981668f5`（B2 全部改动在工作区未提交）；B2 范围＝运行时 Style 三新件＋UIForm/UIService/三 widget/UiServiceModule、编辑器 applier/规则/catalog 委托、令牌 JSON+8 枚与 SO 重生成、试点与迁移 5 prefab、测试 6 套新/3 套改、门禁三件；另有并行线三处最小解阻塞（DevHudPerfSampler 删除、test_l2_entry、config --lane）
- 子代理标识：general-purpose `agent_a43470c4-9416-49bd-8e1d-22dcf38b26a2`（ZCode Agent 工具）
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 变更范围

- 对照任务与方案：T1–T9/AC1–AC7 全映射到位（子代理逐项核对：8 枚令牌值与 SO rgba 逐枚核算一致〔ABGR 打包验证〕、近金不合并成立〔12/25/26 每通道 > 阈值〕、Page_Login diff 仅组件新增+token 字段+量化色值〔guid 与 meta 匹配、色值精确等于 #E6BF40 量化——工具写值签名〕、四 widget prefab 共 10 字段仅加 token 零色变、UiTheme.asset 仅 +8 条目+SourceHash、降级 SafeCall 与 LocaleProvider 同款、命名空间限定/别名为消二义的最小正确改法）；无范围外改动（并行线文件除外）。

## 正确性与边界

- 主路径：resolver 重复/空/未知 ID 与 Apply 四结果、applier 幂等/未知令牌、widget Pick 取值序、装载→注入→装配链均核过。
- 红线：红线 6 ✓（无 public static 可变状态，实例传递）；红线 5 ✓（token 集来自生成物、路径/阈值 const 链单源——一处例外见建议 2 已修）；红线 10 ✓（消费闭环＋租约 Shutdown 归还）；红线 9 ✓（refs×widget×裸值段写者关系）；错误契约 ✓（装载失败降级 Warning 一次不阻断）。
- 并行线三处最小修复均正确（孤儿删除后零代码引用；lane→suite 与 summary 键一致；恰 2 处）。

## 通用性与扩展性

- 通用性: PASS
  - 依据：解析核心单源＋委托面签名不变＋WidgetFields/TryNearestMatch 双方共用、无字面映射
- 扩展性: PASS
  - 依据：ThemeProvider 缝即主题切换驱动点；spacing/radius 走新组件不预造；克隆体边界已登记

## 测试充分性

- 先红后绿：五套 26 例骨架期断言级真红；实现后全绿；基线钉变更如实（302→298，债降 4）。
- 覆盖面：EditMode 五套（resolver/令牌/applier/widget/规则）＋PlayMode 2 例（真页面注入与降级）＋全量 UI 回归；minor 缺口两处（applier 跳过分支已补、asset-null Warning 一次登记 B4）。
- 证据：evidence/verify.json 46/46（修复后复跑 2026-10-25T18:38）。

## 安全与性能

- 安全：纯表现面（UI 样式），无输入边界/注入/权限面；SO 为生成物禁手改（token-drift 规则守卫）。
- 性能：每页装配一次遍历（refs＋IThemeBound，量级每页几十）；resolver 为字典直查；SO 装载一次持租约；无热路径开销（开页期一次性）。

## 测试有效性

- 46/46 verify 证据覆盖变更面（L1 1843、doc-lint 0/0、五套新夹具 10/6/3/7/7、PlayMode 真 prefab 2/2、真实面 Error=0/基线钉 298 如实"只降不升"）；两处 minor 分支缺口（applier 跳过 IThemeBound、asset-null Warning 一次）——前者复评后已补用例（UiStyleRefEditModeTests 增至 7 例全绿），后者登记 B4。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| C-1 | minor | "widget 是自身 Graphic 唯一写者"注释/判据口径只对 StateButton（同节点）成立；TabGroup/StarRating 写子节点——子节点上的 ref 不被双写者判据捕获、也不被应用段跳过（实现忠实 AC5 原文，属口径外盲区） | 三处注释措辞修正（"自持 Graphic 写权（含子节点翻动）"）；盲区＋B4 扩规则（"IThemeBound 祖先子树内禁 ref"）审计登记 | 注释已修；规则扩展登记 B4 |
| C-2 | minor | UiWidgetThemeEditModeTests 硬编码 SO 路径字面量 | 改用 `UiThemePaths.DefaultAssetPath`（全限定） | 已修，夹具 7/7 |
| C-3 | minor | unity-tests.py 的 stale `filter_ok` hunk 超出本批声明范围（并行线既有工作区改动） | 审计登记行补归属注记（随其批次提交） | 已登记 |
| C-4 | minor | applier 跳过分支无直接用例；UIForm asset-null Warning 一次无直接断言 | 前者补 `ApplyRefs_跳过IThemeBound节点`（7/7 绿）；后者登记 B4 | 一修一登记 |
| C-5 | minor | `Pick` 三行取值序在三个 widget 逐字重复 | 登记 B4 收敛 resolver 侧 helper（评审后不宜再动生产代码形状） | 已登记 |
| C-6 | 建议 | ApplyColors 包装变量无意义／_themeAsset 字段可局部化／枚举 doc 措辞／OCE 单独 rethrow／空 token ref 节点退出裸值管辖口径 | 记 build-log 后续建议 | 已记录 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 通用性: PASS
  - 依据：解析核心单源＋委托面签名不变＋WidgetFields/TryNearestMatch 双方共用、无字面映射
- 扩展性: PASS
  - 依据：ThemeProvider 缝即主题切换驱动点；spacing/radius 走新组件不预造；克隆体边界已登记
- 阻塞问题: 0
- 验证有效性: 46/46 覆盖变更面（两处 minor 分支缺口一补一登记）
- 评审人: general-purpose agent_a43470c4（ZCode Agent 工具）
- 评审时间: 2026-10-25（一轮；minor 修复后按"只剩 minor→修复复测收口"处理，未再开新轮：受影响夹具复跑 7/7＋7/7，全量 verify 复跑见 evidence/verify.json）

## 复测记录

- 修复内容：C-1 注释 ×3、C-2 路径常量、C-4 前半用例；C-3/C-4 后半/C-5/C-6 登记。
- 复测命令：`unity-tests --filter UiStyleRefEditModeTests`（7/7）、`--filter UiWidgetThemeEditModeTests`（7/7）、`wf verify` 全量复跑（修复后）。
- 复测结果：见 evidence/verify.json 最新一轮。
