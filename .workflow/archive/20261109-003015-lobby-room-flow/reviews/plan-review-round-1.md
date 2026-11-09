# 首轮独立方案评审：大厅进房离房链路样板简化

## 评审输入

- 对象：本运行首轮plan.md及17个关键源码的baseline。
- 子代理：Helmholtz，01a1baff-0636-7171-a963-86168eb508e7；只读，未写代码/测试或操作Unity。
- HEAD：d4f284c79adb3852b3eb68f7303d439cb4def969；评审核对17文件哈希与baseline一致。

## 方案检查

推荐B方向成立，范围、候选方案、回滚可行；接口边界与异步移交/清理/恢复设计未闭合，不能推进施工。

## 验收标准可判定性

AC1～AC9均可观察和判定；PASS只代表标准可判定，不表示实现或方案已满足。

## 发现的问题与处理

| ID | 严重度 | 独立评审发现 | 要求修订 |
|---|---|---|---|
| P-1 | major | ProcedureBoundaryTests明确禁止Procedure直接使用MetaEntryService，初版拟持具体服务违反守卫 | Meta暴露实际需要的窄票据/账号端口；不移出令牌、不削弱边界守卫 |
| P-2 | major | Request只排队，实际接管前仍在Main；初版在Request返回就放弃操作所有权 | 覆盖Queued窗口，离房/重入使其失效，Match在建连接前检查；保留占用直到排队操作被实际接管或丢弃 |
| P-3 | major | OnLeave不等待异步清理，场景Unload为Forget；无法据此判离房完成 | 本次操作可等待的清理完成信号、等待必要卸载、清理期间阻断入场、登录/宿主退出抑制旧页面恢复 |
| P-4 | major | 现有ProcedureError/BootstrapErrorPresenter只有重启提示，没有返回大厅/重试 | 明确房间失败恢复动作，复用FeedbackService.ShowErrorAsync；启动致命错误保留原处理 |
| P-5 | minor | 共享67commands/19red_commands且另有活动运行；本批隔离未解决 | 明列测试前置项，不修改其他运行定义，不声称已隔离完成 |

主代理按此返回修订plan，所有问题需独立复评后再闭合。

## 评审结论

- 评审方式: 独立子代理
- 结论: FAIL
- 验收标准: PASS
- 阻塞问题: 4
- 评审人: Helmholtz（01a1baff-0636-7171-a963-86168eb508e7）
- 评审日期: 2026-11-08（Asia/Hong_Kong）
