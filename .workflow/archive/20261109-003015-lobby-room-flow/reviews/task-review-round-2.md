# 任务复评：B1/B2/B3 修订闭合复核

## 评审输入

- 已读：`.workflow/active/lobby-room-flow/reviews/task-review-round-1.md`、`tasks.md`、`plan.md`（含末尾“任务评审后执行约束”），共 3 次读取，未超 12 次上限。
- 范围：仅静态复核 B1/B2/B3 是否在任务/方案文本中闭合；未重做源码或全仓调查，未改文件，未操作 Unity，未运行测试/门禁。
- 身份：独立子代理复评；此前无任务上下文，不是首轮作者；不声称实现已满足设计。
- 依据：用户给定的 B1/B2/B3 修订口径 + `plan.md` 末尾约束。`tasks.md` 当前 0/6、全部待办，符合施工前评审状态。

## 覆盖与追溯

| 追溯项 | 对应任务 | 复核结论 |
|---|---|---|
| B1 行为测试先行 | T1 写全量行为断言、只转调适配器；T6 仅调用迁移；T4 不首次补断言 | 主体闭合，证据面有 minor |
| B2 会话分离 | T2/T3 文件集无测试；T6 独立测试迁移；T4 依赖 T3+T6 | 闭合，T2 会话 ID 证据有 minor |
| B3 精确命令与回归 | T2 入场 green + 全量 known-red；T3 全量回归；T4 入口/截图；T5 旧引用/施工卷 | 未闭合：T3 全回归 green 门槛缺失 |
| AC1～AC9 | tasks.md 表已映射：AC1=T2/T5；AC2=T1/T2/T4；AC3=T1/T2/T3/T4/T6；AC4=T1/T3/T4；AC5=T1/T3/T4；AC6=T1/T3/T4/T6；AC7=T2/T3；AC8=T1/T3/T4/T6；AC9=T4 | 覆盖完整；acceptance.md 对应任务列按用户给定视为已回填，未重复核查 |

## 检查项

- [x] T1 使用现有真实 API/阶段和只转调适配器；两个排队窗口（Main→Match、JoinAck→Battle）、卸载阻断、登录优先级均点名。
- [x] T1 禁止把编译错误或测试替身自测当 red；关键测试不延期到 T4，T4 明确“不承担首次补写关键行为断言”。
- [x] T6 独立测试作者仅改调用适配，场景/断言冻结并逐条对照；T2/T3 生产会话禁止写测试。
- [x] T2/T3 文件集已移除测试文件；T6 独占测试写集；T4 依赖 T3 与 T6。
- [x] T6 两轮串行交接；T2/T3 done 分别要求当轮映射/验证证据；T6 最终 done 在 T3 映射后。
- [x] T2 跑全量 RoomFlow 但只把 T3 未实现的生命周期 red 留给 T3，不要求未来测试先 green，未形成死锁。
- [ ] T3 的 done 门槛只写“T1/T2 完整 RoomFlow 全部 green”；MetaEntry/Core/PageFlow/Net 仅出现在验证命令中，未写成“全部 green、任一失败返工”。
- [x] T4 有脚本入口 `AgentScripts/LobbyRoomFlow/RunRoomFlowUi.cs` 和截图路径 `Artifacts/LobbyRoomFlow`。
- [x] T5 已补 02/04/14/图源/HTML 旧引用与精确施工卷 `Docs/施工进度/客户端重构线.md`。
- [x] 未强制某种新 API 骨架；允许先补最小可编译契约再跑 red。

## 发现的问题与处理

| ID | 严重度 | 问题/不闭合路径 | 处理 |
|---|---|---|---|
| B3 | major（阻塞） | T3 验收标准只要求“完整 RoomFlow 全部 green”；验证列虽列 MetaEntry/Core/PageFlow/Net 命令，但未规定这些命令失败即不得 done。执行上 T3 可在 Core/Meta/PageFlow/Net 回归失败时仅凭 RoomFlow green 标记完成，正是 B3 要消除的回归缺口。 | T3 验收补“所列全部命令 green；任一失败返工；不得 skip/取消/过滤空集换绿”；逐套记录命令输出；把 T2 的 known-red 清单列为 T3 入场核对项。 |
| B1 | minor（已闭合） | T1 验收“至少晚到取消真实断言 red”是下限；两窗口/卸载/登录的“现有已满足 green”未要求逐条证据，存在弱断言或假 green 风险。 | T1 证据按用例记录 red 或“现有实现已满足”的依据；冻结断言清单落到固定路径/哈希；主代理 `verify --red` 逐类核对。 |
| B2 | minor（已闭合） | T2 验收未重复“记录测试/生产会话 ID 与不重叠写集”；plan 全局约束已覆盖，但任务证据字段为空。 | T2 证据补会话 ID/写集；T2 描述“维护源链接”限定生产侧，`LiteFramework.Core.Tests.csproj` 源链接归 T6。 |
| m3 | minor | T6 两轮交接仍是描述性文字，未给 round-1/round-2 证据落点；依赖列只有 T1。 | 增加两轮证据文件/命令输出；保留 T1 依赖以避免 round-1 死锁，最终 done 由 T3 映射后的文本门控。 |
| m4 | minor | T4 验证写“`--help` 确认签名后执行”，不是可直接复现的完整命令。 | 记录解析后的 `run_script`/`capture_game_view` 完整参数与截图文件名。 |
| m5 | minor | T5 写集含生成 HTML，描述又说只经生成器重生成。 | 将 HTML 标为生成物，不手改；先改图源再跑生成器。 |

## 评审结论

- 评审方式: 独立子代理
- 评审轮次: 复评（非首轮作者）
- 结论: FAIL
- 阻塞问题: 1
- 阻塞项: B3
- B1: 已闭合（仅 minor 证据面）
- B2: 已闭合（仅 minor 证据/写集边界）
- 说明: 本次仅静态复核任务/方案文本；未运行测试、Unity 或门禁，未改文件，未宣称实现满足设计。T1 行为测试先行与 T2/T3/T6 会话分离的主体已成立；B3 的 T3 全回归 green 门槛补齐后可仅复评 B3。

- 代理类型: dsh headless
- 会话标识: session-39a67d41-7656-4b48-a833-ffb0557101df
