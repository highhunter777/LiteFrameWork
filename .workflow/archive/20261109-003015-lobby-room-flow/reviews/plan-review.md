# 方案复评：大厅进房离房链路样板简化

## 评审输入

- 方案：本运行plan.md；首轮记录：reviews/plan-review-round-1.md。
- 独立复评子代理：Leibniz，01a1bb08-9fd3-7642-9903-fdc0673ecba9；日期2026-11-08（Asia/Hong_Kong）。
- 定点核对AGENTS、ProcedureBoundaryTests、StageMachineCore.Request、ProcedureStageBase、ProcedureMatch/Battle、PageFlow/AccountReentry、FeedbackService与Meta接口。
- 只读复评，未执行测试、写生产/测试文件或操作Unity。本记录由主代理根据子代理实际返回整理；不构成人工审批。

## 方案检查

推荐B可以实施；首轮4项major设计缺口均已闭合。

| 首轮问题 | 独立复评结论 | 依据 |
|---|---|---|
| P-1 | 关闭 | ProcedureFlow消费IRoomTicketSource而非Meta具体服务；保留类型禁用边界，无新增程序集依赖 |
| P-2 | 关闭 | Main→Match排队窗口继续占用；句柄失效由Match零建连接消费；DisplayToken脱离与独立取消明确 |
| P-3 | 关闭 | 共享内部操作句柄/完成信号，不新增框架FSM；必要卸载await，清理及恢复前阻断入场；导航代次保护登录/退出 |
| P-4 | 关闭 | 本批补清理→错误确认→大厅→新票重进；现有FeedbackService有可等待错误确认能力，启动Fatal保留 |
| P-5 | 前置项写实 | 命令隔离尚未实施，tests-first前明确配置与证据归属；CLI支持独立root，不宣称已解决 |

Match→Battle排队窗口同样闭合：会话由句柄托管到实际接管；Main覆盖时回收并完成清理，不能在Request(Battle)返回就宣布移交。

## 验收标准可判定性

PASS表示标准可判定，不表示实现已通过或用户已确认。

| 标准 | 结论 | 判定依据 |
|---|---|---|
| AC1 | PASS | 调用链、Meta无流程副作用、边界守卫与程序集引用 |
| AC2 | PASS | 三类入口请求/迁移/会话次数、票据事实、等待页与JoinAck |
| AC3 | PASS | 可控响应和Advance顺序，两个排队窗口、晚到与新旧请求隔离 |
| AC4 | PASS | 失败注入、清理后确认回大厅、新取票，启动Fatal另验 |
| AC5 | PASS | Loading Back、等待页离房、设置离开、幂等与登录落点 |
| AC6 | PASS | 所有权/释放事件、挂起卸载期间阻断、订阅/输入/Back/旧回调隔离 |
| AC7 | PASS | 账号重入、GM sandbox、本地直入、结算、敏感日志及范围 |
| AC8 | PASS | 真实red→green、定向/门禁/编译证据，既有失败与未执行项单列 |
| AC9 | PASS | 射线命中、真实指针/键盘、页面和清理副作用证据 |

## 发现的问题与处理

无新增major或尚存阻塞项。子代理返回两项minor，主代理按允许直接收口的建议写入方案：

1. 错误确认固定ShowErrorAsync(retryable:false)单按钮，确认后回大厅手动重进，不引入旧票自动重试。
2. Match→Battle竞态用例明确JoinAck→排队Battle→离房覆盖→Advance Main，断言未接管资源恰好释放、Battle未接管及清理前拒绝新入场。

P-5配置隔离仍为tests-first前置工作，当前未完成；不得借本评审声称其验收完成。验收确认仍需用户明确答复，不得据此进入任务拆解或施工。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: Leibniz（01a1bb08-9fd3-7642-9903-fdc0673ecba9）
- 评审日期: 2026-11-08（Asia/Hong_Kong）
