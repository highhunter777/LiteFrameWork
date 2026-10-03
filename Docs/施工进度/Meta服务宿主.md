# Meta 服务宿主骨架：施工进度

> 依据：[《Meta 服务专项设计》](../design/architecture/Meta服务专项设计.md) §4.1（宿主选型）/§4.2（工程结构）/§5.2（错误与版本）/§10（配置、启动与关闭）/§11.2-11.3（Metrics/Health）/§15（施工映射 G1 行）；[服务端总设计](../design/architecture/商业级通用服务端框架总设计.md) §12（Generic Host、非零退出码）、§P0-3（解析前限制长度）、§P0-5（buildHash）；[《框架先行建设与业务接入专项设计》](../design/architecture/框架先行建设与业务接入专项设计.md) §4"持久化"行、§5-4"会话与房间端口"。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

## 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| M0-a 宿主骨架 | `MetaServer/` 工程 + Generic Host 装配 + Options 范围校验 `ValidateOnStart` + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站请求体上限 | **已完成**（见下） |
| M0-b 接缝登记 | gitignore 白名单、`Tests.slnx`、L0 纪律扫描目标（R11 纯化边界） | **已完成**（见下） |
| M0-c 持久化接缝 | 存储端口、迁移/事务/幂等约束、故障夹具、一个持久化样例（框架先行 §4"持久化"行） | **已完成**（2026-09-30：批一契约面 + L1、批二真 Mongo 实存储 + L3 重启恢复报告——见下） |
| M0-d 票据接缝 | `IJoinTicketValidator` 接口 + 非法票据测试（服务端总设计 §P0-6；§5-4"没有真实登录业务时也不能省略票据验证接口与非法票据测试"） | **已完成**（2026-09-26，见下） |

**范围界定**：本批只交付宿主骨架，**不含任何业务模块**——Auth/Lobby/Profile 归 G3（《Meta 服务专项设计》§15），不提前建空壳模块（客户端 `ProcedureId` 已按同一原则刻意未加 Login/Lobby/Result 枚举）。

**M0-d 归属更正（2026-09-26）**：票据验证器接口**不在 MetaServer 工程内**，落在 `RoomServer/Application/`——
《服务端总设计》§7 的"建议最小接口"把 `IJoinTicketValidator` 列在 RoomServer 工程结构下，Meta 专项 §15
R2 行亦写"RoomServer 侧：Join Ticket **本地验签**"。Meta 侧只**签发**（§6.2"唯一身份接缝"）。
本表原把它记作 Meta 批次，属归类不准；实现按设计归属走。

## 施工记录

### 2026-10-03 · MetaServer 硬化批（架构审查 P2/P3 修复：线程安全/脱敏/元数据权威/门禁化）

**背景**：2026-10-03 服务端架构审查登记的 MetaServer 侧债务，按"信封线并行不相交"原则收口本批
（不触 RoomServer/Transport；MetaServer 亦不在 RoomServer buildHash 闭包内，无哈希联动）。

**交付物**：

| # | 项 | 内容 |
| --- | --- | --- |
| H1 | `Ops` 线程安全 | Kestrel 并发请求共用同一实例——计数改 `Interlocked`（`CountRequest`/`CountRejected` 方法，读面保留只读属性）；`/metrics` 格式化改每次调用独立缓冲（原共享 StringBuilder 并发互踩）。L1 并发用例（8 线程 × 1 万次）钉住不丢计数 |
| H2 | Mongo 连接串脱敏 | 非法 URI 校验错误**不再回显原串**（可能含凭据，原样泄入 stderr/日志）；只报长度与形状提示 |
| H3 | Outbox 元数据存储端权威 | `OutboxEnvelope` 增 `CreatedAtUtc`/`LastFailureReason`（默认参数，既有 4 参构造零破坏）；**调用方只声明 OperationId/Payload**——入队时存储端强制 Pending/0/当前 UTC（修复 Attempts 播种）；`RecordFailureAsync` 改条件更新：**只对 Pending 生效**（已 Confirmed 幂等不累加、未知无操作）并落原因/时刻（修复"丢 reason 且 Confirmed 也累加"）。`OutboxDoc` 增三字段（Bson 无 schema，旧文档读回缺省值，**无迁移**、索引不变）。UTC 口径对齐 §5.2 |
| H4 | Program 退出诊断与 CLI 门禁 | 未知参数/`--bind` 缺值 = 配置错误退出码 2（修复"静默忽略拼错覆盖项"与 fail-closed 矛盾）；解析收口 `MetaHost.ParseCliOverrides`（L1 可测）；启动异常改打完整堆栈（`ToString`——迁移/依赖失败需要原始抛点） |
| H5 | 超时可配 | `MongoServerSelectionTimeoutMs`（默认 5000，100..60000）/`ReadyPingTimeoutMs`（默认 2000，100..10000）落 Options 管线（修复两处硬编码），范围校验进 `Validate` |
| H6 | TLS 门禁化 | http 绑定只允许回环地址（127.0.0.1/::1/localhost）；非回环 http 默认拒绝、需显式 `AllowNonLoopbackHttp=true`（受信内网豁免）——§12"外部接口一律 TLS"从注释落成启动期门禁 |

**验证证据**：

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L1 | `dotnet test Tests/MetaServer.Tests -c Release` | **69 / 0**（+15 例：超时范围×4、回环三形态、非回环拒绝/豁免、https 不受门禁影响、连接串脱敏、CLI 解析×3、Ops 并发计数、Outbox 元数据权威×3） |
| L3 | `MONGO_TEST_URI=mongodb://127.0.0.1:27017/?replicaSet=rs0 dotnet test Tests/MetaServer.Integration.Tests -c Release` | **24 真跑 / 0 失败** + 2 例容器重启恢复跳过（docker 依赖，同既有记录口径；用户态 mongod 8.0.32 副本集） |

**已知边界**：`/ready` 未含 Outbox 积压阈值（依赖派发器语义，归 R3）；`appsettings.json` 版本化文件载体仍缺（需"代码默认值 vs 文件基线"单源裁决，登记待办）；`SampleSettlementCommand` 挪出 Contracts 与样例错误形状统一（归 G3 契约批）；安全信封由并行线施工中（后注：同日 2026-10-03 批1 已交付，[安全信封](安全信封.md)）。

### 2026-09-30 · M0-c 持久化接缝（批二：真 Mongo 实存储 + L3 重启恢复报告）——M0-c 关闭

**范围**：MongoDB.Driver 装配裁决、真适配器三件、宿主接线（配置/启动迁移/样例端点//ready）、L3 容器夹具与用例、
**重启恢复报告**（§5-5"实际存储验证"+§14 L3 矩阵——M0-c 的完成判据）。本批达成，M0-c 标已完成。

**交付物**：

| 文件 | 内容 |
| --- | --- |
| `MetaServer.csproj` | **MongoDB.Driver 3.12.0**——服务端第一个真 NuGet。裁决登记于 csproj 注释：§4.1"零 NuGet"论证范围是 Web 包；§9.1 唯一权威存储/§14 Mongo 测试容器/§15 G1 存储端口共同要求真实驱动，自研线协议被 §1.1 否决。驱动只落 Infrastructure，Contracts 零驱动依赖 |
| `Infrastructure/Persistence/Mongo/MongoDocuments.cs` | 集合名单单源（迁移/适配器/L3 断言共用）+ 五个文档形状 |
| `Infrastructure/Persistence/Mongo/MongoSettlementLedger.cs` | 真结算账本：幂等双维度（_id＋业务键复合唯一索引）、**副本集事务内** CAS 推进＋账目写入（冲突/撞键整笔回滚）、Duplicate 返回首次快照、存储异常＝未确认（重试安全） |
| `Infrastructure/Persistence/Mongo/MongoOutboxStore.cs` | 真 Outbox：入队幂等（唯一 _id 裁判）、有界容量（满则 RejectedFull）、OutboxSeq 原子自增承载入队次序（$natural 无排序保证不用）、确认条件更新不回退、失败只累加计数 |
| `Infrastructure/Persistence/Mongo/MongoMigrations.cs` | 真迁移步骤 v1（ledger 建集＋业务键唯一索引）/v2（outbox 建集＋扫描索引）；**回滚＝结构回退（移除索引）不 drop 集合**——不销毁数据，步骤注释声明该与"完全互逆"的偏差 |
| `Infrastructure/Persistence/SchemaMigrationRunner.cs`（硬化） | 回滚后版本读回按尽力报告：存储不可达→recovered=-1＋rollbackFailed=true（原先裸异常穿透类型化结果承诺——死端口用例暴露） |
| `Host/MetaConfig.cs` | `MongoConnectionString`/`MongoDatabaseName`/`OutboxCapacity` 三项＋范围校验（连接串形状/配套库名/容量界） |
| `Host/MetaHost.cs` | 装配：连接串非空→注册 Mongo 家族（客户端/库/三端口/迁移执行器/样例用例/HostedService）；**功能门读**经 ConfigurationBinder 绑同节、**消费值**一律 IOptions（避开 2026-09-25 双实例失效）；`/ready` 在配置存储时 ping Mongo（2s 超时） |
| `Host/MigrationStartupService.cs` | 启动迁移；失败/被拒/存储异常统一 `InvalidOperationException` → **宿主拒绝启动（fail-closed，§9.1）** |
| `Host/SampleEndpoints.cs` | `POST /sample/settlement`——样例⑤"简单测试命令"HTTP 载体：accepted/duplicate 200（首次快照）、conflict 409、invalid 400、store-unavailable 503；**未配置存储→503 store-not-configured**（拒绝相应功能，不退回替身） |
| `Tests/MetaServer.Integration.Tests/`（5 件） | `MongoFixture`（MONGO_TEST_URI→探活→docker 自起容器含 rs.initiate；用例级独立库＋统一清理；`Xunit.SkippableFact` 显式跳过）＋四组 26 例 L3 |
| `scripts/test.ps1` | **命名契约守卫**（见下——防再次静默漏跑） |
| `Tests.slnx`/`.gitignore` | 新工程登记（含 csproj 白名单） |

**三处实测教训（如实）**：

1. **发现契约静默漏跑（本批最大陷阱）**：设计 §4.2 树形命名 `MetaServer.IntegrationTests` **不匹配** test.ps1 的
   `*.Tests.csproj` 发现通配符（"IntegrationTests.csproj" 不含 ".Tests.csproj" 子串）→ 门禁**静默丢弃**该工程
   （L3 总数 93 而非 118，无任何报错；M0-b 记录的警告以"照设计文本命名"的方式重现）。修复＝工程改名
   `MetaServer.Integration.Tests` 满足契约＋test.ps1 加守卫（`Tests/**/*.csproj` 不满足契约即显性 throw）。
   **门禁通配符是硬约束，设计文本让位并留痕**。
2. **docker 输出管道死锁**：`RunDockerAsync` 等退出后再读 stdout——`docker inspect` 的 KB 级 JSON 填满管道缓冲、
   进程写阻塞不退出 → 恒定 10s 超时 → docker 被误判不可用（重启用例 1ms 假跳过、容器时间戳证明 restart 根本没执行）。
   修复＝并发排空输出管道。定位靠指纹："每用例恰 10s＝inspect 超时值"。
3. **R11 豁免问题的实际答案＝无需豁免**：适配器纯编排（驱动在 NuGet 包内不被扫描）、自写代码不触碰 R11 禁用原语
   ——L1 纪律扫描零违规实证。批一登记的"两处注释矛盾"以"Infrastructure 保持 R11-clean by construction"化解，
   `ScanTargets.cs` 零改动。

**验证证据**（非原开发机；Mongo＝Docker `litegame-mongo` 容器，mongo:8.0 **单节点副本集 rs0**——事务要求副本集，§8.1）：

| 门禁 | 结果 |
| --- | --- |
| L1 全量 | **882 通过 / 1 失败**——失败为 BuildHash 快照既有红（批一定性：快照提交内容与常量不同步、远端 main 同样存在，与本批无关）；含纪律扫描：**驱动适配器首次落 Infrastructure，R11 零违规** |
| L3 全量 | **119 通过 / 0 失败 / 0 跳过**（LiteNet.Tests 87 ＋ MetaServer.Integration.Tests **26** ＋ MetaServer.Tests 6） |
| 冒烟（手验，非门禁） | 宿主**进程级**重启：两次起 `MetaServer.exe`（env `META_Meta__Mongo*`），同命令重发 → `duplicate` 首次快照；mongosh 核验 schema_version=2、`ux_settlement_key` 唯一索引在（**集合自动建不算证据，索引才是迁移实证**） |

**重启恢复报告**（§14 必备故障矩阵逐项——M0-c 完成判据）：

| 矩阵项 | 证据 | 形态 |
| --- | --- | --- |
| 实际持久化确认 | 首次提交 accepted＋介质可查（mongosh 直读账目）；迁移建索引可证 | L3＋冒烟 |
| 重复提交（两维度） | 同操作号/同业务键换操作号 → Duplicate 首次快照；**重复提交一百次只生效一次**（§17"幂等"行样例级闭环） | L3 |
| 提交边界（CAS 冲突） | 真实事务**整笔回滚**：账目无残留、修订未动；携带实际修订号重试成功 | L3 |
| 响应丢失 | L1 语义钉（FailAfterCommit→未确认→重试命中首次）；宿主级重发 POST → 200 duplicate 首次快照 | L1＋L3 |
| 进程恢复 | **宿主进程重启**（冒烟手验）；**容器重启**（`docker restart` → 账目仍在、Outbox 待处理恢复/确认不复活，2 例）；客户端重建（新 IMongoClient，多例） | L3＋冒烟 |
| 迁移失败 | v2 注入失败 → v1 **真实回滚**（唯一索引移除＋版本归零）；死端口 → 宿主拒绝启动（fail-closed） | L3 |
| OS 级"确认点前后杀进程" | **未做**——事务提交边界＋介质级重启覆盖确认点语义；OS kill 矩阵归 G4 真机（与热更专项 §8 杀进程同批） | 后置（如实登记） |

**已知边界**：

- 事务内并发 write-conflict **不自动重试**：以未确认抛出、调用方重试（驱动可重试写不覆盖多语句事务提交冲突）。
- Outbox 容量检查与插入非原子：并发窗口可短暂越界——容量是显式承诺不是硬不变量（契约注释声明）。
- 真实 Auth/Lobby/Profile 归 G3；样例端点是样例⑤载体**非业务 API**（错误形状简化为 code+data；§5.2 正式
  `{code,messageKey,args}` 归 G3 Contracts）。
- Redis、TLS/Dockerfile/Secret Provider/编排面归 R4；`BuildHash` 占位未接（§P0-5）。

### 2026-09-30 · M0-c 持久化接缝（批一：契约面 + L1 语义段）

**范围**：按"非本机开发、不跑 Unity"约束只做纯 .NET 可验部分；L3 实存储段（Mongo 驱动装配、副本集容器夹具、进程级重启恢复报告）待具备 Mongo/容器环境后施工（批二）。
**状态判据**：批一交付 ≠ M0-c 完成——§5-5"只做 fake 存储不能证明重启恢复"，完成以 L3 重启恢复报告为准。

**交付物**：

| 文件 | 内容 |
| --- | --- |
| `Contracts/Persistence/SettlementLedger.cs` | 结算账本端口 `ISettlementLedger`：**双维度幂等**（operationId + (playerId, matchId, settlementType)，唯一索引为最终裁判，§11.2/§11.3）、首次应用 = 唯一账目 + revision CAS 推进的**同一原子边界**（CAS 冲突整笔回滚不残留）、`Duplicate` 携带**首次快照**（重复提交不重新计算）、`SettlementStoreUnavailableException` = 未确认语义（不冒充成功、重试安全） |
| `Contracts/Persistence/Outbox.cs` | 持久 Outbox 端口 `IOutboxStore`：**有界容量**（满则显式 `RejectedFull`，§11.2"任何队列必须有显式容量与清理策略"）、入队幂等（Confirmed 条目不复活不重投）、**失败保持可重试**只累加计数（§10"未提交项保持可重试状态"）；`OutboxStatus` 刻意两态——失败不落独立状态，重试语义由状态本身承载 |
| `Contracts/Persistence/SchemaMigration.cs` | 迁移契约：`IMigrationStep`（Migrate/Rollback 必须互逆、Migrate 可重复应用）、`ISchemaVersionStore`（**版本写回 = 确认点**，任意一步后进程终止、重启从最后确认版本继续）、`MigrationOutcome` 四态（失败显式报告"哪一步、回到哪版、回滚是否失败"——§9.1 不静默半迁移） |
| `Contracts/Persistence/SampleSettlementCommand.cs` | 样例⑤"简单测试命令"DTO + `Validate()` 一次报全（与 `MetaConfig.Validate` 同口径）；**非业务模型**——§5-5 不冻结库存/奖励语义 |
| `Infrastructure/Persistence/SettlementSampleUseCase.cs` | 样例命令消费者：验证 → 原子应用 → `SampleCommandResult` 五态（Accepted/DuplicateHit/Conflict/Rejected/Unconfirmed）；**非业务模块**（Auth/Lobby/Profile 归 G3；生产装配缺真实存储时拒绝启动、不悄悄退回替身） |
| `Infrastructure/Persistence/SchemaMigrationRunner.cs` | 迁移执行器：步骤表连续性校验（缺口/重复/降级/越界全部显式拒绝）→ 逐版本应用每步写回 → 失败**逆序回滚含失败步本身**并显式报告；全程经端口注入，无墙钟/无 IO |
| `Tests/MetaServer.Tests/`（4 文件） | `PersistenceTestDoubles.cs`（外持状态替身＝"介质比进程活得久"的 L1 形态；故障注入 `FailBeforeCommit`/`FailAfterCommit`）+ 三组契约测试 28 例（SettlementPersistence 11 / OutboxPersistence 7 / SchemaMigration 10，Trait=Contract） |

**登记面**：零新工程、零 csproj——`Contracts/`、`Infrastructure/` 落盘即自动受 R11 扫（M0-b 已预留）；`Tests.slnx`、`.gitignore`、`ScanTargets.cs` 本批零改动。

**过程性记录（如实）**：

1. **CAS 冲突测试场景首建模错误**（首跑 2 例红暴露）：冲突笔误用同业务键（match-1/settle）——同键换操作号按契约属**重复提交**（上游重复签发），不属冲突。改为"同账号、不同 match、修订号过期"。该修正同时把"幂等判定先于 CAS"的优先序钉进了测试。
2. **弱断言自纠**：首批 9 处 `Assert.Equal(1/0, Count)`（xUnit2013）已全部改 `Single/Empty`——意图表达明确。既有 `MetaConfigTests` 3 处 xUnit1031 警告非本批引入，未动。

**验证证据**（本机为**非原开发机**：zip 快照、无 .git；dotnet SDK 10.0.301 编 net8.0——`global.json` `latestMajor` 允许；本机执行策略拦 ps1 → 以 `-ExecutionPolicy Bypass` 运行）：

| 门禁 | 结果 |
| --- | --- |
| L1 全量 | **882 通过 / 1 失败**：LiteFramework.Core.Tests **523**（含纪律扫描——MetaServer 新增 `Contracts/`、`Infrastructure/` **首次受扫，R11 零违规**）、LiteNet.Tests 159/160、LiteSim.Core.Tests **147**、LiteTesting.Core.Tests **7**、MetaServer.Tests **46**（18 既有 + **28 本批**） |
| L3 全量 | **93 通过 / 0 失败**（LiteNet.Tests 87 + MetaServer.Tests 6；本批只加 L1 用例，跑 L3 是为确认改了测试工程本身后既有集成用例零回归） |

**1 例 L1 失败为快照既有问题，与本批无关**（证据链，待原机裁决）：
`BuildHash_与当前源码复算一致_未忘记重跑生成器` 红——常量 `55b99f87388c9b13` vs 快照复算 `0ccf063313acf873`。① python 生成器（`gen-build-hash.py`）只读复算同得 `0ccf…`——跨语言口径一致、算法无漂移，快照内容自洽；② 本批 10 个新文件全部在该 53 文件闭包之外（`MetaServer/`、`Tests/`）；③ 文件时间戳全部为解压时刻（zip 重置），无法定位差异文件。**判定：快照提交内容与其常量不同步**（原机常量生成自含未提交生成物的工作树，或常量重生成后的文件未全量推送）。**未擅动**——buildHash 是跨端协议门禁，本机重生成会把快照现状铸成新门禁值、与原机 `55b9…` 分叉。

**未完成（批二，待 Mongo/容器环境）**：

- **MongoDB.Driver 引入裁决**：服务端第一个真 NuGet（M0-a 的零 `PackageReference` 只约束 Web 包，§4.1 论证范围；§14 已点名 Mongo 测试容器）——契约零驱动依赖的分层已就位，驱动只落 `Infrastructure/`，走 C0-① 依赖治理/许可扫描登记。
- **R11 豁免裁决**：真实 IO 落 `Infrastructure/` 时需明确豁免范围——`ScanTargets.cs` 两处注释说法不一（`MetaPurityRules` 注释称"宿主装配层合法使用 Web/IO 故排除在外"，`MetaHostExcludes` 实际只排 `Host/`）。本批以"Infrastructure 保持 R11-clean"回避了该冲突。
- 真 Mongo 适配（结算账本两个唯一索引 + 事务、Outbox 持久实现、真实迁移步骤）＋ **单节点副本集**容器夹具（§8.1 副本集模式——事务在单机模式不可用，事务重试用例跑不了）。
- L3 用例与接线：真实存储确认/重复提交/提交前后**进程级终止**/重启恢复/迁移失败矩阵；`/ready` 判定接真依赖（Mongo 可达、Outbox 未超阈，§10）；样例命令的 HTTP 入口接线（缺真实存储时拒绝启动）。
- 完成后形成**重启恢复报告**，M0-c 方可标已完成。

### 2026-09-26 · M0-d Join 票据验证接缝交付

**目标**（§5-4"没有真实登录业务时也不能省略运行时的票据验证接口与非法票据测试"）：
接口形状由设计钉死为 `IJoinTicketValidator.Validate(string ticket, JoinContext)`（服务端总设计 §7），
故失败不走异常也不走 bool，而是返回 `JoinPrincipal` 带 `JoinTicketRejection` 分类。

**交付物**（全部在 `RoomServer/Application/`，R11 纯化边界之外）：

| 文件 | 内容 |
| --- | --- |
| `JoinTicket.cs` | 接缝：`IJoinTicketValidator` / `JoinPrincipal` / `JoinContext` / `JoinTicketKey` / `JoinTicketRejection`（11 分类） |
| `JoinTicketFormat.cs` | **线格式单一来源**：字段序、Base64Url 编码、被签名覆盖的范围。签发端与验证端共用 |
| `JoinTicketValidator.cs` | `HmacJoinTicketValidator`：验签 + 六项绑定 + 有界 nonce 重放窗口 |
| `ServerHost.HandleJoin` | **接入真实准入路径**：装配验证器后 token 逐一验签，非空不再构成准入理由 |

**三处设计要点（都是被"测试要测它声称测的那件事"逼出来的）**：

1. **线格式单一来源**。测试签发器 `TestTicketIssuer` 与验证器共用 `JoinTicketFormat`——
   若测试侧另写一份拼串，签发端漂移时验证端仍然全绿。这与工程既有的"协议单源红线"是同一条纪律。
2. **重放判定排在最后**（④时效 → ⑤绑定 → ⑥重放）。若提前，攻击者可用乱签票据耗尽 nonce 窗口
   = 拒绝服务。`重放判定在验签之后_伪造签名不占用nonce窗口` 用**同一 nonce** 先坏签名后真签名钉住顺序。
3. **nonce 窗口满时拒绝新票据，不淘汰旧条目**。淘汰等于把已用过的 nonce 放出窗口 = 重开重放口子。
   安全 > 可用；`RejectedWindowFull` 计数 > 0 表示宿主未及时 `PurgeExpiredNonces`。

**一轮实测踩坑（记录以免重犯）**：准入用例最初 12/34 失败——`Ctx()` 的受众缺省值写成了 `"any"`，
而签发器缺省是 `"test"`，于是**全组用例都先撞 AudienceMismatch**；又因受众校验**排在房间/哈希/版本之前**，
把后三者的失配一并掩盖了。两个教训：绑定项判定的**顺序**决定了失配时的可诊断性；
跨文件缺省值不一致时，症状会出现在与原因无关的地方。

**验证证据**：

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| L3 | `scripts/test.ps1 -Lane L3` | **57 通过 / 0 失败**（LiteNet.Tests 由 9 → **51**，+42 即本批；MetaServer 6） |
| L1 | `scripts/test.ps1 -Lane L1` | **761 通过 / 0 失败**（本批用例标 `Integration`，按分层归 **L3** 不占 L1） |

**未完成**：Meta 侧签发端（G3）；非对称验签（R2 可选）；~~重连票据 CSPRNG 化（R2）~~ 已于 2026-09-30 交付（[服务端R2安全](服务端R2安全.md)）；~~远端限流~~ 已交付（同上），~~**安全信封仍缺（另立专项）**~~ **批1 已于 2026-10-03 交付（[安全信封](安全信封.md)）；批2 运维接缝随 R4**。

### 2026-09-25 · 宿主骨架交付（含三处实测缺陷修正）

**① 工程与宿主（M0-a）**

- `MetaServer/MetaServer.csproj`：`Microsoft.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，**零 `PackageReference`**（实测 `project.assets.json` 的 `libs: 0`）；`net8.0` / `LangVersion 10.0` / `Nullable disable` / `ImplicitUsings disable`，与 `RoomServer` 同档除语法版本。
- `MetaServer/Host/MetaConfig.cs`：宿主装配参数（BindAddress / MaxInboundBytes / ShutdownTimeoutSeconds）+ `Validate()` 返回全部违规项。
- `MetaServer/Host/MetaHost.cs`：`Build()` 单一装配路径（测试与生产共用）、`WireGracefulShutdown()`、健康端点、请求计数中间件。
- `MetaServer/Host/Ops.cs`：进程级计数 + Prometheus 文本最小出口（`/metrics`）。
- `MetaServer/Host/BuildHash.cs`：`"meta-0.0.0-unwired"` **显式占位**——`gen-build-hash.py` 接线随 R2/G3，不伪造权威哈希（§20 完成定义第 1 条）。
- `MetaServer/Host/Program.cs`：`Build()` 与 `StartAsync()` 同置于捕获 `OptionsValidationException` 的 try 内；非法配置打印全部违规项并**返回退出码 2**。

**② 语法档位裁决修正（文档同步）**

`LangVersion` 取 **10.0** 而**非** `RoomServer` 的 9.0。2026-09-25 实测：C# 9 下最小 API 惯用写法全部编译失败（`CS1593` target-typed lambda / `CS1503` 方法组 / `CS0030`），只有逐端点显式委托转换可用。该实测**证伪**了《Meta 服务专项设计》§1.1 原写的"与 RoomServer 同档"，已按总设计 §0 规则 2 更新文档。

**③ 三处静默失效缺陷：实测发现并修正**

本批首版"看起来"符合 §10/§12，实测后确认三处**不抛异常、不报警告**的失效，均违反设计：

| # | 缺陷 | 实测证据 | 违反条款 |
|---|---|---|---|
| 1 | `ValidateOnStart` 形同虚设 | 探针：`MaxInboundBytes=10`（越下限）下宿主**启动成功** | §10/§12"不能带默认错配置继续运行" |
| 2 | 配置源全部静默失效 | 字段形态类经 `ConfigurationBinder` **完全不绑定**；文件/环境变量/命令行覆盖一律读不到 | §10"版本化文件、环境变量" |
| 3 | 入站上限未落地 | `MaxInboundBytes` 只是配置对象里的数，**从未限制请求体** | §P0-3"包体在解析前限制长度" |

根因与修正：

- **实例身份**：`AddSingleton(config)` 与 `AddOptions<MetaConfig>()` 解析出**两个不同实例**，`IValidateOptions` 校验的是没人使用的那个。改为配置**只由 Options 管线持有一份**，消费方一律经 `IOptions<MetaConfig>`。
- **绑定形态**：`ConfigurationBinder` **只绑定属性**，`RoomConfig` 式的 public 字段会让配置源完全不生效。`MetaConfig` 改用**属性**——这是与 `RoomConfig` 的**有意差异**，已在两处注释说明。
- **限制落地**：`MaxInboundBytes` 接入 `KestrelServerOptions.limits.MaxRequestBodySize` 与 `FormOptions.MultipartBodyLengthLimit`；`ShutdownTimeoutSeconds` 接入 `HostOptions.ShutdownTimeout`（原配置项未接线，属装饰）。
- **删除手写预校验**：首版在 `Program` 里手写了一次 `config.Validate()` 兜底——它恰好掩盖了缺陷 1，且只看命令行覆盖、看不到配置文件。已移除，改用设计要求的 `ValidateOnStart` 作为唯一门禁。
- **`HttpRejected` 实装**：原字段有注释无写入（恒为 0），现按响应状态码（≥400）记账。

装配契约（三个陷阱的规避方式）已写入《Meta 服务专项设计》§10，回归钉落在 `Tests/MetaServer.Tests/MetaConfigTests.cs`。

**④ 接缝登记（M0-b）**

- `.gitignore`：补 `!MetaServer/*.csproj` 与 `!Tests/MetaServer.Tests/*.csproj`。**不加则工程文件被 `*.csproj` 全局忽略、交付静默丢失**。
- `Tests/Tests.slnx`：追加 `MetaServer` 与 `MetaServer.Tests`。slnx 内的非测试工程**必须**被某测试工程 `ProjectReference`，否则 `test.ps1` 的 no-restore 构建失败——已由测试工程引用满足。
- `Assets/Tools/DisciplineScanner/Scripts/ScanTargets.cs`：新增 `MetaServer` 扫描目标（`MetaPurityRules` = R11），排除 `MetaServer/Host`（宿主装配层合法使用 Web/IO/Console，§12）。**`Contracts/`、`Modules/`、`Infrastructure/` 落盘即自动受扫**，无需再改配置。
- `Tests/LiteFramework.Core.Tests/DisciplineScannerTests.cs`：补登记意图用例（`纪律_MetaServer_登记为受守根_仅宿主层豁免`）。
- 首次扫描命中 `MetaServer/` 根下文件——根因是文件未按 §4.2 结构落位，已迁入 `Host/`。

## 测试面

- `Tests/MetaServer.Tests/`（命名必须匹配 `*.Tests.csproj`，否则被 `test.ps1` 静默漏跑）。
- **零依赖 HTTP 集成测试**（§4.1 依据 5）：`WebHost` 绑定 `http://127.0.0.1:0` 取临时端口 → `IServerAddressesFeature` 取回真实地址 → `HttpClient` 打真实回环。**不用 `WebApplicationFactory`/`TestServer`**（两者都要 `PackageReference`，与 §4.1 零 NuGet 约束冲突）。
- L1（18 例）：配置范围校验边界、`覆盖项_经IOptions生效`、`入站上限_落到Kestrel限制`、`关闭时限_落到HostOptions`、`非法配置_拒绝启动`。
- L3（6 例）：存活/就绪分离、drain 后 live 仍 200 而 ready 转 503、指标出口无高基数字段、请求/拒绝计数、未知路由 404、多实例端口隔离。

## 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **630 通过 / 0 失败**（含 MetaServer 18；纪律扫描"全部目标零违规"绿） |
| L3 | `powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest` | **15 通过 / 0 失败**（含 MetaServer 6） |
| 非法配置 | `MetaServer.exe --bind "not-a-uri"` | 打印违规项，**退出码 2** |
| 正常路径 | `MetaServer.exe --bind http://127.0.0.1:18322` + curl | `/live`→200、`/ready`→200、`/metrics`→计数（4 请求 / 1 拒绝）、未知路由→404 |

**本次未跑 L2/Unity**：纯 .NET 新增，未触碰 `Assets/` 下 Unity 侧代码——除 `ScanTargets.cs` 与 `DisciplineScannerTests.cs`（纯 C# 工具与测试，不参与 Unity 编译）。**未跑 Player 构建**，本批不涉及。

## 已知边界

- **M0-c 已完成（2026-09-30，批一契约面＋批二真 Mongo 实存储）**：存储端口/约束/夹具/样例全部落地，重启恢复报告见批二记录（§14 矩阵逐项）。后置边界：OS 级杀进程矩阵归 G4 真机、Auth/Lobby/Profile 归 G3、Redis/编排面归 R4。
- **M0-d 已交付但范围有限**：交付的是**验证接缝 + HMAC-SHA256 参考实现 + 非法票据矩阵**，落在 RoomServer 侧。
  - **算法是共享密钥 HMAC，不是非对称签名**。§P0-6 允许"本地公钥**或共享验证器**"，框架期 Meta/RoomServer
    同信任域故取后者；换非对称只替换 `HmacJoinTicketValidator` 一个类，接口与消费者不变。
  - **密钥由部署注入**（`--ticket-key <kid>:<base64>`），仓库内**没有**密钥生成/登记工具——
    Meta 侧签发端的实现归 G3（Auth/Lobby），本批只交消费端。
  - **未装配验证器时退回原型级非空校验**：这是刻意保留的联调形态（否则全部历史用例与本地联调齐断），
    但**未验证 ≠ 已验证**——`Session.Principal` 保持 null，且启动时打印显式告警（§6"不能悄悄退回 fake"）。
    生产装配**必须**传 `--ticket-key`。
  - ~~**重连票据仍未达标**：`ReconnectService` 签发的仍是**可预测串**（非 CSPRNG），归 R2，见该文件类注释。~~
    → **已于 2026-09-30 交付**（R2 安全批①：16 B CSPRNG → base64url 不透明串，[服务端R2安全](服务端R2安全.md)）。
- `BuildHash` 为占位值，未接 `gen-build-hash.py`——按 §P0-5，该字段在接线前**不可**用作版本身份或签名。
- 生产编排面（TLS、限流分层、Secret Provider、Docker）未接，归 R4/§12。
- 本批无业务端点，`/metrics` 为最小文本出口；正式 Prometheus/OTel 出口归 R4（§11.2）。
