# 测试计划：大厅进房离房链路样板

## 测试策略

用户委托主代理分配任务并验收；所有测试/实现由真实dsh会话撰写。测试作者与生产作者使用不同会话；主代理执行门禁、核对实际diff及输入证据。dsh认证已通过临时启动patch修复；首轮真实独立任务评审提出B1～B3，任务已修订并进入独立复评，未在任务评审通过前写测试。

## 用例清单

| 验收 | 测试与断言 |
|---|---|
| AC1 | Meta纯取票不触发流程；ProcedureBoundary保留具体服务禁用；生产装配同实例 |
| AC2 | 快速/选房/普通建房各一次请求/迁移，票据房号/地图/人数原样；GM显式模板参数不丢 |
| AC3 | 预取消、在途取消、晚到响应、A取消后B启动且A晚到不清B；Main→Match queued失效零建连接；Match→Battle queued被Main覆盖回收一次 |
| AC4 | 未配置/未登录/票据拒绝/地址非法零连接；握手拒绝/超时/准备失败清理→单按钮确认→大厅→新票重进 |
| AC5 | Loading Back、等待开局离房、设置页退出，重复离房幂等；账号重入不被旧恢复覆盖 |
| AC6 | 资源/订阅/输入/RoomBridge/Back/遮罩清理事件；挂起卸载时不放行新入场；完成后可重进 |
| AC7 | AccountReentry、GM sandbox、本地直入、正常结算；凭据不进日志 |
| AC8 | 实际断言red→green；Core/Net定向、Unity定向/编译与L1/Doc/依赖/注释/目录/登记检查 |
| AC9 | 真实页面先Raycast命中，再pointer/键盘输入；断言页面与清理副作用，直调API不算操作证据 |

## 预期失败证据

尚未产生；初次dsh模型认证故障已修复，该故障不是测试red。T1在当前API上写可编译的取消晚到行为测试，真实断言失败后由主代理运行workflow verify --red留证；缺类型/编译错误/环境故障不计red。

## 运行方式

- 工作流root：C:/Users/hunter/.codex/tmp/workflows/lobby-room-flow-01a1baf2；run lobby-room-flow。该root通过junction复用同一state，独立配置不改其他运行。
- T1新增fixture名包含RoomFlow，定向入口：python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600。
- Core/Net基线命令见evidence/preflight.json；之前16/16通过，仅表示本批前基线。
- Unity项目E:/unityProject/Test当前Editor端口7801；实际调用前重新确认实例并遵守unity-pipeline skill。
- dsh施工不得修改.workflow、门禁脚本/配置，不得删除旧用例/削弱断言或重置工作区。


### 首轮任务评审后补强

T1必须包含两个排队窗口、取消晚到/旧A新B、卸载完成阻断、登录优先级的完整行为断言，全部在真实现有服务/阶段上观察；测试适配器只转调，不实现待验算法。T6独立测试作者在T2/T3接口出现后分两轮迁移调用并逐条比对场景与断言；生产作者无测试写权限。T2必须入场green、完整RoomFlow输出明确列出T3尚未实现的已知red；T3必须完整RoomFlow及T1/T2回归全部green，不能遗漏入场回归。

- T1冻结场景/断言清单路径：evidence/t1-assertion-manifest.json（主代理按测试源码hash和实际red/green输出写入）。T6两轮证据：evidence/t6-round-1.json、evidence/t6-round-2.json，记录不同作者会话/写集/断言对照/命令结果。T4在脚本完成后固定run_script及capture完整参数与Artifacts截图名，不以--help输出当操作证据。

## T1真实失败与断言冻结

独立dsh作者session-1ae08d74-641c-4dab-beae-c439e3030248已交付4个测试源码，主代理review修正刺激后实跑workflow verify --red，9/9为真实NUnit断言失败，编译failed:false/errors:[]。证据evidence/red.json、t1-unity-test-status.json.txt、t1-assertion-manifest.json（源码hash与9用例）。T6在生产接线变化后仅迁移调用适配并保持断言；恢复契约出现前另补其失败测试，不伪称当前9用例覆盖所有错误恢复路径。
