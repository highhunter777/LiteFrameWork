# 上云批C-⑥：控件与列表协议接线

> 依据：[《UI控件Lua API 参考》](../设计文档/client/ui/UI控件LuaAPI参考.md) §3～§6（G6 `SetList`、G15～G17 输入控件族、§6"新增受控方法必须同步 shim/Dispatch/UIBindIndex/白名单并提供真 Lua 调用测试"）；[《上云测试页面层级设计》](../设计文档/client/ui/上云测试页面层级设计.md) §3～§6/§10；[《UI框架总设计》](../设计文档/client/ui/UI框架总设计.md) §8.2（列表窗口复用/重绑纪律）
> 范围：控件族设值/读取/变化通知 + 列表行数据协议（行填充/稳定 key/行点击）+ 设置门面与生效面 + 房间页等待人数 + 结算页名次 + 两条列表条目模板节点
> 状态：**已完成（附验证证据）**——2026-10-13
> 不含（如实挂账，见文末）：Room 播放器席位名单（`SimpleList_Players`）、`Button_StartMatch`（房主开局控件）、设置页现场观感验收

## 交付物

| 项 | 落点 |
| --- | --- |
| 输入控件族（G15～G17） | `UIBindIndex`：`SetSlider/GetSlider/OnSliderChanged`、`SetToggle/GetToggle/OnToggleChanged`、`SetDropdownOptions/SetDropdownValue/GetDropdownValue/OnDropdownChanged`——**程序 Set 不触发变化回调**（`Set*WithoutNotify`）、回调只在用户操作时带值触发、同名重复登记 = 替换、`UnbindAll` 退订全部（池化复用不留旧回调；`_controlUnsub` 登记） |
| 列表协议（G6） | 新端口 `IUIListRows`/`UIListField`（`UI/Bind/UIListRows.cs`）+ `UIBindIndex.SetList(name, rows)` / `OnListClick(name, fn(key, action))`：**行字段按条目子节点名寻址**（条目子树不设 BindName——"条目内部不暴露"）、行 key 稳定（交互回传，不作身份）、**整表替换语义**（空源过渡 → 全部条目重绑：内核只在索引进出窗口时绑定）、行含文案键而语言服务未注入 = **装配期 fail-fast**、语言切换整表重绑刷新行文案（不做逐行订阅）、行点击按条目内按钮节点名回传（条目根按钮 = 空串） |
| Lua 适配器 | `LuaBehaviourAdapter`：shim **+12 方法**（20 → 32）；派发由 `Action<string,LuaTable>` 改 **`Func<string,LuaTable,object>`**（get* 系回值，其余 nil）；未知方法开发期 **告警**（不静默吞拼写错误——§3 限制项收口）；行数据桥 `LuaListRows`（Lua 行表 → IUIListRows，字段值 = 字符串/数字（数据直写）或表（`key` + 具名 `arg0..arg3` 文案）） |
| 设置门面与生效面 | 新 `ProductSettings`（`Bridge.settings`，App 侧）：`Volume/SetVolume`、`SoundEnabled/SetSoundEnabled`、`Quality/SetQuality`、`Save`——存储走 `GameSettings`（增 `SoundEnabled` 设置项）；**生效面**：`AudioService` 分组音量（音效开关 = Effect 组门）+ 画质档位（UI 三档 ↔ 平台档位按名匹配、缺档钳位）；**构造即应用**（`AudioService`/`QualitySettings` 此前零应用面——设置值只存不生效）；落盘 = 设置页 OnHide/Close 调 `Save`（SettingService 脏标记协议） |
| 能力事实源 | `BridgeCapabilitySet.Default()`：ApiVersion **1 → 2**；能力 3 → **6**（补 text/meta/settings——"新增 Bridge 绑定必须同步登记"的既有漏登收口） |
| 条目模板节点 | `Page_Lobby`/`Page_Result` 两条列表条目（构建器 `AgentScripts/BuildC6ListItems.cs`，幂等）：Lobby 条目 `Txt_Label→Txt_RoomName` + 增 `Txt_RoomState`（文案键带参）+ `Button_RowClick`；Result 条目 `Txt_Label→Txt_Kill` + 增 `Txt_MatchTime`/`Txt_EndReason` + `Button_RowClick`——新增节点为新 GO（实例内加组件被 Unity 禁）；新增文本以 `EditorUtility.CopySerialized` 复刻模板 TMP（字体/材质随模板）；三区拉伸锚布局（不写死像素宽） |
| 页面接线 | **Page_Lobby**：ListRooms → `SetList` 行填充（key=roomId/名/人数键）+ `OnListClick` → `EnterRoom(roomId)`（**选择式进房打通**；空 roomId 仍走 QuickEnter）→ 成功替换 Page_Room；**Page_Result**：`SetList` 战绩行（key=matchId/时间数据/击杀键/原因拼键）+ 本局名次（`Txt_Score` ← 新键 `UI.Result.Rank`）；**Page_Room**：等待人数 = `ListRooms` 回查该房投影（players/expectedPlayers——无新协议；投影未含则保持隐藏不显假数）；**Page_Settings**：四控件全接线（拖动/切换/选择即生效）+ 语言切换入口（`Bridge.text.SetLocale`；切换后重设画质选项文本）+ 关闭/OnHide 落盘 |
| 数据面补件 | `MetaBridge`：战绩行增 `time`（本地时间格式化在 C#——Lua 沙箱不依赖 os 库）；`PageFlow.ResultData` 增 `rank`（FFA 无 score：按冻结结果 `players[]` 击杀序**两遍式**推导，并列同名次，无本机行 = 0 隐藏） |
| 文案键 | `+UI.Result.Rank`（42 → 43）→ Luban 双端重生成；**tbltext 为表现层排除表 → buildHash 不变**（`--check` 实证） |
| 测试 | 新增 `UiControlListProtocolEditModeTests` 5 例（**真 Lua 调用面**：真 LuaEnv + 轻适配器 + 合成控件树 → shim/Dispatch/UIBindIndex 全链——程序 Set 不触发回调、用户操作带值回调、重登记替换、UnbindAll 退订、行投影/缩容重绑/缺节点留空/行点击 key+按钮名/行含文案键未注入语言服务的装配期拒绝）；`MetaPagesEditModeTests` 12 → **16 例**（设置页控件初值+变化回调+落盘、大厅行投影+行点击进房、结算名次+战绩行、房间等待人数回查） |

## 证据

| 项 | 命令/方式 | 结果 |
| --- | --- | --- |
| 重编译 | unity-pipeline `recompile` → `recompile_status` | completed / `failed:false` / `errors:[]` |
| 定向 EditMode | `run_tests --filter UiControlListProtocolEditModeTests` · `MetaPagesEditModeTests` · `LTextWiringEditModeTests` · `MetaEntryEditModeTests` | **5/5 · 16/16 · 5/5 · 9/9** |
| 全量 EditMode | `--filter LiteGame.EditModeTests --filter_type assembly` | **493/493** |
| L1 门禁 | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` | **1636/1636** |
| xLua 生成代码 | `GenConfig` 白名单（+ `Func<string,LuaTable,object>`、float/bool/int 形）→ `XLua/Generate Code` | `DelegatesGensBridge.cs` +349/−23 |
| BuildHash | `gen-build-hash.py --check` | 双 OK（文案表排除判据不变） |
| 条目结构 | `BuildC6ListItems.Report`（只读探针） | 两页条目三/四子节点、三区布局、模板保持非激活 |

## 边界与备注（如实）

- **端到端手测未跑**：设置页现场观感（控件视觉/拖动手感）、列表滚动真机表现、联机全链（进房/结算）归本机手测。
- 新增 API 的两处实现取舍：①行文案刷新走**整表重绑**（行是复用条目，不做逐行订阅——滚动路径零残留，代价 = 语言切换时窗口内条目全部重写）；②`SetList` 用"空源过渡"强迫重绑（内核只在索引进出窗口时绑定；整表替换必须显式重绑，否则同索引显示旧行——本批实测暴露并修正）。
- 挂账（登记在案）：`SimpleList_Players` 席位名单（`JoinAck.members` 有席位 id、无名字/在线态——命名面全链缺失，需"席位实时面"批）；`Button_StartMatch`（房主开局——prefab 控件缺失，承接《开房与房主开局》）；`Txt_Score` 的"比分"语义按名次落地（FFA 口径无 score 字段）。
- 存量债同型（如实）：`ProductSettings` 静态装订面沿用 `MetaBridge.s_meta`/`Bridge.Data` 定位器范式（审计表[中]债同族）；`Bridge.settings` 未装订时**装订不抛、读取点 fail-fast**（与 meta 门面同口径——让只测 meta 的用例可共用 ProductBridges 装订口）。
- 与列表内核的关系：窗口/复用/裁剪归 `VirtualList`/`SimpleListView`（未改内核）；行投影/点击/解绑全在 `UIBindIndex.BindListSource`（乐观并发面零新增）。
