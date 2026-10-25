# 测试计划：UI 系统 P2-3 B2：令牌引用组件与应用

> 运行 ui-p2-token-refs · 创建于 2026-10-25T14:18:32+08:00

## 测试策略

- 测试层级：EditMode 夹具为主（Unity 真实资产/AssetDatabase，契约级）；PlayMode 行为用例 1 条（AC4 运行期消费链，真 prefab 装配注入与降级）；既有全套 UI 夹具作回归基线（AC1 委托化零语义漂移）。
- 关键风险与覆盖重点：①解析核心下沉后编辑器读面漂移（回归钉）；②refs 应用幂等/未知令牌不猜色；③widget 注入取值序与 fallback 不漂；④规则五判据与真实面基线（0 新增 Error）；⑤PlayMode 降级路径（ThemeProvider 缺失＝烘焙值＋不阻断）。
- 骨架先行：类型壳（UiStyleResolver/UiStyleRef/IThemeBound/UiStyleRefApplier/StyleRefRule + 三 widget 伴生字段与接口占位）编译通过但行为空转——正向断言失败、负向断言（跳过/不填/降级）天然通过作基线钉。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| UiStyleResolverEditModeTests（10 例） | 契约 | T1/AC1 | FromAsset 真实 SO 计数 20/5、TryColor/TryTypography 已知未知、Apply 四结果枚举、与 UiTokenCatalog 同源一致、重复/空 ID 口径、HitEpsilon 单源 | 全绿（红：计数 0/Apply NoToken 等） |
| UiStyleRefEditModeTests（6 例） | 契约 | T4/AC3、AC6-③ | 临时目录 prefab：ApplyRefs 写值/幂等/未知计数；ApplyColorsWith 裸值段跳过已声明；MigrateWidgetTokens 最近匹配填入/不命中不填 | 全绿（红：stub 零应用/迁移不填） |
| UiThemeStateTokensEditModeTests（3 例） | 契约 | T2/AC6-① | 令牌源 JSON 8 枚状态色值精确；SO 同步；命名组归属 | 全绿（红：令牌缺失） |
| UiWidgetThemeEditModeTests（7 例） | 契约 | T6/AC6-② | 三 widget 实现 IThemeBound；注入后各态取令牌值（StateButton 常态/选中、TabGroup 选中常态、StarRating On/Off）；未注入/未知令牌 fallback 不漂 | 全绿（红：接口未实现/注入空转） |
| UiStyleRefRuleEditModeTests（7 例） | 契约 | T6/AC5 | 五判据：未知 Error/一致零问题/漂移 Warning/无 Graphic Error/双写者 Error/widget 三态/规则注册进默认集 | 全绿（红：stub 规则零报告） |
| PlayMode 行为用例（1–2 例） | 端到端 | T5/AC4 | 真 prefab（试点页）打开后 ref 节点颜色＝令牌现值；ThemeProvider 置 null＝保持烘焙值、开页不抛 | 全绿（施工后先行落测） |

## 预期失败证据

- 命令：`wf verify --red`（red_commands＝上表五套 EditMode 夹具；见 `.workflow/config.json`）
- 观察到的失败（evidence/red.json + 定向复跑，2026-10-25 15:2x；全程非编译/非环境错误）：
  - UiStyleResolverEditModeTests：10 例 **8 失败**（计数 0/Apply NoToken/同源不一致/重复 ID 口径）；2 例负向钉（NoToken 跳过/HitEpsilon 常量）骨架期已绿
  - UiStyleRefEditModeTests：6 例 **4 失败**（refs 零应用/幂等计数/迁移不填/裸值段未跳过）；2 例负向钉绿
  - UiThemeStateTokensEditModeTests：3 例 **2 失败**（令牌缺失/SO 缺失）；1 例命名钉绿
  - UiWidgetThemeEditModeTests：7 例 **6 失败**（接口未实现/注入空转）；1 例 fallback 钉绿
  - UiStyleRefRuleEditModeTests：7 例 **6 失败**（stub 规则零报告/未注册）；1 例一致零问题钉绿
  - 首轮红证据作废原因如实记录：①测试代码编译错（Assert.IsNullOrEmpty 不存在）→ 修复后编译过；②系统代理开启拦 127.0.0.1（Clash 7890）→ 门禁脚本 unity 调用加 `--proxy-disable`（批外修复，登记 build-log）
- 为什么这是正确的失败：骨架类型壳行为空转，断言针对的是 B2 验收语义（单源计数/写值/判据/迁移），实现落地后转绿；负向断言（跳过/不填/fallback）在骨架期已通过＝行为不漂基线钉。

## 运行方式

- 全量测试：`wf verify`（L1 + 既有 UI 全套 EditMode + PlayMode + 本批五套 + doc/toolchain）
- 单套：`python scripts/gate/unity-tests.py --mode editor --filter <SuiteName> --timeout 600`
- 红测：`wf verify --red`