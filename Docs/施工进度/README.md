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
| [客户端表现基础.md](客户端表现基础.md) | 《框架先行》§7 第 4 项余部：Scene/Entity/Audio/VFX 生命周期收敛（迟到加载代次检查、实体作用域与关闭面、分域时钟、池释放面、VFX 关闭面/真取消/迟到不复活）+ 《动画模块专项设计》首个批次的纯规则半部（AnimationId/Handle 三分量身份/终态/Profile Resolver/通道仲裁/有界保留）。UI 接缝半部已交付；Animator 后端与真角色验收留后续批 |
| [Meta服务宿主.md](Meta服务宿主.md) | 《Meta 服务专项设计》§4.1/§4.2/§10/§11：宿主骨架（Generic Host + Options 范围校验 ValidateOnStart + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站上限）、零 NuGet 接入、R11 边界登记。含三处静默失效缺陷的实测与修正。持久化样例（M0-c）与票据验证接口（M0-d）未交付 |
| [热更内容校验.md](热更内容校验.md) | 《热更与内容发布专项设计》热更批(一)~(九) + 审查修复批：发布身份/签名校验/激活事务、候选校验内核（文件/空间端口、失败分类、逐文件摘要复算、下载计划多源轮转）、PatchCoordinator 编排与健康聚合、主链接线、Lua 受控沙箱、反回退基线、真实下载/受信公钥库/健康探针族/Bridge 能力、候选事务先于下载落盘+临时文件回收、IL2CPP 密码学验证（Windows x64）。仍待：端到端装配、移动端余量/Android 后端、断点续传与写盘中断矩阵（G4 真机） |
