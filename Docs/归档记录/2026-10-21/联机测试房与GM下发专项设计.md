# 联机测试房与 GM 下发专项设计（P1 模板可选 + P2 判定面 GM）

> **定位**：回答"云上真 KCP 联机时，怎么让客户端**可选进入测试房**、并在该房**启用 GM 工具**（免死/无限弹药等）"。
> **不做的事**：不重复 GM 面板与本地服机制（归《[GM与调试工具分层设计](GM与调试工具分层专项设计.md)》）；不重复房间模板字段语义（归 [10 手册 §5](../../项目说明/10-Windows云主机部署.md)）；不裁决定价/容量。
> **冲突裁决**：与代码不一致以代码为准，并顺手修本文。

---

## 0. 为什么本地服能开、联机不能

| | 本地服 | 云上联机 |
|---|---|---|
| 权威在哪 | **同进程**（`LocalServerTransport`），客户端预测与权威是同一份状态 | 独立进程 RoomServer，客户端只连 KCP |
| GM 写口 | `TestModeOptions` → `SimTestRules.ActiveMatch`（内存单实例），改完两端立刻同值 | 客户端改的是自己的预测；权威完全不知道 |
| 结论 | 免死/弹药/爆头"开关即生效" | **必须**：①规则随房下发 ②双端同值应用 ③服务端授权 |

代码事实：`MatchTestRules`（`Assets/LiteSim/Core/Scripts/MatchTestRules.cs:16-42`：`NoDeath:19`、`InfiniteAmmo:23`、爆头覆写 `:30/:33`）只有一个消费点 `SimStep.Step(..., rules: _testRules)`（`RoomRuntime.cs:581`）；线上构房恒传 `null`（`Server/RoomServer/RoomInstance.cs:275-276`），仅本地服注入（`Assets/LiteGame/DevLocalServer/LocalServerTransport.cs:83-96`）。`SimTestRules.cs:9-10` 原文即「**在线房间级下发未做**——真 KCP 形态不提供测试房」。

---

## 1. 目标与非目标

**目标**

1. 客户端「开发」页可**指定模板建房**（`sandbox`/`standard`），普通入口仍走 `default_template`；
2. 进入测试房后可**启用判定面 GM**（首批：免死、无限弹药），**仅该房生效**；
3. GM 房**不产出结算/战绩**；规则**不进 `CombatConfigDigest`**；
4. 非授权客户端**无法**开启 GM（否则等于内置外挂）。

**非目标（本专项不做）**

- 爆头覆写（`HeadHitLineOverride`/`HeadshotRadiusOverride`）的联机下发——它按设计"不进摘要、不可重放"（`MatchTestRules.cs:25-33`），先只做 NoDeath/InfiniteAmmo；
- 服务端 bot/假人；转移房主；把 GM 房开放给非开发者。

---

## 2. P1：房间模板可选

### 2.1 数据流

```
客户端「开发 → 测试房」 → POST /lobby/create-room { …, template:"sandbox" }
   → Meta CreateRoomUseCase 校验/授权 → 票据盖章 template（新字段）
   → 房间服 Join 解析 principal.Template → OpenRoom(templateId)
   → RoomServerConfig.BuildRoomConfig(templateId != null ? templateId : null, …)
```

现状：`CreateRoomCommand` 只有 `RequestId/BuildHash/MapId/ExpectedPlayers`（`LobbyContracts.cs:148-162`）；`CreateRoomUseCase.Create` 透传（`CreateRoomUseCase.cs:83-90`）；房间服侧 `InboundJoinHandlers.cs:82-90` 取 `MapId/ExpectedPlayers` 后 `Open(roomId,…)`；`ServerHost.OpenRoom` 固定 `BuildRoomConfig(null, …)`（`ServerHost.cs:1778`，null ⇒ `_defaultTemplateId`，`RoomServerConfig.cs:242`）。

### 2.2 改动面

| 层 | 文件 | 改动 | 共享闭包？ |
|---|---|---|---|
| Meta 契约 | `LobbyContracts.cs:148` | 加 `public string Template { get; set; }` | 否（Meta 私有） |
| Meta 用例 | `CreateRoomUseCase.cs:71-90` | 校验模板名（`^[a-z0-9_-]{1,32}$`）+ 授权判定 | 否 |
| Meta 盖章 | `JoinTicketIssueUseCase.cs:78-114`、`LobbyTicketSigner.cs:52` | 票据带 template | 否 |
| 票据格式 | `Assets/RoomServer/Application/JoinTicketFormat.cs` | **v3 → v4**：`PartCount 15→16`、`Fields` 加段、`BuildPrefix` 覆盖新段、`ReadFields` 解析 | **否**（仅 Meta 源链接 `META_EXTRA_FILES`；客户端票据不透明） |
| 票据验签 | `JoinTicket.cs:86-132`（`JoinPrincipal`） | 出 `Template` 字段 | 否 |
| 房间服 | `InboundJoinHandlers.cs:82-90`、`ServerHost.cs:1762/1778`、`IRoomGateway.Open` | 带上 template，非空且模板不存在 ⇒ 拒绝进房（复用 `RoomServerConfig.HasTemplate:275`） | 否（`Server/`） |
| 客户端 | 开发页（Lua）+ `MetaContracts.cs` CreateRoom 调用 | 加「测试房」入口，请求带 `template`；**这是唯一需要重打包的改动** | 是（客户端代码） |

**关键结论**：票据加字段**不触发** `gen-build-hash.py` 的共享闭包（`SHARED_TARGETS` 不含 `Assets/RoomServer`），但**版本 v3→v4 必须两端同批发布**（旧票据一律被 `Split` 拒），且客户端 UI 改动仍需一次重打包。

### 2.3 失败语义

| 情况 | 结果 |
|---|---|
| 模板名非法（字符/长度） | `400 invalid-request` |
| 模板名在房间服不存在 | 拒绝进房（`lobby.version-mismatch` 同级的显式拒绝，不静默回落 default） |
| 未授权客户端请求 `sandbox` | `403`，并按"越权建测试房"记审计 |
| 请求空 template | 与现状一致（走 `default_template`） |

---

## 3. P2：判定面 GM 下发

### 3.1 规则集与生效路径

```
建房：票据 gm=true + rules{noDeath,infiniteAmmo} → RoomInstance 构造传 MatchTestRules
运行期：客户端 GM 面板 → 上行 GM 命令 → 权威循环 SetTestRules → 广播 rules 帧 → 双端同值
```

- 服务端注入缝已存在：`RoomRuntime` 构造参数 `testRules`（`RoomRuntime.cs:122-136`），线上只是恒传 null（`RoomInstance.cs:275-276`）。
- **运行期变更需要 `RoomRuntime` 暴露受控写口**（`Assets/RoomServer/Runtime/RoomRuntime.cs` 属**共享闭包**）⇒ 改它必然 `buildHash` 变化 ⇒ 客户端重打包。P2 的重打包不可避免（客户端还要应用规则）。
- 双端一致是硬要求：免死只改服务端会导致"客户端预测掉血 → 权威快照回弹"。规则必须随 **StartGame/规则帧**下发，客户端 `BattleContext` 用同值初始化预测。

### 3.2 摘要口径（必须天然满足，不得额外加位）

- 现状 `FixedCombatConfig.Capture`（`FixedCombatConfig.cs:40-44`）→ `CombatConfigDigest.WithTestRules(gameplayDigestBase, values.NoDeath, values.InfiniteAmmo)`（`CombatConfigDigest.cs:83-95`）——摘要里的两位来自 **`CombatValues`**，不是 `MatchTestRules`。
- 因此红线：**GM 规则只落 `MatchTestRules` 运行态，绝不写回 `CombatValues`**（对照 `LocalServerTransport.cs:88-96` 的本地做法——那是把规则位盖进 `CombatValues`，联机形态禁止）。
- 反向推论：若将来要"用摘要区分 GM 房身份"，必须 `CombatConfigDigest.cs:83/87` + 客户端 `BattleContext.cs:259` 同批升级——本专项不做。

### 3.3 结算排除

- 结算在 `RoomRuntime.Finish` 生成（`RoomRuntime.cs:472-491`），宿主入盒 `ServerHost.cs:1218-1246`（`_settlementOutbox.Enqueue:1229`）。
- 裁决：**在 `RoomRuntime.cs:487` 之前判 GM 标记**——GM 房结束时不产出 `SettlementReadyOutput`（走 `Abort` 同族的无结算收尾 `:495-501`），客户端仍收 `MatchEnded`。
  - 理由：入盒前拦截（`ServerHost.cs:1229`）会让"房间以为已提交、宿主丢弃"，Outbox 计数与日志口径分叉；源头不产出更干净。
- GM 房战绩在 Meta 侧自然不存在（没有提交），无需新增过滤。

### 3.4 授权（三选一，本专项推荐 B）

| 方案 | 做法 | 强度 | 代价 |
|---|---|---|---|
| A 账号白名单 | Meta 配置 `dev_accounts: []`（accountId 列表），建房/开 GM 前比对 | 强（服务端唯一判定） | 每加一个开发者要改配置并重启 Meta |
| **B 测试房口令（推荐）** | Meta 配置 `gm_room_key`（进 `deploy/.env`，不入库）；`CreateRoomCommand.gmKey` 与常量时间比较，匹配才盖章 `gm=true` | 中（口令泄露=可开，但仅测试期） | 一条配置 + 客户端输入框 |
| C 票据 dev 标志 | 开发者账号先经一次"提权"端点换 dev 票据 | 强 | 多一条状态链 |

红线：**授权判定只在服务端（Meta）**；客户端"我看得到开发页"不等于"我被授权"——未授权请求一律 403 并审计。

### 3.5 失效与回滚

- 规则随房间生命周期存在，**房间关闭即消失**，不落盘、不重启保留；
- 换模板/关 GM：GM 端点 `room/abort` 收房后重建（本专项不做"运行期把 GM 房转普通房"）；
- 进程重启：所有 GM 房自然消失（无持久化）。

---

## 4. 协议与版本清单

| 变更 | 是否改共享闭包 | buildHash | 客户端重打包 |
|---|---|---|---|
| 票据 v3→v4（template/gm 段） | 否（`META_EXTRA_FILES`） | 不变 | 否（票据不透明） |
| `CreateRoomCommand.template/gmKey` | 否（Meta 私有） | 不变 | 否 |
| 客户端开发页入口 + CreateRoom 带参 | **是**（客户端代码） | **变**（客户端常量不同步） | **是** |
| P2 `RoomRuntime.SetTestRules` + 规则帧 | **是**（`Assets/RoomServer/Runtime`、proto） | **变** | **是** |

⇒ 实施建议：**P1 与 P2 合并成一次客户端发版**（反正都要重打包），但服务端可分两次上（P1 先可用）。

---

## 5. 验收

| 项 | 判据 |
|---|---|
| 模板可选 | 客户端「测试房」建房后 `gm.py --action rooms` 看到的房；服务端日志横幅模板名正确 |
| 默认不变 | 普通入口建房仍走 `default_template`（不指定 template 时行为零变化） |
| 越权拒绝 | 无口令/错口令请求 `template=sandbox` ⇒ 403，且房间未创建 |
| GM 生效 | 免死房内伤害不扣血；**客户端预测不出现"掉血回弹"**（双端同值） |
| 摘要不变 | GM 房开局不拒；`CombatConfigDigest` 与普通房同值（规则不进摘要） |
| 结算排除 | GM 房打完/收房后 Meta `GET /matches` 无该局、Outbox 无条目 |
| 非 GM 房不受影响 | 普通房伤害/弹药/结算全链回归（L1 + 一局实测） |
| 失效 | 房间关闭后重进普通房，GM 状态不残留 |

---

## 6. 分期与依赖

| 期 | 内容 | 依赖 |
|---|---|---|
| **P1** | 模板可选（Meta 契约/票据 v4/房间服按票据选模板/客户端开发页入口） | 无（可先上，普通房零变化） |
| **P2a** | 建房时注入 GM 规则（不可运行期改） | P1 的 `gm` 标志 + 授权 |
| **P2b** | 运行期开关（GM 面板 ↔ 权威规则帧） | P2a + `RoomRuntime` 写口 + proto 规则帧 |

> P2b 是"体验完整"的那一步；P2a 只解决"进房即免死"，不改运行期。

---

## 7. 风险与挂账

1. **预测一致性**：NoDeath 若不随规则帧下发，客户端会持续"掉血回弹"——P2b 的验收项，不可省。
2. **口令泄露**：B 方案是测试期手段；长期应转 A（账号白名单）。
3. **票据版本硬切**：v3→v4 期间旧客户端/旧房间服互拒——Meta 与房间服必须同批发布（客户端不感知，但要在发布顺序里写清）。
4. **爆头覆写**：仍留在内环（不进摘要、不可重放，联机下发会破坏调参语义）。
5. **GM 房与大厅**：GM 房是否对其他测试者可见（大厅列表会列出来）——本专项默认**可见但不标注**，若要隐藏需给 `RoomReport` 加标志（改动面 +1，单独立项）。
