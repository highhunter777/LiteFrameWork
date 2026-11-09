# T4 真实页面操作验收记录（2026-11-08 接管后补跑）

> 环境：Unity 2022.3.55f1c1 Editor（pipeline 7801），本机独立 Meta/Room/Mongo 链（`AgentScripts/LobbyRoomFlow/local_stack.py`，Meta 5018 / RoomUDP 17788 / Mongo 27028，临时任务容器 codex-lobby-flow-mongo）。不触碰云侧、不重打 Player。所有点击均为 EventSystem.RaycastAll 首命中 + 真实 pointer down/up/click；Back 为 InputSystem `QueueStateEvent(KeyboardState)`。

## 本轮实际执行（真实页面）

| 动作 | 路径 | 结果 | 证据 |
|---|---|---|---|
| 登录 | `Page_Login` → 点 `Btn_GuestLogin` → `Page_Lobby` | ok=true；首命中 `[UIRoot]/Bottom/Page_Login(Clone)/Cot_SafeArea/Cot_LoginCard/Btn_GuestLogin` | evidence/ui-login.json（前轮）+ 本轮复跑同结果 |
| 快速进入 | `Page_Lobby` → `Btn_QuickEnter` → `Page_Room` | ok=true；JoinAck 后席位投影非空（seats=1/expected=2） | evidence/ui-quick-enter-after-rebuild.json |
| 离房 | `Page_Room` → 点 `Btn_Leave` → `Page_Lobby` | ok=true；房间投影清零（seats=0/expected=0） | evidence/ui-leave-1.json、ui-leave-2.json |
| 再次进入 | `Page_Lobby` → `Btn_QuickEnter` → `Page_Room` | ok=true | evidence/ui-reenter.json |
| 普通建房 | `Page_Lobby` → `Btn_CreateRoom` → 确认 → `Page_Room` | ok=true | evidence/ui-create-room.json |
| 建房后离房 | `Page_Room` → `Btn_Leave` → `Page_Lobby` | ok=true | evidence/ui-leave-created.json |
| 选房进入 | `Page_Lobby` → `Btn_Rooms` → 真实列表行 `R-CJK2A6` → `Page_Room` | ok=true；row_text=R-CJK2A6；seats=2/expected=4；Btn_Leave 命中 | 本轮 run_script 输出（见下）；前轮 ui-select-room-success.json 为失败留痕，不作为通过证据 |
| 房主开局 | `Page_Room` → `Btn_StartMatch` → 场景 `Boot→Arena01`，房间页/加载遮罩关闭 | ok=true；scene_after active=Arena01 | evidence/ui-owner-start-success.json |
| 对局中离场 | 对局 HUD → Esc（真实 InputSystem）→ `Page_Settings` → `Btn_ExitMatch` → 确认 `Button_Ok` → `Page_Lobby` | ok=true；确认后大厅可操作、房间投影空 | evidence/ui-battle-menu-leave.json |
| 房间失败恢复 | JoinAck 失败 → 错误确认模态 → `Button_Ok` → `Page_Lobby` | ok=true；模态 kind=error；确认后大厅可操作 | 本轮 ConfirmError 输出 |

本轮选房通过时的原始判据（run_script result 原文摘录）：

```
action=RoomList
row_object=Clone_Row(Clone)
row_text=R-CJK2A6
room=source=IRoomView(RoomBridge);view=true;seats=2;expected=4;owner=false;ui_rows=2
ok=true
before_pages=Page_Lobby
after_pages=Page_Room
click_first_hit=[UIRoot]/Bottom/Page_Lobby(Clone)/Cot_SafeArea/Cot_Rooms/VList_Rooms/Viewport/Cot_Rows/Clone_Row(Clone)
step8=assert=房间页可操作（Btn_Leave 命中）
```

截屏（`capture_game_view --source screen`）：

- `Artifacts/LobbyRoomFlow/t4-lobby-after-leave.png`（离房回大厅）
- `Artifacts/LobbyRoomFlow/t4-room-selected.png`（选房进入后的房间页）
- 前轮失败留痕：`Artifacts/LobbyRoomFlow/first-join-error.png`

## 未验证（不得计通过）

1. **等待 JoinAck 时用加载面 Back 取消**：本机链的 JoinAck 失败窗口太短/不可控（房间服进程杀掉后 Meta 很快剔除实例；挂起进程时连接直接失败）。尝试过真实 Esc 注入，但 `run_script` 在 pipeline 上是串行执行，第二个脚本无法在第一个脚本等待期间并发执行，`CancelWait` 前置的 Loading 在场条件无法满足。该契约需改由自动化生命周期用例覆盖（T6 定向用例），真实 UI 侧保留未验收。
2. **GM sandbox 建房进入**（AC7）：本轮未跑；前轮仅做代码迁移核对，无真实页面证据。
3. **账号重入真实页面落点**（AC7）：仅自动化用例覆盖，未做真实页面复现。
4. 部分步骤的实时截屏未覆盖（登录/建房/开局/对局菜单），仅有结果 JSON 留痕。

## 环境清理

- `local_stack.py stop` 已执行：停止本任务 Meta/Room/Mongo、删除任务容器、`Assets/StreamingAssets/deploy.args` 按原字节恢复（`git status` 无差异）。
- Unity 已退出 Play；`DevStartupConfig.DirectToTraining` 仅在编辑期内存改为 false（不保存场景），退出 Play 后为场景原值，`Assets/Scenes/Boot.unity` 无 git 差异。
- `Assets/Artifacts`（截图命令默认落点）已通过 pipeline `delete_asset` 删除，截图改存仓库根 `Artifacts/LobbyRoomFlow`。
