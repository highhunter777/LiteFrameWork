# 服务端 R3-Lobby 批：施工进度

> 依据：[《Meta 服务专项设计》](../设计文档/architecture/Meta服务专项设计.md) §6.2/§7/§12（Lobby：实例注册、容量驱动分配、drain 协同、版本准入、Join Ticket 签发）；[《上云测试专项设计》](../设计文档/architecture/上云测试专项设计.md) §2/§4/§5（封闭测试"游客→进房"链的签发端）。
> 提交：`3da054f`（本机复跑 L1 1525/1525 ＋ L3 156/156 确认）。
> 本文件只记录已执行的施工状态与验证证据；设计裁决只写入设计文档。

## 批次规划（R3 线）

| 批 | 范围 | 状态 |
|---|---|---|
| R3-Auth-1 游客登录 | 访问令牌/游客账号/`POST /auth/guest`（见[服务端R3首批](服务端R3首批.md)） | 已完成（附验证证据） |
| **R3-Lobby** | 实例注册/心跳（TTL 清扫、有界）、容量驱动分配（跳过排空/满员）、版本准入、**Join Ticket 签发**（`JoinTicketFormat` 单源）、房间投影查询、RoomServer 注册客户端（含排空上报） | **已完成（附验证证据）** |
| R3-Auth-2 正式身份 | 账号绑定、刷新令牌、吊销与受保护端点中间件 | 未开始 |
| R3-Profile | 结算提交消费、Ledger/Inventory、结果查询与 Archive | **已完成（附验证证据）**——见[服务端R3-Profile](服务端R3-Profile.md) |

## 施工记录

### 2026-10-08 · R3-Lobby 实例注册与 Join Ticket 签发端

**Meta 侧（分配与签发）**

- `Server/MetaServer/Contracts/Lobby/LobbyContracts.cs`：稳定错误码表（`lobby.invalid-request` / `lobby.instance-unauthorized` / `lobby.disabled` / `lobby.no-capacity` / `lobby.version-mismatch` / `lobby.registry-full`）与 DTO（注册/心跳、签发命令与响应、房间投影；访问令牌失败复用 `auth.unauthorized`）。
- `Server/MetaServer/Modules/Lobby/InstanceRegistry.cs`：内存实例注册表——**注册即心跳**（get-or-create 幂等键 = `instanceId`；更新以换对象表达，快照读者不见半更新）；**有界**（表满拒新实例，既有实例心跳不受影响）；心跳 TTL 惰性清扫（时钟注入——L1 用虚拟时钟造超时，不 sleep）；**容量驱动分配**（跳过排空与满员；排序确定：玩家占用 → 房间占用 → Ordinal 实例标识）；版本过滤并**区分"版本冲突"与"无容量"**（处置不同）。
- `Server/MetaServer/Modules/Lobby/LobbyTicketSigner.cs`：签发端——组装/签名走 **`JoinTicketFormat`（源链接共编，单源红线）**；nonce = CSPRNG 128 bit；`MatchId`/`SimVersion` 置空（对局由房间创建自带 MatchId；Sim 版本分离未启用——不为"占位"编造值）。
- `Server/MetaServer/Modules/Lobby/JoinTicketIssueUseCase.cs`：字段边界（UTF-8 字节上限同游客登录口径）→ 分配 → 按实例上报的构建哈希**盖章**签发；结果型返回（端点映射 HTTP）。
- `Server/MetaServer/Host/LobbyEndpoints.cs`：
  - `POST /lobby/instances/register`——**实例密钥** Bearer（常量时间比较；Base64 解码后比对）；
  - `POST /lobby/join-ticket`——**访问令牌**鉴权（消费 AccessTokenService——Lobby 为首个受保护端点，§6.1 注释预留的消费方由此接入）；分配 + 签发；
  - `GET /lobby/rooms`——房间投影查询（同实例密钥鉴权）。
- `Server/MetaServer/Host/MetaConfig.cs` / `MetaHost.cs` / `Ops.cs`：Lobby 配置门（**半段配置拒启**：两密钥 + Auth 密钥三缺一即启动失败；密钥 ≥32 字节、kid 形状同房间端约束、TTL/心跳/容量范围校验）＋装配（消费值经 `IOptions`，与 Auth/持久化块同款）＋指标 `meta_lobby_instance_register_total` / `meta_ticket_issue_total`（有实装才声明）。
- `Server/MetaServer/MetaServer.csproj`：**源链接 `Assets/RoomServer/Application/JoinTicketFormat.cs`** 共编——签发与验签同一份形状规则（《上云测试》§2"物理上不会漂"）；该文件只依赖 BCL，不把 LiteNet/proto 带进 Meta。

**RoomServer 侧（注册客户端）**

- `Server/RoomServer/LobbyRegistrationClient.cs`：启动首发 + 周期心跳；载荷 = 宿主实时快照（buildHash = `ServerBuildHash`、容量、占用、**排空位**、实际监听端口回读）；**失败不退出对局服务**（失败计数 + 节流日志 + 下周期重试——Lobby 不可达只影响"新玩家分配"）；单在途请求、无队列；HTTP handler 可注入（L1 伪造 handler 直测载荷与失败路径）。
- `Server/RoomServer/RoomServerConfig.cs`：`lobby` 分区（url 空 = 不注册；url 配置时 `instance_id`/`advertise_host` 必填、**半段配置拒启**、心跳间隔 1000..300000ms 范围校验）；`Describe()` 含 lobby 段。
- `Server/RoomServer/HostAssembly.cs`：**装配收敛专项批2 首批对象**——Lobby 注册客户端注册进容器（惰性工厂；解析序=创建序 → 释放逆序天然"先停心跳、再收宿主"）。
- `Server/RoomServer/Program.cs`：`--lobby-instance-key` / `LITENET_LOBBY_INSTANCE_KEY`（**密钥不进配置文件**；配置 url 缺密钥启动即拒——fail-closed）；心跳在排空期间继续（`Draining=true` 上报至进程退出——§7 drain 协同的上行）。
- `Server/RoomServer/ServerHost.cs`：`OccupiedPlayerCount`（已进房席位占用；未进房连接不算占用）。
- `Config/roomserver.json`：lobby 分区说明 + 占位（url 空 = 默认关闭）。

**实施中实测（一处）**：

- **同步 `HttpClient.Send` 与自定义 handler 不兼容**：首版客户端用同步 `Send`，测试替身（只覆写 `SendAsync` 的 `HttpMessageHandler`）直接抛 `NotSupportedException`。改 `SendAsync + GetAwaiter().GetResult()`（专用心跳线程上同步等待，无同步上下文不可能死锁）——真 handler 与测试替身同一形态，7 例客户端用例转绿。

## 验证证据

| 层级 | 命令 | 结果 |
|---|---|---|
| 双服务构建 | `dotnet build Server/MetaServer/MetaServer.csproj -c Release` / `Server/RoomServer/RoomServer.csproj -c Release` | 0 错误（MetaServer 0 警告；RoomServer 仅 Luban 生成物既有 CS8981 警告） |
| 全量 L1 | `powershell -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest -ResultsDirectory TestResults/r3-lobby-l1-final` | **1525/1525 通过，0 失败**（1472 → +53；产物目录 `TestResults/r3-lobby-l1-final/`） |
| 跨端同源闭环（L1） | `Tests/MetaServer.Tests/JoinTicketIssueUseCaseTests.cs` | Meta 签发票据 → 房间端**真实** `HmacJoinTicketValidator` 验签通过；篡改/过期/受众/房间不符/重放分类逐项断言——**签发端形状漂移本组必红** |
| 注册表与用例（L1） | `LobbyRegistryTests` / `MetaConfigTests` / `LiteNet.Tests/LobbyRegistrationClientTests` / `RoomServerConfigTests` | 心跳超时/表满/分配确定序/版本过滤；配置半段拒启；客户端载荷字段与排空位透传、失败不抛 |
| L3 真 HTTP 闭环 | `Tests/MetaServer.Tests/LobbyHttpTests.cs`（Integration trait，真 Kestrel + 真回环 HTTP） | **11/11**：实例注册（鉴权/字段/表满）→ 分配 → **真 `LobbyRegistrationClient`** → 签发 → 房间端验证器验签全链；心跳超时/排空窗口/无容量/版本 409/令牌拒绝各路径 |
| L3 车道 | `powershell -File scripts/gate/test.ps1 -Lane L3 -Profile PullRequest -ResultsDirectory TestResults/r3-lobby-l3` | **156/156 通过，0 失败**（145 → +11＝LobbyHttpTests；本工程 MetaServer.Integration.Tests **30/30 真容器全实跑、0 跳过**） |
| L0 依赖与密钥扫描 | `powershell -File scripts/gate/l0-dep-scan.ps1` | 1 条**既有**红（`JoinAdmissionGateTests` 测试字面量误报，非本批引入，留裁决）；**本批零新增** |
| buildHash | `python scripts/codegen/gen-build-hash.py --check` | **CHECK OK：`07dbe182f67d0e69` 不变（75 文件）**——本批全在 hash 闭包外（Sim/协议/表未动） |
| 冒烟（三形态） | `RoomServer.exe --duration`：①裸 KCP ②信封 ③lobby 启用（指向不可达 Meta） | 三形态 **exit 0**、严格 60Hz（180/210 ticks、掉时债 0）；lobby 形态失败按节流日志提示"对局服务不受影响"，恢复后自动续报 |

## 边界与下一批

- 本批未启动 Unity，不跑 L2 EditMode/PlayMode/Player（本机无 Unity 环境）；buildHash 未变，Unity 侧无需重编译对齐。
- **注册表驻进程内存**：单 Meta 实例形态；多 Meta 实例部署需共享存储（Redis 注册表——《Meta 服务专项设计》§9.2"可丢失/可重建"数据）。本批不做，属部署形态。
- Meta 侧**未做限流分层**（§12）：封闭测试由 IP 白名单 + 规模约束覆盖；正式公网接入前需补（随 Auth-2 或 R4）。
- Lobby 完整语义未做：实例心跳在途健康判定降级、实例再平衡、`MatchStartSnapshot`（归 R3-Profile）。「Join Ticket 签发与查询」的"查询"落为房间投影 `GET /lobby/rooms`。
- 进程级 Ctrl+C 排空手验沿用 2026-10-04 三场景基线（[服务端多房间](服务端多房间.md)）；本批"排空位上报"以两侧证：L1 载荷透传 + L3 排空实例拒分配；未重跑进程级全链。
- 客户端一键游客入口与结果页（《上云测试》批 C）需 Unity 环境；`POST /matches/result`、`GET /matches`、RoomServer 提交管道与 SIGTERM 排空（批 A 剩余项）归 R3-Profile——**已于 R3-Profile 交付（2026-10-10，[服务端R3-Profile](服务端R3-Profile.md)）**。
