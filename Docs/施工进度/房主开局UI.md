# 房主开局 UI（HUD 批C·房主开局：Button_StartMatch 五件套 + 迁移换归属）

> 依据：《开房与房主开局》五件挂账清单（prefab 控件/显隐口径/点击接线/被拒反馈）；`UI.Room.StartMatch`/`StartDenied` 文案键已在册（表零改动）；协议与 Runtime 全备（PacketType 13/14/15、JoinAck.IsOwner、StartEarly、未满员开局）
> 范围：房主开局钮（prefab+Lua）、`Bridge.room` 第七门面（会话态）、`RoomClient.IsOwner` 协议派生态、房主迁移（续批"UI 换按钮归属"消费面）收口；**创房入口（挂账⑤ Lobby 输入房号开房）不在本批**
> 状态：**已完成（附验证证据）**——2026-10-14

## 交付物

| 项 | 落点 |
| --- | --- |
| 房主事实单源（协议态） | `RoomClient.IsOwner`（`LiteNet/Client/RoomClient.cs`）——JoinAck.IsOwner 定初值（重连重进同源刷新——迁移后旧主不夺回）；`OwnerChanged` 广播按"新房主席位 == 本机席位"重算。`BattleClient.IsOwner` 透传（LiteClient）。**UI 开钮归属的唯一事实源**——服务器侧确定性裁决，本端只消费 |
| 第七门面 `Bridge.room`（会话态） | `App/Room/RoomBridge.cs`（新）：静态门面 + `IRoomSessionFace` 端口 + `RoomSessionFace`（BattleClient 适配——IMetaClient 先例同形）。Lua 面：`IsOwner()` 拉取／`OnOwner(fn)` 订阅（**替换语义**；装订/订阅**即回放**——页面先于 JoinAck 打开的竞态由回放双序消解）／`StartMatch(cb)`（**被拒恰一次**回 false+reason；成功不回调——OnStartGame 流程接管关页；无会话显性降级 false）。会话装订/摘除归 `ProcedureBattle`（`BindSession` 随 Match Scope 建立／`UnbindSession` 在 DetachView——`SimTestRules.ActiveMatch` 同款"活会话登记"范式、随收尾必摘） |
| 能力面 | `ProductBridges.BindGlobals` 加一行；`BridgeCapabilitySet.Default()`——**ApiVersion 2→3**＋`bridge.room` 登记（七门面）。**零 xLua 重生成**：`Func<bool>`/`Action<LuaFunction>` 委托形全在 GenConfig 白名单在册 |
| 房间页钮（prefab） | `Page_Room.prefab` 新增 `Button_StartMatch`（克隆 Button_Leave 形态、`BindName=StartMatch` §9.1 同文、缺省隐藏、Leave 上方落位）。构建器 `AgentScripts/BuildRoomStartMatchButton.cs`（幂等、结构断言） |
| 房间页接线（Lua） | `Page_Room.lua`：OnShow 订阅 `Bridge.room.OnOwner`（fn 收口钮显隐——回放/迁移同路径）＋点击防重入（在途禁钮）→ 被拒 Toast `UI.Room.StartDenied`（服务端 reason 现话术直显——"只有房主可以开始对局"）＋复位；新增 **OnHide**（`OnOwner(nil)` 退订——门面持引用纪律）。**续批"OwnerChanged→UI 换按钮归属"消费面同批收口**（旧主失位隐藏/新主到位亮钮，同一订阅路径） |

## 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | **0 错误**（两轮：门面链路／测试） |
| LiteNet L1（房主位协议态） | `RoomClientOwnerTests` **3/3**：JoinAck 携位初值（真假两向）／迁移命中本机到位失位即摘／重进同源刷新不夺回（全假传输喂编码包——重连测试件同形态）；LiteNet.Tests 全量 **551/551** |
| 定向 EditMode（房主开局批 5 fixture） | **50/50**：`RoomBridge` **7/7**（回放双序/替换语义/迁移推送/无会话降级/被拒恰一次/摘除作废——真 LuaFunction 回调 + fake 端口）· `MetaPages` **19/19**（既有回归＋3 新例：房主回放亮钮-点击防重入-被拒 Toast 复位／非房主隐藏-迁移失位同路径摘钮／OnHide 退订）· `BattleHudMenu` **3/3**（新增房间页开局钮结构守卫：在位/缺省隐藏/BindName 同文）· `PageFlowBattleMenu` **4/4**（ProcedureBattle 改动回归）· `UiValidation` **14/14**（命名/表路径门禁罩新节点） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` 结果见下（提交信息） |
| 门禁往返 | 本批两处编译红往返后收敛：RoomBridge 缺 `using LiteClient`（BattleClient 不可达）；LiteNet 测试件命名空间踩坑（`PacketType/PacketCodec` 住 `LiteNet.Protocol`、消息住 `LiteNet.Proto`——限定 `Proto.X` ＋ 对照既有测试件 using 形态收敛） |

## 边界与备注（如实）

- **创房入口（挂账⑤）不在本批**——Lobby 页"输入房号开房"控件未做（服务器对未知 roomId 自动 OpenRoom 已就绪，纯 UI 面，归后续批）；
- **实机端到端未跑**（真联机房主开局全链：未满员 StartEarly、被拒话术观感、迁移换钮实况）归本机手测；L2（PlayMode 全量/夜间）未跑——本批为 C# 门面＋EditMode 定向＋prefab 结构（批C-③ 同验证面口径）；
- `s_ownerFn` 为门面持有的持久 LuaFunction：增量重填路径页面跑到关闭（旧表有效）；DevReload 重建 env 的边缘（对局中重锤）由 SafeCall 兜底＋页面 OnShow 重绑——如实登记，不为批外路径加机制；
- `StartMatch` 成功路径无回调（RoomClient 契约"被拒才到达"同构一层）——按钮在途锁至页面被关（流程接管）或被拒复位，服务端必答（runtime 响应确定性）故无悬挂面；
- 服务器 reason 为中文现话术（"只有房主可以开始对局"）直显——封闭测试口径；reason 码表化（跨语言）随后续本地化批。

## 审计登记回写

| 条目 | 处置 |
| --- | --- |
| L122 挂账"Button_StartMatch（房主开局——prefab 控件缺失）" | **已交付**（本批）——就地注记＋修复登记行 |
| 《开房与房主开局》续批"UI/客户端消费面挂账批C"（含 OwnerChanged→UI 换按钮归属） | 开局钮＋迁移换归属两消费面收口（本批）；创房入口仍挂 |
