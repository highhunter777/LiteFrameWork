# 上云批C-④：五页接线 + 文案键启用

> 依据：[《上云测试页面层级设计》](../设计文档/client/ui/上云测试页面层级设计.md) §1～§8/§10；[《上云测试专项设计》](../设计文档/architecture/上云测试专项设计.md) §2/§3 批C
> 范围：五页 Lua 接线（`Bridge.meta` 调用点 / Login→Lobby→Room→Result 跳转链 / `SetTextKey` 启用）+ `Bridge.ui` 路径寻址 + PageFlow（流程侧三处页面切换）+ 文案键表源增补 + xLua 生成代码补面
> 状态：**已完成（附验证证据）**——2026-10-13
> 不含（挂**批C-⑥：控件与列表协议接线**）：列表行数据协议（三个列表的行填充与行点击选择式进入）、控件族设值与变化通知（G15～G17；设置服务接线、语言切换入口）、Room 实时席位/等待人数、结算页 `Txt_Score`

## 交付物

| 项 | 落点 |
| --- | --- |
| 页面寻址（路径制） | `Bridge.Data.ResolveUIFormId`（lua_path → tbuiform id：表驱动反查 + 会话缓存，`ClearLuaCaches` 同清）——**magic id 不进脚本**；`Bridge.ui` 全面改按 lua_path 寻址（`Show`/`Replace`〔新增〕/`Close`/`IsOpen`/`GetLogic`），未知路径 = 错误日志 + 显性不动作（不炸 Lua 壳） |
| 流程侧页面流 | `Assets/LiteGame/App/Ui/PageFlow.cs`（新，App/Ui/）——①首进 Main 开登录页（**只一次**）；②StartGame 关房间页；③Ended 后：正常结束（Completed + 冻结结果在）开结算页（数据表 = 本机 kills/deaths + 结束原因枚举名），失败/主动离场且房间页在则替换回大厅 |
| 主/战流程挂接 | `ProcedureMain`（**配置了 `-meta.url` 才开入口页**——离线/本地开发形态零变化）、`ProcedureBattle`（开局关房 / 结束开结算页）、`ContainerModule` 构造并注入（`pageFlow` 随流程机建） |
| 联机门面补面 | `MetaBridge`：增 `Version`（buildHash，页面版本展示）/`LeaveMatch`；`QuickEnter`/`EnterRoom` 成功载荷改**换票结果的房号**（房间页展示用）；战绩行增 `reason`（枚举名——Lua 不抄枚举值）；`MetaEntryService.LeaveMatch`（请求流程机回 Main——进房的逆操作，同一窄接口同一收尾管道） |
| 五页 Lua 接线 | `Page_Login`（游客登录 → 成功替换 Lobby；失败页内重试 + Toast；未配置显性拒绝且按钮禁用）、`Page_Lobby`（ListRooms 三态 Ready/RoomEmpty/ListFailed；QuickEnter → Allocating → 替换 Room，房号随导航数据；Records/Settings 入口）、`Page_Room`（房号显示；Leave = 替换回大厅 + 请流程收尾）、`Page_Result`（本局冻结结果记分板；大厅入口回落最近一战；空表空态；BackLobby 替换）、`Page_Settings`（全标签键 + 账号/服务器/版本只读 + Close；未登录不显空标签） |
| 文案键 | `Luban/Data/#ltext.xlsx` 增 `UI.Lobby.ListFailed`（41 → 42 键）→ `Luban/gen.bat` 双端重生成（`tbltext.bytes` + `tbltext.lua`）；**tbltext 是表现层排除表 → buildHash 不变**（`--check` 双 OK 实证） |
| xLua 生成代码 | `GenConfig` 白名单补全门面导出面全部委托形（含 `Func<string,bool>`/`Action<LuaFunction>` 等）+ `XLua/Generate Code` 重生成（`DelegatesGensBridge.cs` +184/−19）——**首个真实 Lua 消费者上路前**把"未登记形只能走 Mono 反射兜底（IL2CPP 不可靠）"的隐患收口；旧 int 形（ui.Show/Close/IsOpen）随之退场 |
| 测试 | `MetaPagesEditModeTests` 12 例（真 LuaEnv + 受控面替身——无需 Unity 对象即可驱动整页逻辑并断言调用序：三页状态机/跳转语义/文案键/离开链/结算三态/设置只读 + **文案键守卫**〔页面脚本引用的每个 LText 键 ⊆ tbltext 在册，含结束原因值域〕）；`LTextWiringEditModeTests` 键数 41 → 42 同批核对 |

## 证据

| 项 | 命令/方式 | 结果 |
| --- | --- | --- |
| 重编译 | unity-pipeline `recompile` → `recompile_status` | completed / `failed:false` / `errors:[]` |
| 定向 EditMode | `run_tests --mode editor --filter MetaPagesEditModeTests` | **12/12** |
| 全量 EditMode | `run_tests --mode editor --filter LiteGame.EditModeTests --filter_type assembly` | **484/484**（含 LTextWiring 5/5 · MetaEntry 9/9 · UiU0 6/6 · ReleaseLayout 6/6 · ConfigSnapshot 3/3） |
| L1 门禁 | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` | **1636/1636**（含纪律扫描） |
| 纪律扫描 | `DisciplineScannerTests.真实源码_全部目标_零违规` | 绿。首跑红＝`App/Ui/PageFlow.cs:6 R12:using XLua` → 行内 `lint-allow R12`（结算页数据表构造点在流程侧——与 MetaBridge 同判据，本仓第二处）后转绿 |
| BuildHash | `python scripts/codegen/gen-build-hash.py --check` | 双常量 OK（shared `ab821e1b49cd6a13` 不变） |

## 页面流（口径落点）

```text
[Main 首进，配置了 -meta.url] → Page_Login
Page_Login ──GuestLogin ok（替换）──▶ Page_Lobby
Page_Lobby ──QuickEnter ok（替换，带房号）──▶ Page_Room ──StartGame（流程侧关闭）──▶ [对局]
[对局] ──Ended：Completed（Go）──▶ Page_Result ──BackLobby（替换）──▶ Page_Lobby
[对局] ──Ended：失败/主动离场──▶（房间页在则替换回 Page_Lobby；流程收尾归既有管道）
登录/大厅/结算 ──设置入口（叠加）──▶ Page_Settings ──Close/ESC──▶ 回退
```

## 边界与备注（如实）

- **端到端手测未跑**：真联机链路（`-meta.url` + 真 Meta + 真房服 + 真 KCP/信封）需本机与部署环境，归手测；本批验证面 = 重编译 + 全量 EditMode + L1 + 纪律扫描（等价于 批C-③ 的验证面）。
- 失败/取消语义：`Page_Room` 离开先替换回大厅再 `LeaveMatch()`（流程回 Main 后入口页不重开——`OpenEntryOnce` 只开一次；房间页不在 = 无从谈回大厅，留在现状）。
- 结算页 `Txt_Score` 隐藏：线上冻结结果无 score 字段（FFA 口径 `Score` 只在 Team 模式有值）；补 ranking/score 归协议批。
- 视觉/动效未动：prefab 零改动；按压动效走既有 `Pulse` 口，点击音效仍挂效果系统 E2（与《页面层级设计》§7 一致）。
- 存量债同型（如实）：`Bridge.Data` 的静态装订面与静态 LuaTable 缓存沿用既有"受控存量债"范式（《代码质量审计-客户端》[中] 债同族）；本批新增的 `ResolveUIFormId` 缓存走同一 `ClearLuaCaches` 清理点，未扩大机制面。
- 历史记录不改写：《上云五页制作》所记"接线挂账"由其索引行与新记录承接；`BridgeCapabilityHealthProbe` 的能力行未随批扩展（探针未装配，单独评估）。
