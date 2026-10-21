# 归档记录（2026-10-22）

> 本目录存放 2026-10-22 被取代/退役的文档；只用于追溯，不参与当前裁决。

| 文件 | 原位置 / 退役原因 | 现行权威 |
| --- | --- | --- |
| [日志标签收敛专项设计.md](日志标签收敛专项设计.md) | `Docs/设计文档/架构/`——专项范围已全部实现并封闭（33 个散值收敛为 32 项 `LogTag` 枚举、门面 string 重载删除、191 处调用点迁移、显示名单源与穷尽守卫）；原稿 §5 范围外项各有归属，设计不再承担现行裁决 | 代码：`Assets/LiteFramework/Scripts/Core/Logging/LogTag.cs`（词表/显示名单源）、`Log.cs`（门面与守卫契约）；批次证据见[施工进度/GM与调试线.md#ch2-日志标签收敛](../../施工进度/GM与调试线.md#ch2-日志标签收敛)；服务端 `HostLog` area 仍归 R4 OTel |
| [对象池专项设计.md](对象池专项设计.md) | `Docs/设计文档/架构/`——批1–4＋续全部完成（内核淘汰/收缩/验证、集合池、收编与引用池升级），无剩余批次 | 代码：`Assets/LiteFramework/Scripts/Core/Pool`（内核/集合池/引用池）、`Assets/LiteFramework/Scripts/Unity/Pool`（GameObject 池）；证据见[施工进度/框架地基线.md#ch3-对象池高级化](../../施工进度/框架地基线.md#ch3-对象池高级化) |
| [事件中心专项设计.md](事件中心专项设计.md) | `Docs/设计文档/架构/`——批1–3 全部完成（优先级/有界队列/延迟派发、统计诊断、发布拦截链） | 代码：`Assets/LiteFramework/Scripts/Core/Event`、`Core/Util/SubscriptionBag`；证据见[施工进度/框架地基线.md#ch4-事件中心高级化](../../施工进度/框架地基线.md#ch4-事件中心高级化) |
| [状态机专项设计.md](状态机专项设计.md) | `Docs/设计文档/架构/`——批1–5 全部完成（迁移表/守卫/看门狗、层级抢占对齐、快照恢复、统计诊断），无剩余批次 | 代码：`Assets/LiteFramework/Scripts/Core/Fsm`；证据见[施工进度/框架地基线.md#ch6-状态机高级化](../../施工进度/框架地基线.md#ch6-状态机高级化) |
| [服务端宿主结构拆分专项设计.md](服务端宿主结构拆分专项设计.md) | `Docs/设计文档/架构/`——6A/6B/6C/7A 全部完成（6C 结论为不切），切分边界即当前实现 | 代码：`Server/RoomServer/RoomRegistry.cs`、`Server/RoomServer/InboundHandlers.cs`、`Server/MetaServer/Host/MetaHostOptions.cs`、`MetaServiceRegistration.cs`；证据见[施工进度/服务端装配与结构线.md#ch2-结构批6A房间表收口](../../施工进度/服务端装配与结构线.md#ch2-结构批6A房间表收口) 至 [#ch5-结构批7A宿主装配分段](../../施工进度/服务端装配与结构线.md#ch5-结构批7A宿主装配分段) |
| [安全信封专项设计.md](安全信封专项设计.md) | `Docs/设计文档/网络/`——PSK 握手/HKDF/AES-256-GCM 传输装饰器与双端 fail-closed 装配已全部交付 | 代码：`Assets/LiteNet/Transport/Security`；证据见[施工进度/服务端R线.md#ch4-安全信封](../../施工进度/服务端R线.md#ch4-安全信封) |
