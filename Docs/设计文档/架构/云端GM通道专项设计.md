# 云端 GM 通道专项设计（房间服运维端点 ＋ 云端测试房）

> **定位**：回答"云上联机测试（真 KCP 独立进程房间服）缺 GM/调试手段时，开发者怎么进隐藏测试房、运维侧怎么在不改客户端的前提下对房间下手"。
> **不做的事**：不做客户端内置控制台（归[《GM 与调试工具分层专项设计》](GM与调试工具分层专项设计.md)）；爆头覆写不下发（仍留内环）；不重复上云部署步骤（归 [10-Windows 云主机部署](../../项目说明/10-Windows云主机部署.md)）。
> **冲突裁决**：与代码不一致以代码为准，并顺手修本文。

---

## 0. 背景与问题

- 云上 GM 有两块：**进云端测试房**（模板/口令/选图，§9）与**对房间下手**（§1–§8 端点；判定面规则见 §6）。
- 客户端 GM 能力（`MatchTestRules` / `ILocalTestHooks`：免死、无限子弹、爆头覆写、bot 补位）依赖"客户端预测与进程内服务器**同进程同实例**"；真 KCP 独立进程房间服下够不到权威侧（《GM与调试工具分层设计》§6 第 3 条）。
- 服务端现状（代码事实）：除 9100 `/metrics`（**只读**）外**无任何写通道**；房间级动作 `ExecuteStartEarly` / `Abort` / `TransferOwner` 全是 private，唯一入口是 `RoomRuntime.Execute`；踢席位无现成 API。
- 云上 exe 无 DevHUD（`DevHUD.cs:7` 限定 `UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`，工程 `developmentBuild: false`）。
- **已确认的既有便利**：`RoomRuntime.ExecuteStartEarly`（`RoomRuntime.cs:285-299`）**无人数下限** ⇒ 单人即可开局，缺位是站桩实体。

---

## 1. 形态

房间服进程内新增一个**极小的 HTTP 管理端点**：

| 项 | 值 |
|---|---|
| 端口 | `gm_port`（配置，**缺省 0 = 关闭**）；云上取 `9101` |
| 绑定 | `gm_bind`（配置，缺省 `127.0.0.1`；云上取 `10.8.0.1`）。**装载期拒绝 `0.0.0.0` / `::` / 空**——公网暴露即配置错误，fail-fast |
| 方法 | 仅 `POST`（GET 一律 404，避免被顺手 curl/爬虫探到） |
| 鉴权 | Header `X-GM-Key`，与环境变量 `LITENET_GM_KEY` **恒定时间比较**（复用 `SecureEnvelopeCrypto.FixedTimeEquals`）；**密钥空 ⇒ 拒绝启动本端点**（fail-closed） |
| 载荷 | `application/json`，`{"room":"<roomId>","player":<n>}` 一类最小字段 |
| 响应 | `200 {"ok":true,...}` / `401` 未授权 / `404` 未知动作 / `409` 前置不满足（房间不存在、无 owner 等） |

密钥**只走环境变量**，不进 `roomserver.json`、不进仓库、不回显（同《上云测试专项设计》§5 密钥红线）。

---

## 2. 线程模型（硬约束）

`SubmitCommandTo` 在 `_mailboxRouting == false` 时会**同步** `ExecuteCommandTo`——若在 HTTP 线程直接调用，等于外部线程改权威状态。故：

- HTTP 线程**只做**：鉴权 → 解析 → **入队**（`GmActionQueue`：锁 + 列表，照抄 `PendingDisconnectQueue` 范式）→ 立即返回 `200 {"ok":true,"queued":true}`；
- `ServerHost.Pump()`（权威循环）中 `Flush` 队列，动作在权威循环内落地，再经既有 `SubmitCommandTo` 投递到房间（Mailbox 路由开启时走 `EnqueueControl`，其 `lock` 保证跨线程安全）；
- **Worker 执行形态（生产装配）下执行 owner 是 Worker**：宿主线程不得旁路直驱 `RoomRuntime.Execute`——`SubmitCommandTo` 的同步兜底只在宿主 owner 形态成立，worker 形态入盒失败即如实报错（未入盒 = 未执行）。规则类动作（`/gm/rules`）不新增 `RoomCommandKind`，经 `RoomInstance` 待办交接给归属 Worker，在驱动开头落地（判定与快照因此看到同一份规则）；
- 读类动作（`rooms/list`）可在 HTTP 线程直接读 `RoomSummaries()`——它与 Lobby 心跳同族的"近似快照"口径（字段级读、撕裂由 catch 兜底），不进权威循环。

---

## 3. 动作集

| 动作（HTTP 路径） | 实现路径 | 语义 |
|---|---|---|
| `/gm/rooms` | `ServerHost.RoomSummaries(includeHidden: true)` | 房间号/阶段/在座/期望/地图（近似快照；含隐藏房） |
| `/gm/start` | `RoomCommand.StartEarly(ownerPlayerId)` | 不等满员强制开局。**发起席位必须是 owner**（内核校验），无 owner ⇒ 409 |
| `/gm/abort` | `RoomCommand.Shutdown(ShutdownReason.Operator)` | 与进程排空同口径的运维终止 |
| `/gm/kick` | `RoomCommand.Disconnect(connectionId)` + 传输层断连 | 踢席位（按 `player` 找会话连接） |
| `/gm/rules` | `MatchTestRules` 规则位（见 §6） | 云端测试房判定面开关（1=免死 / 2=无限弹药 / 0=关）；只对隐藏测试房开放 |
| `/gm/settle-flush` | `ServerHost.FlushSettlementOutbox()` | 立即回灌待提交结算（验结算链用） |
| `/gm/diag-list` | `RoomDiagnosticsService.ListFileSummaries()` | 诊断目录文件清单（名/字节/修改时刻）；诊断未启用 ⇒ 404 `{"ok":false,"error":"diagnostics disabled"}` |
| `/gm/diag-fetch` | `RoomDiagnosticsService.ResolveFile(name)` + 文件流 | 请求体 `{"file":"<目录直下文件名>"}`；命中回传 `application/octet-stream`（非 JSON），回传上限 64 MB ⇒ 413。**文件名即能力边界**：分隔符/`..`/非纯文件名 ⇒ 400，不存在 ⇒ 404 |

**诊断下载的安全口径**：读面只暴露诊断目录**直下**文件（`ResolveFile` 拒绝一切路径形态）；GET 依旧 404；响应大小上限独立于存储上限（存储保留策略归 `RoomRunStore`）。

**不做的动作与原因**：

- **转移房主**：需新增 `RoomCommandKind`，而 `Assets/RoomServer/Core/RoomCommand.cs` 属**双编共享源集** ⇒ BuildHash 变化 ⇒ **客户端必须重打包**，与"客户端零改动"冲突。替代：要换人就 `kick` 当前 owner（下一席位按既有规则接任）。
- **判定面 GM**（免死/弹药/爆头）：见 §6，单独立批。

### 3.1 契约单源（机器可读）

动作集与请求/响应字段的唯一出处是 [`Server/RoomServer/gm-contract.json`](../../../Server/RoomServer/gm-contract.json)（schema v1：`transport` / `status` / `actions[]` / `success`）。消费面：

- **CLI** `scripts/deploy/gm.py`：动作表、必填字段、请求体与路径全部从契约派生（`scripts/_gm_contract.py`），不再维护第二份 `ACTIONS`；`--help` 之外的无参用法行也由契约动作名生成。
- **服务端** `GmHttpListener.cs`：路由 switch 保持不变；L0（`l0-dep-scan.py`）静态扫描该文件的 `"/gm/<动作>"` 字面量并与契约对账。
- **编辑器页** `Assets/LiteGame/Editor/Gm/*.cs`：页内出现的 `/gm/<动作>` 必须在契约中声明（当前只有 `rules`），否则 L0 报漂移。
- **测试** `scripts/tests/test_gm_contract.py`：契约 ↔ 服务端路由 ↔ 编辑器页 ↔ CLI 四方一致；`tests/test_toolchain_domains.py` 之外不另立动作清单。

新增/修改动作的顺序：改契约 → 改 `GmHttpListener` 路由与字段解析 → 改编辑器页（如需）→ CLI 自动跟随 → 跑 L0 与 GM 契约测试。契约只描述协议，不复制服务端实现细节（队列/线程/房间状态机仍在 §2/§3 正文）。

---

## 4. 门禁（四件套，缺一不可）

1. **默认关闭**：`gm_port=0` 不创建监听器（与 `metrics_port` 同形态，行为零变化）。
2. **绑定面**：只绑隧道地址 `10.8.0.1`；装载期拒绝通配地址（§1）。
3. **密钥**：`LITENET_GM_KEY` 空 ⇒ 启动失败/降级并告警；比较用 `FixedTimeEquals`；**不进配置文件**。
4. **审计**：每个动作一行 `Console.WriteLine("[GM] action=… room=… arg=… result=…")`，落到 `C:\litegame\room\out.log`。

调用面：从开发机走 SSH 隧道（`ssh -L 9101:10.8.0.1:9101 litegame-cloud`）再 `curl`；后续可接进 `scripts/deploy/gm.py`。

---

## 5. 结算与战绩口径

- `room/abort` 沿用既有 `Shutdown(Operator)` 语义（与进程排空同源），**不是**"无痕清房"：它按既有路径走完 Finishing/Settling。若要不留战绩地清场，先 `kick` 全部席位（走 `AllPlayersLeft` 路径）。
- `room/start` 提前开局本身不改变结算口径；`sandbox`（不限时/无击杀上限）房间**不会自然结束** ⇒ 不会有结算产出（同 10 手册 §5 的既有代价说明）。
- 云端测试房（判定面 GM，§6）**必须被结算面标记并排除**（不进战绩/统计）。

---

## 6. 判定面 GM（云端测试房限定形态）

- 形态：服务端侧注入（客户端下发 = 给客户端开挂接口，且《GM与调试工具分层设计》§3 明确判定面覆写"不进摘要、不可重放"）——`/gm/rules` 动作，**只对带模板建的云端测试房**（不进大厅列表的隐藏房）生效，普通房一律 409。
- 范围：免死 + 无限弹药两个规则位（`MatchTestRules.GmFlags`；随快照下发，客户端据此同值预测）。
- 红线：① 仅云端测试房生效（判据与大厅隐藏过滤同源）；② **不进 `CombatConfigDigest` / 联机身份摘要**；③ 该房**结算被拒**（跳过结算入盒，客户端仍收到终局 `MatchEnded`）；④ 不落盘，进程重启即失效。
- 落地路径：宿主 owner 形态在权威循环内直接替换规则引用；**Worker 执行形态经 `RoomInstance` 待办交接**，由归属 Worker 在驱动开头落地（判定与快照读到同一份规则）——不新增 `RoomCommandKind`，BuildHash 与线格式不变。

---

## 7. 验收

| 项 | 判据 |
|---|---|
| 默认关闭 | `gm_port=0` 时无监听、无端口占用（行为零变化） |
| 未授权 | 无/错 `X-GM-Key` ⇒ 401，且**不执行**动作（L1 断言） |
| 密钥空 | `LITENET_GM_KEY` 未设 ⇒ 端点不启动 + 告警，宿主继续运行 |
| 通配绑定 | `gm_bind=0.0.0.0` ⇒ 装载期 `InvalidDataException` |
| 队列语义 | HTTP 只入队；`Pump` 后才生效（L1：入队 → `PumpUntil` → 断言效果） |
| 模板可选 | GM 页「本地测试模式 → 云端测试房」选择地图并指定 `template=sandbox` 建房成功；服务端启动横幅模板名正确 |
| 默认不变 | 普通入口不指定 template 走 `default_template=standard`（正常结算）；测试房显式 `sandbox` |
| 越权拒绝 | 无/错口令请求 `template=sandbox` ⇒ 403，且房间未创建 |
| 摘要不变 | 云端测试房开局不拒；`CombatConfigDigest` 与普通房同值（规则不进摘要） |
| 结算排除 | 规则房打完/收房后 Meta 无该局、Outbox 无条目 |
| `room/start` | 单人房间由 owner 触发即开局（无人数下限）；无 owner ⇒ 409 |
| `room/kick` | `FakeRoomTransport.Disconnects` 出现该连接 |
| `room/settle-flush` | 待提交计数归零（假提交器断言收到提交） |
| 回归 | 既有 L1 全绿；**BuildHash 不变**（未碰共享源集）⇒ 客户端不需重打包 |

---

## 8. 挂账

1. 转移房主（§3，需改共享源集，代价与"客户端零改动"冲突）。
2. 服务端 bot/补位假人（云上无人陪练；当前只有真人客户端与站桩缺位）。
3. GM 面板「云侧页」（编辑器内直连云上 GM 端点，免去 SSH 隧道）——等端点稳定后再加。
4. DevHUD：需开 `developmentBuild` 重出包，属独立批次。
5. 爆头覆写联机下发：仍留内环（不进摘要、不可重放），不下发。
6. GM 房大厅可见性：默认可见不标注；要隐藏需给 `RoomReport` 加标志，单独立项。

---

## 9. 云端测试房：模板与授权

> 云端测试房是**隐藏房**（不进大厅列表），只能凭建房时拿到的票据进入；它是开发者专属房间，地图由 GM 页选择并随建房票据盖章，房间级规则经 §6 `/gm/rules` 下发。普通房走 `default_template=standard`，正常结算。

### 9.1 数据流

```text
GM 页「本地测试模式 → 云端测试房」选地图
  → POST /lobby/create-room { …, template:"sandbox", mapId:<选中地图> }
  → Meta CreateRoomUseCase 校验/授权 → 票据盖章 template + mapId（v4）
  → 房间服 Join 解析 principal.Template → OpenRoom(templateId)
  → RoomServerConfig.BuildRoomConfig(templateId, …)
```

普通入口不指定 template 时走 `default_template=standard`（普通房、结算）；云端测试房入口只在开发侧提供，显式带 `sandbox` + `room.gmkey`，测试者包不带口令。

### 9.2 落点

| 层 | 文件 | 责任 | 共享闭包 |
|---|---|---|---|
| Meta 契约 | `LobbyContracts.cs` | `CreateRoomCommand.Template` | 否 |
| Meta 用例 | `CreateRoomUseCase.cs` | 模板名文法校验 + 授权判定 | 否 |
| 票据 | `JoinTicketIssueUseCase.cs`、`JoinTicketFormat.cs`（v3→v4）、`JoinTicket.cs` | 票据带 `Template` 段并验签 | 否（仅 Meta 源链接） |
| 房间服 | `InboundJoinHandlers.cs`、`ServerHost.OpenRoom`、`IRoomGateway.Open` | 按票据选模板；模板不存在 ⇒ 拒绝进房 | 否 |
| 客户端 | GM 页「本地测试模式」 + `MetaContracts.cs` | 「云端测试房」入口（地图下拉 + 创建并进入） | 是（客户端代码） |

> 票据加字段不触发 BuildHash 共享闭包，但票据版本 v3→v4 **必须两端同批发布**（旧票据一律被拒）。

### 9.3 失败语义

| 情况 | 结果 |
|---|---|
| 模板名非法（字符/长度） | `400 invalid-request` |
| 模板名在房间服不存在 | 显式拒绝进房，不回落 `default_template` |
| 未授权客户端请求 `sandbox` | `403`，并按"越权建云端测试房"记审计 |
| 请求空 template | 走 `default_template=standard`（普通房，正常结算） |

### 9.4 授权

| 方案 | 做法 | 强度 | 代价 |
|---|---|---|---|
| A 账号白名单 | Meta 配置 `dev_accounts`，建房/开 GM 前比对 | 强（服务端唯一判定） | 每加开发者改配置并重启 |
| **B 云端测试房口令（推荐）** | Meta `gm_room_key`（进 `deploy/.env`，不入库）；口令恒定时间比对通过才盖章 `gm=true` | 中（口令泄露=可开，限测试期） | 一条配置；GM 页取部署注入口令，不手输 |
| C 票据 dev 标志 | 开发者先经"提权"端点换 dev 票据 | 强 | 多一条状态链 |

红线：**授权判定只在服务端（Meta）**；客户端能打开 GM 页不等于已授权，未授权一律 403 并审计。

### 9.5 与判定面 GM、结算的关系

- 建房只决定云端测试房身份；免死/无限弹药经 §6 `/gm/rules` 下发，仅隐藏测试房生效；
- 规则房结算被拒（§6 红线③），模板本身不改变结算路径；
- 爆头覆写不下发（§8 挂账 5），仍在本地内环调参。

### 9.6 协议与版本

| 变更 | 共享闭包 | BuildHash | 客户端重打包 |
|---|---|---|---|
| 票据 v3→v4（`Template` 段） | 否（`META_EXTRA_FILES`） | 不变 | 否（票据不透明） |
| `CreateRoomCommand.template/gmKey` | 否（Meta 私有） | 不变 | 否 |
| GM 页云端测试房入口 + CreateRoom 带参（含地图） | 是（客户端代码） | 变 | 是 |
