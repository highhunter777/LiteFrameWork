# 云端 GM 通道专项设计（房间服运维端点）

> **定位**：回答"云上联机测试（真 KCP 独立进程房间服）缺 GM/调试手段时，运维侧怎么在不改客户端的前提下对房间下手"。
> **不做的事**：不做判定面 GM（免死/无限子弹/爆头覆写）——**单独立批**，见 §6；不解释 GM 面板分层（归《GM与调试工具分层设计》）；不重复上云部署步骤（归 [10-Windows 云主机部署](../../项目说明/10-Windows云主机部署.md)）。
> **冲突裁决**：与代码不一致以代码为准，并顺手修本文。

---

## 0. 背景与问题

- 客户端 GM 能力（`MatchTestRules` / `SimTestRules`：免死、无限子弹、爆头覆写、bot 补位）依赖"客户端预测与进程内服务器**同进程同实例**"；真 KCP 独立进程房间服下够不到权威侧（《GM与调试工具分层设计》§6 第 3 条）。
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
- **Worker 执行形态（生产装配）下执行 owner 是 Worker**：宿主线程不得旁路直驱 `RoomRuntime.Execute`——`SubmitCommandTo` 的同步兜底只在宿主 owner 形态成立，worker 形态入盒失败即如实报错（未入盒 = 未执行）。规则类动作（`room/rules`）不新增 `RoomCommandKind`，经 `RoomInstance` 待办交接给归属 Worker，在驱动开头落地（判定与快照因此看到同一份规则）；
- 读类动作（`rooms/list`）可在 HTTP 线程直接读 `RoomSummaries()`——它与 Lobby 心跳同族的"近似快照"口径（字段级读、撕裂由 catch 兜底），不进权威循环。

---

## 3. 动作集（本批）

| 动作（HTTP 路径） | 实现路径 | 语义 |
|---|---|---|
| `/gm/rooms` | `ServerHost.RoomSummaries(includeHidden: true)` | 房间号/阶段/在座/期望/地图（近似快照；含隐藏房） |
| `/gm/start` | `RoomCommand.StartEarly(ownerPlayerId)` | 不等满员强制开局。**发起席位必须是 owner**（内核校验），无 owner ⇒ 409 |
| `/gm/abort` | `RoomCommand.Shutdown(ShutdownReason.Operator)` | 与进程排空同口径的运维终止 |
| `/gm/kick` | `RoomCommand.Disconnect(connectionId)` + 传输层断连 | 踢席位（按 `player` 找会话连接） |
| `/gm/rules` | `MatchTestRules` 规则位（见 §6） | 测试房判定面开关（1=免死 / 2=无限弹药 / 0=关）；只对隐藏测试房开放 |
| `/gm/settle-flush` | `ServerHost.FlushSettlementOutbox()` | 立即回灌待提交结算（验结算链用） |

**不做的动作与原因**：

- **转移房主**：需新增 `RoomCommandKind`，而 `Assets/RoomServer/Runtime/RoomCommand.cs` 属**双编共享源集** ⇒ BuildHash 变化 ⇒ **客户端必须重打包**，与"客户端零改动"冲突。替代：要换人就 `kick` 当前 owner（下一席位按既有规则接任）。
- **判定面 GM**（免死/弹药/爆头）：见 §6，单独立批。

---

## 4. 门禁（四件套，缺一不可）

1. **默认关闭**：`gm_port=0` 不创建监听器（与 `metrics_port` 同形态，行为零变化）。
2. **绑定面**：只绑隧道地址 `10.8.0.1`；装载期拒绝通配地址（§1）。
3. **密钥**：`LITENET_GM_KEY` 空 ⇒ 启动失败/降级并告警；比较用 `FixedTimeEquals`；**不进配置文件**。
4. **审计**：每个动作一行 `Console.WriteLine("[GM] action=… room=… arg=… result=…")`，落到 `C:\litegame\room\out.log`。

调用面：从开发机走 SSH 隧道（`ssh -L 9101:10.8.0.1:9101 litegame-cloud`）再 `curl`；后续可接进 `scripts/deploy/gm.ps1`。

---

## 5. 结算与战绩口径

- `room/abort` 沿用既有 `Shutdown(Operator)` 语义（与进程排空同源），**不是**"无痕清房"：它按既有路径走完 Finishing/Settling。若要不留战绩地清场，先 `kick` 全部席位（走 `AllPlayersLeft` 路径）。
- `room/start` 提前开局本身不改变结算口径；`sandbox`（不限时/无击杀上限）房间**不会自然结束** ⇒ 不会有结算产出（同 10 手册 §5 的既有代价说明）。
- 判定面 GM 一旦落地（§6），其房间**必须被结算面标记并排除**（不进战绩/统计）——该条属下一批的验收项，本批不引入。

---

## 6. 判定面 GM（测试房限定形态）

- 形态：服务端侧注入（客户端下发 = 给客户端开挂接口，且《GM与调试工具分层设计》§3 明确判定面覆写"不进摘要、不可重放"）——`/gm/rules` 动作，**只对带模板建的测试房**（不进大厅列表的隐藏房）生效，普通房一律 409。
- 范围：免死 + 无限弹药两个规则位（`MatchTestRules.GmFlags`；随快照下发，客户端据此同值预测）。
- 红线：① 仅测试房生效（判据与大厅隐藏过滤同源）；② **不进 `CombatConfigDigest` / 联机身份摘要**；③ 该房**结算被拒**（跳过结算入盒，客户端仍收到终局 `MatchEnded`）；④ 不落盘，进程重启即失效。
- 落地路径：宿主 owner 形态在权威循环内直接替换规则引用；**Worker 执行形态经 `RoomInstance` 待办交接**，由归属 Worker 在驱动开头落地（判定与快照读到同一份规则）——不新增 `RoomCommandKind`，BuildHash 与线格式不变。

---

## 7. 验收（本批）

| 项 | 判据 |
|---|---|
| 默认关闭 | `gm_port=0` 时无监听、无端口占用（行为零变化） |
| 未授权 | 无/错 `X-GM-Key` ⇒ 401，且**不执行**动作（L1 断言） |
| 密钥空 | `LITENET_GM_KEY` 未设 ⇒ 端点不启动 + 告警，宿主继续运行 |
| 通配绑定 | `gm_bind=0.0.0.0` ⇒ 装载期 `InvalidDataException` |
| 队列语义 | HTTP 只入队；`Pump` 后才生效（L1：入队 → `PumpUntil` → 断言效果） |
| `room/start` | 单人房间由 owner 触发即开局（无人数下限）；无 owner ⇒ 409 |
| `room/kick` | `FakeRoomTransport.Disconnects` 出现该连接 |
| `room/settle-flush` | 待提交计数归零（假提交器断言收到提交） |
| 回归 | 既有 L1 全绿；**BuildHash 不变**（未碰共享源集）⇒ 客户端不需重打包 |

---

## 8. 挂账

1. 转移房主（§3，需改共享源集，代价与"客户端零改动"冲突）。
2. 服务端 bot/补位假人（云上无人陪练；当前只有真人客户端与站桩缺位）。
3. GM 面板「云侧页」（编辑器内直连云上 GM 端点，免去 SSH 隧道）——等本批端点稳定后再加。
4. DevHUD：需开 `developmentBuild` 重出包，属独立批次。
