# Meta 服务专项设计

> 状态：现行专项设计；目标服务尚未实现
> 版本：1.0
> 更新日期：2026-09-26
> 适用范围：`MetaServer`（Auth/Lobby/Profile）、Meta 与 RoomServer 的接缝、Meta 与客户端的接口契约、宿主选型、存储与结算幂等、Meta 测试矩阵
> Owner：Meta/数据；服务端架构负责宿主与边界接缝，客户端流程/UI Owner 负责消费端契约
> 依赖：`SignatureVerifier`（签名原语）、`Protocol`/`PacketCodec`（版本字段）、`LiteNet.Contracts` 版本契约、热更专项的 ReleaseCatalog/兼容策略、Mongo/Redis 部署环境
> 实施状态：第 2 节是 2026-09-26 代码核查基线；其余为目标契约。本文的工程结构、接口命名与目录建议不表示已有代码或已完成交付。

## 1. 定位与冲突裁决

Meta 是战斗服之外的**局外权威**：账号、身份、大厅、进度、库存与结算账本。它与 RoomServer 的分工是单向、不可逆的——Meta 向房间提供不可变入场事实，房间向 Meta 输出受验证的结算增量，**房间不对局中查询 Meta，Meta 不参与战斗判定**。

本文件细化《商业级通用服务端框架总设计》§6/§7/§11/§12/§13 在 Meta 侧的落地，并吸收《游戏业务系统总设计》§2 的权威边界表。冲突时按服务端总设计 §0 的优先级处理；本文不复制协议、状态机或业务规则的第二份定义。

- 目标架构、安全红线、上线门槛以[服务端总设计](../architecture/商业级通用服务端框架总设计.md)为准。
- 客户端侧消费契约（`MetaClient`、Login/Lobby/Result 流程、Account Scope）以[客户端总设计](../architecture/商业级通用客户端框架总设计.md)§10.1/§19 C2 为准；本文只定义服务端一侧。
- 战斗与局外权威边界、DTO 命名（`Command`/`DomainEvent`/`Snapshot·Push`）、UTC 与错误码形状以[游戏业务系统总设计](../gameplay/游戏业务系统总设计.md)§2/§3.3/§3.4 为准。
- 版本字段分工（`protocolVersion`/`simVersion`/`buildHash`/`configHash`/`contentVersion`）与规范化规则以服务端总设计 §P0-5 及[热更专项](../client/content/热更与内容发布专项设计.md)§5 为准。
- 施工先后与准入以[框架先行建设与业务接入专项设计](../architecture/框架先行建设与业务接入专项设计.md)、《[待办总览](../../待办总览.md)》为准。

### 1.1 待裁决问题与当前选择

| 待裁决问题 | 当前目标选择 |
| --- | --- |
| 宿主框架 | **ASP.NET Core Minimal API + Kestrel**，以 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 引用共享框架，不引入任何 NuGet Web 包（见 §4.1） |
| 是否一开始拆微服务 | 不拆。第一版为**模块化单体**，Auth/Lobby/Profile 是同进程内的独立模块与数据所有权（服务端总设计 §11.1） |
| 是否自研 HTTP 栈 | 否。禁止用 `HttpListener`、裸 `Socket` 或第三方自研框架重写路由、TLS、HTTP/2、健康检查与优雅关闭 |
| Chat/Guild 是否同期 | 否。战斗服稳定后加入，可同进程但保持独立模块与数据所有权（§11.1 末段） |
| 房间内热路径是否查 Meta | 否。 `RoomServer` 只验本地公钥，对局热路径不查 Profile 数据库（§P0-6） |
| RoomServer 是否直接写 Meta 库 | 否。只输出受验证 `RewardDelta`，由 `Profile.Apply` 在事务内落账（§11.2/§11.3） |
| 是否引入 Redis 作为事实源 | 否。Redis 只承载可丢失或可重建的在线状态、限流桶、注册表与短期 TTL（§11.2） |
| Meta 是否内嵌 Content/热更控制面 | 否。内容发布描述与信任根由热更专项定义；Meta 不另建第二个分发控制面 |
| 构造语法档位 | **`LangVersion 10.0`**（不沿用 RoomServer 的 9.0）。C# 9 不支持最小 API 的惯用写法——target-typed lambda（`MapGet("/x", () => "ok")`）与方法组处理器均编译失败（`CS1593`/`CS1503`/`CS0030`），只有逐端点显式委托转换可用。其余档位同 RoomServer：`net8.0` / `Nullable disable` / `ImplicitUsings disable` |

## 2. Current：2026-09-25 核查事实

| 范围 | 核查结果 |
| --- | --- |
| 服务端代码 | `RoomServer/`（`Runtime/` + `Application/` + `Program.cs`）之外，**MetaServer 宿主骨架已建立（2026-09-25，[Meta 服务宿主](../../施工进度/Meta服务宿主.md)）**：Generic Host + Options + 健康检查 + 优雅关闭，`FrameworkReference` 零 NuGet 接入。Auth/Lobby/Profile/Mongo/Redis/Outbox 业务仍无实现或桩 |
| 宿主 | `RoomServer/Program.cs` 常驻形态已接 `Console.CancelKeyPress` 触发排空（2026-09-26，[服务端多房间](../../施工进度/服务端多房间.md)，"信号→排空→退出"链路待真实终端手验）；Meta 宿主骨架交付内容见上行——`ValidateOnStart` 范围校验、`/live` `/ready` `/metrics`、drain 与入站上限均已建立 |
| 身份与票据 | `ReconnectService` 已具备票据**房间绑定**（R1 交付），但签发的仍是**可预测串**（CSPRNG 归 R2）；**Join 已接票据验签接缝（M0-d，2026-09-26）**：`IJoinTicketValidator` + `HmacJoinTicketValidator`（过期/篡改/重放/密钥轮换/受众/房间/哈希矩阵），接入 `ServerHost.HandleJoin` 真实准入路径且验证先于建房——"只校验非空 token"已终结；Meta 侧签发端（Auth）归 G3 |
| 签名原语 | `Assets/LiteFramework/Scripts/Core/Content/SignatureVerifier.cs` 已落地**RSA-2048 + PKCS#1 v1.5 + SHA-256**，并留档实测结论：`ECDsa`/`ECDsaCng` 在 Mono 下抛 `NotImplementedException`，Ed25519 无类型。该结论是**客户端运行时**的约束，服务端为完整 .NET，但为保持单一密钥体系，Meta 沿用同一原语 |
| Web 依赖 | 仓库内**没有任何 Web/HTTP 服务端代码**；仅 `UniRx/Scripts/UnityEngineBridge/ObservableWWW.cs`（无关）。`global.json` 锁 SDK `8.0.400`（`rollForward: latestMajor`），本机 `Microsoft.AspNetCore.App` 8.0.22 与 9.0.11 均已安装 |
| 版本字段 | `ServerHost.ServerBuildHash` 与 `CombatConfigDigest`（SHA-256 截取 uint32）已存在；`protocolVersion`/`simVersion`/`contentVersion` 的目标语义尚未在协议中全部落地 |
| 客户端消费端 | `MetaClient` 未实现。`ProcedureId` 刻意未加 Login/Lobby/Result 枚举，避免出现空阶段（[客户端 C2 记录](../../施工进度/客户端C2.md)） |
| 测试接缝 | L1 为 .NET xUnit（`Tests/*.Tests`，入口 `Tests/Tests.slnx`）；**尚无 HTTP/数据库测试容器夹具**（测试开发框架总设计 §8 列为待补） |
| 文档坐标 | Meta 的设计坐标分散在服务端总设计 §6/§11/§13、业务总设计 §2、《待办总览》G3。**本文是 Meta 的唯一专项入口** |

结论：Meta 当前是**骨架接缝阶段**——宿主骨架（2026-09-25）与 Join 票据验签接缝（2026-09-26）已交付，Auth/Lobby/Profile/Mongo/Redis/Outbox 业务仍为设计；完成度判定见服务端总设计 §1「数据与 Meta 服务」行（已同步至骨架阶段）。本文除本节外均为 Target。

## 3. 服务边界与数据所有权

### 3.1 模块职责

| 模块 | 唯一负责 | 不负责 |
| --- | --- | --- |
| Auth | 登录、账号绑定、访问令牌签发与校验、身份到 `accountId` 的映射 | 权限业务语义、库存、房间 |
| Lobby | RoomServer 实例注册与容量上报、匹配与房间分配、**Join Ticket 签发**、Match 状态对外投影 | 房间内状态、战斗判定、重连票据（归 RoomServer） |
| Profile | 进度、库存、Loadout、Reservation、**Settlement Ledger** | 对局中状态、奖励数值计算（只接受受验证 `RewardDelta`） |
| Shared Infra | Mongo、鉴权中间件、Outbox/Inbox、HTTP 管线、Metrics、健康检查 | 任何业务规则 |

### 3.2 Meta 不做什么

- **不产生战斗事实**。伤害、击杀、命中、弹药、冷却不由 Meta 判定或接受客户端上报（业务总设计 §2 严格禁止项）。
- **不阻塞 Room Worker**。Meta 不向房间热路径回调用；房间需要持久化的结果先写**有界 Outbox**，由后台服务重试（服务端总设计 §6 末段）。
- **不向房间提供可变的永久数据**。入场数据固定为不可变 `MatchStartSnapshot`；`Running` 期间房间不读取永久角色数据（§9.1）。
- **不成为第二个内容控制面**。发布描述、信任根、兼容矩阵归热更专项；Meta 最多读其产物。
- **不在 `RoomRuntime` 内被引用**。`RoomServer/Runtime` 的纯化红线由 L0 纪律扫描 R11 把守，Meta 的任何类型都不得进入该层。

## 4. 拓扑、宿主与工程结构

### 4.1 宿主选型：ASP.NET Core Minimal API + Kestrel

**裁决：采用 ASP.NET Core Minimal API，以共享框架引用接入，不引入 NuGet Web 依赖。**

依据：

1. **零新增依赖**。`net8.0` 上 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 引用的是随 SDK 安装的共享框架，**不产生 `PackageReference`**，不进入 NuGet 还原图。符合 C0-① 的依赖治理与 `scripts/l0-dep-scan.ps1` 的依赖/许可扫描口径。WebSocket 同属该共享框架，不需要额外包。
2. **与 RoomServer 的宿主规划收敛**。服务端总设计 §12 已裁定 `RoomServer.Host` 走 .NET Generic Host、Options `ValidateOnStart`、启动失败返回非零退出码。两条线共用同一套 Host/Options/`IHostedService`/Cancellation/优雅关闭纪律，不产生第二套启动范式。
3. **§13.3 与 §14 的要求是现成路径**。`/live`、`/ready`、`/metrics`、readiness gate、多阶段 Dockerfile、非 root、只读根文件系统、显式端口、优雅终止时限，在 ASP.NET Core 上都有受支持的标准做法；用 `HttpListener` 或自研栈重写这些是纯负债，且会引入自研 TLS/HTTP 的合规风险。
4. **契约与诊断同源**。结构化日志、`Activity`/`Trace`、限流中间件与 OpenTelemetry 接入点在 R4 的 §13 目标里有直接对应物。
5. **零依赖的 HTTP 集成测试可行**。`WebApplicationFactory`/`TestServer` 都需要 `PackageReference`，与上述约束冲突；改用 `WebHost` 绑定 `http://127.0.0.1:0` 取临时端口、启动后从 `IServer` 的 `IServerAddressesFeature` 取回真实地址、再以 `HttpClient` 打真实回环——全程零 NuGet，且测的是真实 Kestrel 而非内存管道。L3 集成用例用此形态。

**被否决的选项：**

| 选项 | 否决理由 |
| --- | --- |
| `System.Net.HttpListener` | 无路由、无 HTTP/2、无 TLS 终止、无健康检查与优雅关闭接缝；等于自研半个 Web 服务器 |
| 自研 Socket + HTTP 解析 | 违反 §19"禁止自创"同类纪律在传输层的延伸；安全面不可控 |
| 第三方轻量框架（Nancy 类、Fleck 等） | 增加一个需长期维护的依赖，换取 ASP.NET Core 已具备的能力；与 C0-① 的依赖最小化相悖 |
| gRPC / 二进制协议作主接口 | 客户端主线是 HTTPS/JSON（客户端总设计 §10.1:267、业务总设计 §2）；改协议形状会波及 `MetaClient` |

**形态约束：** 首版只在**一个 Host 进程**内以模块（`Auth`/`Lobby`/`Profile`）组织，模块间**不跨进程**、不共享可变状态、各自拥有数据；拆进程只在容量、故障域或团队所有权提供证据后发生（§3 保留决策）。

### 4.2 目标工程结构

```text
MetaServer/                              单一工程起步（§7 过渡步骤 1）
├─ Contracts/                            协议版本、错误码、请求/响应 DTO、DomainEvent
├─ Modules/
│  ├─ Auth/                              登录、令牌、身份映射
│  ├─ Lobby/                             实例注册、容量、匹配、Join Ticket 签发
│  └─ Profile/                           进度、库存、Loadout、Reservation、Ledger
├─ Infrastructure/                       Mongo、Outbox/Inbox、鉴权、Metrics
└─ Host/                                 Generic Host、Options、健康检查、装配

Tests/MetaServer.Tests/                  L1（纯逻辑/契约）
Tests/MetaServer.IntegrationTests/       L3（HTTP/DB 测试容器）
```

**过渡顺序（对齐服务端总设计 §7，禁止目录大搬迁）：**

1. 先在 `MetaServer/` 工程内按上述 namespace 与文件夹建立边界。
2. 用 characterization tests 固定既有行为，消除静态单例、系统墙钟与直接 IO。
3. 依赖方向稳定后**每次只拆一个 csproj**（顺序建议：`Contracts` → `Infrastructure` → 各 `Modules` → `Host`）。
4. 新工程加入 `Test.sln`；测试工程加入 `Tests/Tests.slnx`，沿用 L1 现有执行入口。
5. `Contracts` **不得**引用 `Modules`/`Infrastructure`/`Host`；`Modules` 之间只经 `Contracts` 与显式接口通信。该方向由 L0 扫描把守（§14）。

### 4.3 与 RoomServer 的接缝

```text
Meta  ──签名 Join Ticket──►  RoomServer（本地公钥验签，不查库）
Meta  ──MatchStartSnapshot（不可变）──►  RoomServer
RoomServer  ──签名 SettlementEnvelope──►  Meta.Profile.Apply
Meta  ◄──RoomServer 实例注册/容量/心跳──  Lobby
```

- 四个方向的**版本准入**统一走服务端总设计 §P0-5 的字段分工；不匹配即拒绝并记录稳定原因码。
- 接缝 DTO 与 `battle.proto` 分离：局外契约不混进战斗协议（业务总设计 §3.3）。
- RoomServer 侧的验签与注册消费归 R2；Meta 侧的签发与分发归 G3。两端的**契约测试**在任一侧先行时同步建立。

## 5. 接口面与统一契约

### 5.1 三名分开

禁止用一个 DTO 兼三种语义（业务总设计 §3.3 末段）：

| 名 | 语义 | 方向 |
| --- | --- | --- |
| `Command` | 客户端请求，可被拒绝 | C→S |
| `DomainEvent` | 已发生的事实，成功后发出 | S→C（WebSocket 推送） |
| `Snapshot` / `Push` | 同步载荷，可重复投递 | S→C |

### 5.2 错误、时间与版本

- **错误统一形状** `{code, messageKey, args}`；`code` 是稳定机器可读标识，`messageKey` 交给本地化，`args` 只放非敏感参数。错误码表归 `Contracts` 单源，客户端不解析自由文本。
- **时间字段一律 UTC**，使用带时区的 ISO-8601；禁止本地时间与隐式时区推断。
- **版本字段分工**沿用 §P0-5 五件套。接口在拒绝版本不匹配时返回稳定原因码（"过旧/过新/不兼容"三类可区分），不只返回一个布尔。
- **JSON 规范化必须版本化**：固定字段/表/id 排序、类型、字符串编码、整数宽度、浮点表示、缺省/缺失规则与 schema 标识；拒绝非有限值，规定负零语义。幂等键与任何摘要都算在规范化结果上，不能依赖序列化器默认行为（热更专项 §5）。

### 5.3 幂等与并发

- **所有外部写操作带 `requestId`/`operationId`**；数据库唯一键是**最终幂等裁判**，不以应用层重试纪律代替（§11.2）。
- **库存使用 revision CAS**（`expectedRevision`），公会使用 `guildVersion` CAS；CAS 失败返回可区分的冲突码，不静默覆盖。
- **响应丢失后的重放必须返回原结果或首次结果**，不得二次生效；重复提交只允许生效一次。
- 幂等窗口、客户端重试与超时都是**有界的**：上限、退避与放弃条件在配置中显式声明。

## 6. Auth：身份、令牌与票据

### 6.1 访问令牌

- 登录产出的访问令牌绑定 `accountId`、签发时刻、过期时刻、`audience` 与令牌版本；校验必须验证**签名、过期、audience 与用途**，任一项失败即拒绝（fail-closed）。
- 令牌**不进日志**（§13.1 禁止项）。
- 刷新与吊销：刷新令牌与访问令牌分离；吊销以 `jti`/版本号在服务端可判定，不依赖客户端配合。

### 6.2 Join Ticket

Join Ticket 是 Meta 与 RoomServer 之间**唯一**的身份接缝，必须满足（§P0-6）：

- 绑定 `playerId`/`accountId`、`roomId`/`matchId`、过期时刻、`nonce`、`build`/`sim` 版本与 `audience`。
- 由 **Meta 私钥签发**，`RoomServer` 用**本地公钥**验签；对局热路径**不查询** Profile 数据库。
- 有效期**短期**；`nonce` 用于重放判定，`RoomServer` 维护有界的一次性消费窗口。
- 拒绝语义 **fail-closed**：任何校验失败即作废该票据，不降级为"仅警告"。
- `KCP cookie` 只解决连接探测，**不等同**业务身份、加密或完整性保护（§P0-6 末段）——不得以 cookie 存在宣称身份已校验。

### 6.3 签名原语、密钥与轮换

**裁决：RSA-2048 + PKCS#1 v1.5 + SHA-256，与 `SignatureVerifier` 同源。**

- 依据：该组合与热更内容签名的发布信任根同源（`Assets/LiteFramework/Scripts/Core/Content/SignatureVerifier.cs`），保持**单一密钥体系**。客户端运行时（Mono）无 `ECDsa` 实现、无 Ed25519 类型，换档位无效；Meta 侧虽是完整 .NET，但"服务端用更强曲线、客户端无法验证"没有价值。
- **密钥标识**：签名信封携带 `kid`，验签方按 `kid` 选择公钥；轮换期间新旧公钥并存。
- **轮换与撤销**：必须有可执行流程与到期策略；禁止把"换密钥"实现为需要同时停机的操作。
- **私钥永不入库**、不进入仓库或客户端产物；由受控签名环境持有，经 Secret Provider 注入（热更专项 §6）。
- **失败语义**：验签失败、`kid` 未知、描述过期、时钟回拨、长时间离线，各自有明确结果；**不得仅依赖本地系统日期断言安全**（热更专项 §6）。

**客户端时钟回拨与离线**：票据与令牌的过期判定以**服务端时钟**为准；客户端本地时间只用于提前刷新，不作为拒绝或接受的依据。

## 7. Lobby：房间注册与分配

- **实例注册**：`RoomServer` 实例启动后向 Lobby 注册（实例 ID、版本、房间容量、当前占用、健康状态），并周期性上报；心跳超时即从可分配集合移除（§14）。
- **容量驱动分配**：Lobby 只在**已上报容量**的实例间分配；不按估算值分配，不把运行中的新 Match 分给正在 drain 的实例。
- **drain 协同**：实例置 readiness=false 后，Lobby 立即停止向其分配新 Match；既有房间按 §12 流程排空（§12 优雅关闭）。
- **版本准入**：Lobby 分配时校验 `protocolVersion`/`simVersion`/`buildHash` 兼容性；协议或 Sim 不兼容的发布采用**版本并存**——旧 Match 继续由旧实例完成，新 Match 只进入新实例（§14 末段）。
- **匹配**：首版只做"有容量即可加入"的最小分配，不做评分匹配；具体策略按产品需要扩展，不在框架期冻结。
- **Join Ticket 在此签发**：Lobby 完成分配后签发 §6.2 的票据，与 `roomId`/`matchId` 一并下发。
- **Match 状态投影**：Lobby 展示的房间/对局状态是 RoomServer 上报事实的**投影**，不是权威；投影过期即降级显示，不猜状态。

## 8. Profile：进度、库存与结算

### 8.1 数据原则

- MongoDB 使用**副本集模式**；事务只围绕必须原子化的 Ledger/Inventory 更新，不做跨无关聚合的宽事务（§11.2）。
- 库存 `revision CAS`；Loadout 走 `Reservation`（进局前预留、结算后确认或释放）。
- **`RoomServer` 不保存永久库存，不接受客户端上报奖励数量**。
- 局外物品、装备、货币的唯一权威是 `ProfileService`；客户端只发 UseCase。

### 8.2 结算 Outbox / Inbox

```text
RoomRuntime 冻结 MatchResult
→ SettlementBuilder 生成签名 SettlementEnvelope
→ RoomServer 本地持久 Outbox
→ 后台提交 Profile.Apply
→ Profile 事务内写唯一 Ledger + 更新库存/进度
→ 返回原结果或首次结果
→ Outbox 标记完成
```

- **推荐唯一键**：`operationId` 与 `(playerId, matchId, settlementType)`。Profile 超时、RoomServer 重启或网络重试**不得重复发奖**。
- **幂等裁判在数据库**：唯一索引冲突即判定为重复，返回首次结果而非报错给调用方。
- **持久化确认点必须写明**：成功返回之前什么已经落盘，是契约的一部分；不能靠"通常会成功"。
- **重启恢复**：进程重启后 Outbox 可恢复并继续提交；已确认的结算不丢、不重。
- **可观测**：`settlement_outbox_pending`、`settlement_retry_total`、`settlement_idempotent_hit_total` 为必备指标（§13.2）——`idempotent_hit_total` 持续增长是上游重试异常的早期信号。

## 9. 存储与依赖

### 9.1 MongoDB

- 唯一权威存储；**唯一索引是幂等的最终实现**，不是应用层判断。
- 索引与迁移显式版本化；迁移失败有明确结果与回滚方式，不静默半迁移。
- 备份恢复演练、Outbox 堆积恢复与回滚手册属于 R4 交付（§14）。
- 测试用**测试容器**验证唯一 Ledger 与事务重试（§15 L3），不使用内存替身证明事务语义。

### 9.2 Redis

- **只用于可丢失或可重建的数据**：在线状态、限流桶、实例注册表、短期 TTL 缓存。
- **不作为永久事实来源**；禁止把 Redis 当数据库、把 MQ 当幂等保证（§19）。
- Redis 不可达时，Meta 的降级行为必须显式定义（哪些接口拒绝、哪些降级），不能表现为数据丢失。

## 10. 配置、启动与关闭

- **Generic Host + Options**：绑定并在启动阶段 `ValidateOnStart`；端口、连接串、超时、限流阈值、Outbox 容量与重试上限均有**范围校验**。
- **配置来源**：版本化文件、环境变量与 Secret Provider；命令行只用于本地覆盖。**Secret 不入库**（§12）。
- **启动失败即非零退出码**：不得带默认错配置继续运行（§12）。
- **优雅关闭**：

  1. readiness 置 false（Lobby 立即停止分配）。
  2. 停止接受新的外部写；在途请求在时限内完成。
  3. 刷新 Outbox 到持久介质；未提交项保持可重试状态。
  4. 停止后台服务与 Host。

- **`/ready` 的判定必须包含真实依赖**（Mongo 可达、Outbox 未超阈），不能只证明进程活着。

### 10.1 装配契约（必须遵守）

以下三条是上述条款的**实现约束**。它们描述典型的框架行为，违反时**不抛异常、不报警告**，只在运行时表现为"配置未生效"，因此必须显式遵守：

1. **配置只由 Options 管线持有一份**。另注册配置单例（`AddSingleton(config)` 与 `AddOptions<T>()` 并存）会解析出**两个不同实例**，`IValidateOptions` 校验的是无人使用的那个——非法配置照常启动成功，违反上方"启动失败即非零退出码"。消费方一律经 `IOptions<T>` 取用。
2. **配置类必须用属性，不得用 public 字段**。`ConfigurationBinder` 只绑定属性，字段形态会让版本化文件与环境变量配置源**完全不生效**，违反上方"配置来源"。`RoomServer.Runtime.RoomConfig` 的字段形态属过渡实现，不作为 Meta 侧范式。
3. **校验触发点可能在 `Build()` 而非 `StartAsync()`**。Host 构造 `ConsoleLifetime` 时会解析 `IOptions<HostOptions>`，若其配置委托依赖 `IOptions<MetaConfig>` 即提前带出校验。`Program` 必须把 `Build()` 与 `StartAsync()` 同置于捕获 `OptionsValidationException` 的块内，否则异常以未处理形式逃逸，拿不到稳定的非零退出码与错误清单。

### 10.2 配置项落地要求

配置项**不得只是躺在配置对象里的值**，必须接到真正生效的框架位置上；否则等同于未实现：

| 配置项 | 必须落到的位置 |
| --- | --- |
| 入站请求体上限 | Kestrel 的请求体上限与表单体上限（§P0-3"解析前限制长度"） |
| 关闭时限 | Host 的关闭超时 |
| 监听地址 | 由 `IOptions<T>` 解析后绑定，不得从另建实例读取 |
| 并发/队列上限 | 对应组件的真实容量参数（§13） |

指标字段同理：**不得存在有声明无写入的计数器**（如声明"被拒绝请求数"却从不递增）；未实装的字段应在接入前不声明。

## 11. 可观测性与 SLO

### 11.1 日志与追踪

- 结构化日志，公共字段：`service/version/instanceId/accountIdHash/roomId/matchId/requestId/event/errorCode/durationMs`。
- **禁止写日志**：token、刷新令牌、Join Ticket、重连票据、IP 全量值、完整 payload 与个人数据（§13.1）。
- **Trace 只用于低频跨服务流程**：登录、Match 分配、票据签发、结算提交。**不为每帧 Sim 或每次心跳建 Span**。

### 11.2 Metrics

- 必备指标：`http_request_total{route,code}`、`http_request_duration_seconds`、`auth_failure_total{reason}`、`ticket_issue_total`、`instance_registered`、`match_allocated_total`、`settlement_outbox_pending`、`settlement_retry_total`、`settlement_idempotent_hit_total`，以及进程 CPU/GC/堆/线程/Socket/异常计数。
- **标签禁用高基数字段**：`accountId`/`playerId`/`matchId`/`requestId`/`connectionId` 只进日志与 Trace，不进 Metrics 标签（§13.2）。
- 出口 `/metrics` 走 Prometheus/OpenTelemetry。

### 11.3 健康检查

| 端点 | 语义 |
| --- | --- |
| `/live` | 进程事件循环仍能响应 |
| `/ready` | 配置、Mongo（及启用的 Redis）、后台服务正常，且实例未处于 drain |
| `/metrics` | 指标出口 |

### 11.4 SLO 口径

具体数值由**产品并发目标与容量测试共同确定**，不在设计阶段虚构（§17 末段）。可测量的口径固定为：接口 p95/p99 时延、登录成功率、票据签发时延、结算提交成功率与重试率、Outbox 积压时长。

## 12. 安全与部署

- **传输**：外部接口一律 TLS；客户端侧必须验证证书链（客户端总设计 §10.2）。
- **限流分层**：按 IP / 账号 / 会话三层；阈值可配、超限有稳定错误码与指标，不做静默丢弃。
- **中间件顺序**固定：TLS 终止 → 请求体上限 → 限流 → 鉴权 → 路由 → 业务；顺序变更需回归。
- **请求体与字段上限**：长度、`repeated` 元素数、字符串 UTF-8 字节数在解析前限制（对齐 §P0-3 口径）；拒绝日志不回显 payload。
- **部署**：多阶段 Dockerfile、非 root 用户、只读根文件系统、显式端口；开发/测试/预发布/生产配置分离。
- **发布**：滚动发布先 drain 再停止；协议不兼容采用版本并存，不在运行中热替换契约（§14）。
- **扫描**：依赖漏洞、Secret、许可证、SAST 与镜像扫描纳入 CI（§14 与热更专项 §13）。

## 13. 预算与容量

**不虚构数字。** 以下为必须建立的口径与杠杆，具体数值在首次容量测试后写入本节：

| 口径 | 说明 |
| --- | --- |
| 单实例并发连接/请求 | 由容量阶梯测试确定，不按估算写死 |
| 登录/票据签发时延 | 记录 p95/p99 与失败分类 |
| 结算吞吐与积压 | Outbox 待处理量与重试率的上限，超限即告警并降级 |
| 存储 | 索引规模、单账号文档数、Ledger 增长速率与归档策略 |
| 连接池与队列 | Mongo 连接池、后台队列、限流桶**均有显式上限与清理策略** |

设计要求：**任何队列、缓存、票据窗口与重试缓冲都必须有显式容量与清理策略**，这是服务端总设计 §20 完成定义第 4 条，不因是 Meta 侧而放宽。

## 14. 测试矩阵

沿用《测试开发框架总设计》分层，不新建第二套分类。Meta 侧覆盖：

| 层级 | 覆盖 |
| --- | --- |
| L0 | 依赖方向（`Contracts` 不被反向引用；Meta 类型不得进入 `RoomServer/Runtime`）；Secret 与凭据扫描；错误码表与 DTO 生成物一致性；`GetHashCode()` 不用于持久化标识与协议字段 |
| L1 | 错误码与规范化 JSON 稳定性；幂等键语义；revision CAS 冲突路径；票据/令牌的过期、篡改、重放、`audience` 不符、`kid` 未知与**时钟回拨**；配置校验失败拒绝启动 |
| L3 | 真实 HTTP + **Mongo 测试容器**：唯一 Ledger 与事务重试；Outbox 提交前后失败、响应丢失、重复提交；进程重启后 Outbox 恢复；限流分层生效；drain 期间不再分配新 Match |
| L4 | 目标容量下长稳；Mongo 暂停、磁盘受限、Outbox 堆积与恢复；滚动升级与回滚演练；备份恢复演练 |

**必备故障矩阵**（对齐热更专项 §16 的持久点思路）：每个持久化确认点**前后**终止进程；票据重放与篡改；Mongo 不可达；Redis 不可达降级；结算重复提交 100 次只生效一次（§17 商业门禁"幂等"行）。

CI 仍以 `scripts/test.ps1` 为唯一入口；HTTP/数据库夹具随实现接入，不等到最后补。

## 15. 与总体施工路径的映射

| 阶段 | Meta 侧交付 | 与本文关系 |
| --- | --- | --- |
| **G1（当前）** | 只建**接缝**：必要存储端口、迁移/事务/幂等约束、故障夹具、**一个持久化样例**；Join Ticket **验证器接口**与非法票据测试 | 框架先行 §4"持久化"行与 §5-4；不建真实 Meta 业务 |
| **R2** | RoomServer 侧：Generic Host、Join Ticket **本地验签**、实例注册与容量上报、drain | 与 §4.1/§6.2/§7 对接；本文为 Meta 侧定义，R2 为房间侧消费 |
| **G3** | MetaServer 本体：Auth/Lobby/Profile、Mongo Ledger、Reservation、Settlement Outbox/Archive；客户端 `MetaClient` 与 Login/Lobby/Result（C2 批③） | 本文件的主体在此时落地 |
| **R4 / G4** | OpenTelemetry、Dashboard、告警、Docker、实例调度、灰度与回滚、长稳与故障注入 | §11/§12/§14 的运维面收口 |
| **后置** | Chat/Guild；完整运营后台；微服务拆分 | §1.1 待裁决表；不进入首个战斗服 Beta |

**当前可开工的只有 G1 那一行**：存储端口、幂等约束、故障夹具与持久化样例，加上票据验证器接口。真实 Auth/Lobby/Profile 语义按《待办总览》G3 排队，不提前实现空壳模块，也不在客户端 `ProcedureId` 中预置空阶段。

## 16. 禁止的做法

- 禁止在 Meta 侧建立第二套协议 DTO、第二套快照或第二套版本字段（§19，服务端总设计）。
- 禁止用 `Task.Run`、锁或并发集合包住现有同步代码伪装成异步安全。
- 禁止每请求一线程、每消息一 Task 或无界 Channel；**所有队列必须有界**。
- 禁止把 Redis 当数据库、把 MQ 当幂等保证。
- 禁止用客户端上报的奖励、时间、身份或 ACK 作为事实。
- 禁止自动重试掩盖不稳定测试或掩盖非幂等写操作。
- 禁止在没有容量数据前拆 Auth/Lobby/Profile 微服务。
- 禁止以"接口存在"或"能登录一次"宣称 Meta 已完成（服务端总设计 §20 完成定义）。

## 17. 关联文档

- [商业级通用服务端框架总设计](../architecture/商业级通用服务端框架总设计.md)：目标架构、R0～R4、安全红线与商业 Beta 门槛；本文的上位裁决。
- [商业级通用客户端框架总设计](../architecture/商业级通用客户端框架总设计.md)：§10.1 会话分层、§19 C2 的 `MetaClient` 与 Login/Lobby/Result。
- [游戏业务系统总设计](../gameplay/游戏业务系统总设计.md)：§2 权威边界表、§3.3 通道选择、§3.4 版本与幂等。
- [热更与内容发布专项设计](../client/content/热更与内容发布专项设计.md)：§5 版本与规范化、§6 发布描述与信任边界、§13 发布证据。
- [框架先行建设与业务接入专项设计](../architecture/框架先行建设与业务接入专项设计.md)：§4 施工包、§5 必须先稳定的接缝、§7 后置事项。
- [测试开发框架总设计](../quality/测试开发框架总设计.md)：L0～L4、执行器与完成定义。
- [待办总览](../../待办总览.md)：项目级当前优先级；本文不改变其排序。
- `Assets/LiteFramework/Scripts/Core/Content/SignatureVerifier.cs`：签名原语的唯一实现与算法选型依据。
