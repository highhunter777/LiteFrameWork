# 施工进度

本目录记录施工进度与批次执行状态。全项目先后、并行关系和关口见 [总体施工路径](../待办总览.md)，本目录保存其引用的实施证据，与设计文档严格分离：

- 设计、契约、验收标准只写入 `Docs/design/`，本目录不裁决任何设计问题。
- 每条进度记录必须可验证：代码路径、测试命令、产物位置；不得以文档描述代替实现证据。
- 批次状态只用：`未开始 / 进行中 / 已完成（附验证证据）/ 阻塞（附原因）`。

## 索引

| 文件 | 范围 |
|---|---|
| [客户端C0.md](客户端C0.md) | 《商业级通用客户端框架总设计》C0：可复制构建与 Player 启动 |
| [同步契约P0.md](同步契约P0.md) | 已交付 Sync-P0：快照分层（公共/比赛/私有）、输入面扩展、和解口径；纳入总体 G0 基线，不等于服务端 R0 完成 |
| [UI-U0.md](UI-U0.md) | 《UI框架总设计》U0 正确性止血：Lua 实例/self、复用合流、Covered/Paused 全关、列表窗口复用、失败回滚、env 重建零旧引用；PlayMode/租约留 U1 |
| [服务端R0.md](服务端R0.md) | 《商业级通用服务端框架总设计》R0 正确性止血：精确节拍、定容输入环、数值边界、可信 ACK、稳定 configHash |
| [服务端R1.md](服务端R1.md) | 《商业级通用服务端框架总设计》R1：RoomRuntime 纯化（Runtime/Application 分层）、RoomCommand/RoomOutput、Match 状态机、Session/席位分离、重连闭环（Restoring 门闩 + 客户端会话状态机）、R11 纯化纪律扫描 |
| [客户端C1.md](客户端C1.md) | 《商业级通用客户端框架总设计》C1-①～⑩：ClientHost/AppLifetime/统一取消链、Scope 原语、IContentService/AssetLease/共享加载/代次、ConfigService 快照化、激活事务与 Patch 流程；Host 下载/验签与深度候选验证留热更批 |
| [UI-U1.md](UI-U1.md) | 《UI框架总设计》U1 操作与所有权：操作合流/取消/类型化结果（UI-03）、租约/缓存预算/销毁/Shutdown（UI-05/06）、统一排序/输入锁职责分离/转场取消复位（UI-09）、真资源包 Player 页面打开锚点；模态栈/导航队列/策略列留 U2 |
| [客户端C2.md](客户端C2.md) | 《商业级通用客户端框架总设计》C2 会话与表现：BattleClient/BattleContext、Match/Battle 流程与 Account/Match Scope、断线自动重连与恢复、地图单源；LiteSim.View 的 SimView（镜像/远端插值/本地和解衰减/事件静默门）、EntityViewMap 池、相机档位、PlayerController 输入采集与三个门；附带通用表现壳所有权（租约缓存/音频释放面/DOTween Manual 轨接 UIClock）。HUD/角色动画与 Login/Lobby/Result 留后续批 |
| [客户端表现基础.md](客户端表现基础.md) | 《框架先行》§7 第 4 项余部：Scene/Entity/Audio/VFX 生命周期收敛（迟到加载代次检查、实体作用域与关闭面、分域时钟、池释放面、VFX 关闭面/真取消/迟到不复活）+ 《动画模块专项设计》基础契约与 UI 接缝 + **真角色模型接入与动画后端首段**（CombatGirlsCharacterPack：运行时视图 prefab/收集组/缺包灰盒降级；AnimatorAnimationBackend + CharacterLocomotionDriver 移动三态）。G2 垂直切片余部（上半身混合/真对局动作对齐/8 人基线）留后续批 |
| [服务端多房间.md](服务端多房间.md) | 《商业级通用服务端框架总设计》§6/§8.2/§116/§429/§520/§600：单进程多房间的**路由与创建**——roomId → 独立 `RoomInstance`、按会话路由、动态建房受 `max_rooms` 约束、配置驱动房间模板。含两处实测缺陷（会话表容量按单房间算导致第二房间连接被拒；预置房间白占容量）。**排空（优雅关闭第 2–3 步，13 例）、跨房间过载隔离（4 例）与真实传输多房间隔离（5 例）已交付（2026-09-26）**。未交付：固定 Worker Pool + 有界 Mailbox（设计 R2）、房间销毁后清理（R2）、排空第 4–5 步（依赖 M0-c 与 Worker 池） |
| [Meta服务宿主.md](Meta服务宿主.md) | 《Meta 服务专项设计》§4.1/§4.2/§10/§11：宿主骨架（Generic Host + Options 范围校验 ValidateOnStart + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站上限）、零 NuGet 接入、R11 边界登记。含三处静默失效缺陷的实测与修正。**M0-d 票据接缝已交付（2026-09-26）**：`IJoinTicketValidator` + HMAC 验证器 + 非法票据矩阵（过期/篡改/重放/密钥轮换/受众/房间/哈希，LiteNet.Tests 9→51、L3 57 通过），接入 `ServerHost.HandleJoin` 真实准入路径。持久化样例（M0-c）仍未交付 |
| [热更内容校验.md](热更内容校验.md) | 《热更与内容发布专项设计》热更批(一)~(九) + 审查修复批：发布身份/签名校验/激活事务、候选校验内核（文件/空间端口、失败分类、逐文件摘要复算、下载计划多源轮转）、PatchCoordinator 编排与健康聚合、主链接线、Lua 受控沙箱、反回退基线、真实下载/受信公钥库/健康探针族/Bridge 能力、候选事务先于下载落盘+临时文件回收、IL2CPP 密码学验证（Windows x64）。**端到端装配已接通（2026-09-25）**——`GameModules` 装配 `TrustedKeyStore.AsResolver()` + 候选配置/Lua 探针；仍待：**信任锚点 provisioning**（零内置锚点→候选一律被拒，fail-closed）、移动端余量/Android 后端、断点续传与写盘中断矩阵（G4 真机） |
| [UI-U2.md](UI-U2.md) | 《UI框架总设计》U2 产品闭环：`UINavigationController`（单写者串行/队列上限/等待超时/排队期取消——含"等待者 finally 抢先注销 Abandon 登记"真实缺陷修复）+ **`ReplaceAsync` 显式替换当前记录（U2-⑨，2026-09-26）**、UIService 模态栈（最顶模态 Back/射线遮蔽）、ProcedureMain/Battle 真实消费者接线、UiFx 中断复位；LText 本地化（服务层 + 控件/Lua 面）、DialogService（合并/互斥组/优先级）、FeedbackService 统一反馈面（System 层 form，视觉单一来源）、per-form 缓存策略列（U2-⑧）、**包③覆盖度余部 PlayMode 10 例（U2-⑨：导航真资源段/转场输入锁/列表真滚动/DevReload 环境重建/循环计数/诊断关联）——含 UIService 回滚租约泄漏与 VirtualList.Offset 符号两处真缺陷修复**。字体/SafeArea、打字机、焦点留后续批 |
| [框架先行.md](框架先行.md) | 《框架先行建设与业务接入专项设计》§4 五包 / §5 六接缝 / §6 五样例 / §8 十条准入项逐项状态与证据。**当前判定：准入未达成**。**样例④ 联机宿主与准入项「联机宿主闭环」已完成（2026-09-26，`TwoRoomFullChainTests` 两房并行四客户端跑完入房到离场）**；**样例② UI 生命周期已完成（2026-09-26，PlayMode 22 例全链——含本批 U2-⑨ 的导航/列表/环境重建段）**。剩余缺口收窄为：**一条未交付前置件**（§5-5 存储与幂等，依赖 Meta M0-c——阻塞准入项「持久化契约有效」与样例⑤）、**两块覆盖度**（样例③ 场景与表现的 Scene/VFX/音效段与故障矩阵；Player/真机循环内存证据）、**一项运维动作**（包② 信任锚点 provisioning——零内置锚点候选一律被拒，非代码缺口）。含 L2 新鲜度守卫假阳修正与 UIService 回滚租约泄漏/VirtualList.Offset 符号两处真缺陷修复 |
