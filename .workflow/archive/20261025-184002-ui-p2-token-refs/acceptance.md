# 验收标准确认：UI 系统 P2-3 B2：令牌引用组件与应用

> 运行 ui-p2-token-refs · 创建于 2026-10-25T14:18:32+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | 令牌解析核心住 LiteClient.UI（UiStyleResolver，命名从总设计 §6.1），编辑器 UiTokenCatalog 为委托；颜色命中阈值单源驻 resolver；既有调用点与全部既有 UI 夹具回归全绿（公开面签名不变） | 既有 UI EditMode 全套回归＋新 UiStyleResolverEditModeTests；代码审读 | T1/T2 |
| AC2 | UiStyleRef 组件上任（token ID 声明、空合法、单组件约束、只声明不驱动）；≥1 个产品页/控件模板真实挂载（经工具/unity-pipeline，非手改 YAML） | UiStyleRefEditModeTests＋试点 prefab diff 审读 | T3/T7 |
| AC3 | refs 应用并入 ui.style.colors-refresh（D2）：按声明写值、幂等（重跑零变更）、报告含 应用/跳过/未知 token 计数；已声明节点不被裸值段改写；回退口径＝幂等重跑＋工具落盘（总设计 §6.2「可 Undo」措辞同批澄清） | 工具夹具（注入式扫描目录）＋真实目录冒烟（不落盘） | T4 |
| AC4 | bootstrap 装载 UiTheme.asset 一次，经 UIService ctor 形参＋RunOpenAsync 赋值注入 UIForm.ThemeProvider；页面装配后 ref 节点颜色＝令牌现值；装载缺失/异常降级＝保持烘焙值＋Warning 一次不阻断 | PlayMode 真 prefab 用例（同 UiBehaviorPlayModeTests 通道）＋代码审读 | T5 |
| AC5 | ui.style.ref 规则族进 UiValidator.Rules（未知 token＝Error、声明值漂移＝Warning〔阈值单源〕、无 Graphic＝Error、widget 状态 token 三态〔未知 Error/漂移 Warning/空合法〕、同节点 UiStyleRef＋IThemeBound 双写者＝Error）；真实面跑一次 0 新增 Error | 规则夹具＋UiValidator.RunCli 真实面 | T6 |
| AC6 | 状态色令牌化（D3 用户裁决）落地：三 widget 伴生 token 字段＋IThemeBound 装配注入（refs 遍历跳过 IThemeBound 节点）＋fallback 降级；映射表落设计文档（新增 8 枚 color.state.*〔清单含值与来源 file:line，量化零可见变化，近金/近灰不合并〕、复用 color.text.bright；取值权威＝prefab 序列化现值）；迁移段并入 colors-refresh（幂等/不命中不填/报告）；uGUI Selectable（实测 33 处/17 prefab）与 C# 默认色存量登记审计留 B4 | 夹具/PlayMode＋试点 prefab diff＋doc-lint＋审计 diff | T6/T7 | 
| AC7 | 工具/索引/设计文档回写齐（§6.1 resolver 命名、§6.2 ThemeProvider 缝与「可 Undo」澄清、§8.1 refs-aware 口径与 B2 消费者）；config.json 增门禁；toolchain.py check 0 error；wf verify 全绿 | toolchain.py check＋doc-lint＋wf verify | T9 |

## 提审记录

- 提审时间：2026-10-25
- 提审方式：结构化提问（AskUserQuestion：AC 总确认＋D1/D2/D3 三裁决点）
- 用户原话或选择（首轮，2026-10-25 AskUserQuestion）：AC1–AC7「确认通过（推荐）」；D1＝「方案 B：装配期应用（推荐）」；D2＝「并入 colors-refresh（推荐）」；D3＝「本批令牌化」（未采纳豁免推荐）。
- D3 裁决引起方案实质扩范围 → 按流程修订方案并重走子代理评审（两轮 FAIL 后 PASS，见 plan-review.md「D3 增量评审」）；修订后 AC5/AC6 有实质变化（新增 widget 判据与 8 枚令牌清单），需用户对**增量**再确认。

## 用户确认

- 确认结论: 通过
- 确认人: highhunter777（对话用户，AskUserQuestion 两轮：首轮 AC 总确认＋D1/D2/D3；次轮 D3 增量确认「确认增量（推荐）」）
- 确认时间: 2026-10-25
- 变更要求：无（D3 增量修订已按流程重评并二次确认）

## 变更记录

- 2026-10-25：D3 用户裁决由"推荐豁免"改为"本批令牌化"→ 方案实质扩范围（映射表/widget 字段/IThemeBound/迁移段/判据扩展）→ 重新子代理评审两轮（FAIL：8 枚清单补齐、33/17 计数修正、双写者裁决）→ 第三轮 PASS → AC5/AC6 增量经用户二次确认通过。
