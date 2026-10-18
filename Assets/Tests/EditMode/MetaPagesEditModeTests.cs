using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LiteClient;
using LiteGame;
using LiteTesting.Unity;
using Luban;
using NUnit.Framework;
using UnityEngine;
using XLua;
using LiteClient.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 批C-④ 五页接线（《上云测试页面层级设计》§1～§6）：真 LuaEnv + 磁盘脚本 + **受控面替身**
    /// （Bridge 三表 + self.ui 记录器——页面只认受控面，故无需 Unity 对象即可驱动整页逻辑并断言调用序）：
    /// ① 登录页（未配置拒绝 / 在途态 / 成功替换大厅 / 失败页内重试 + Toast）；② 大厅页（列表三态 / 快速进入）；
    /// ③ 房间页（房号 + 离开=回大厅并请流程收尾）；④ 结算页（本局结果 + 玩家排行 / 空态）；
    /// ⑤ 战绩页（ListRecords 列表行投影 / 空态）；⑥ 设置页（标签 + 只读数据 + 关闭）。
    /// 另含**文案键守卫**：页面脚本引用的每个 LText 键（含结束原因值域拼键）必须在 tbltext 表源在册。
    /// 流程侧三处页面切换（首进开入口 / 开局关房间页 / 结束开结算页）归 <c>PageFlow</c>（C# 面，真导航需 Unity——归 PlayMode/夜间）。
    /// </summary>
    public sealed class MetaPagesEditModeTests : UnityTestBase
    {
        /// <summary>
        /// 替身面（Lua 侧，只用单引号以免 C# 转义）：`Bridge` 三表 + `self.ui` 记录器 + 按钮/回调捕获。
        /// 记录格式（串）示例：`Key:Txt_Status=UI.Login.NoServer`、`Replace:UI.Page_Room#Room-A`、`LeaveMatch`。
        /// </summary>
        private const string Harness = @"
log = { info = function() end, warning = function() end, error = function() end }
calls = {}
buttons = {}
controls = {}
lists = {}

local function dataTag(d)
    if type(d) == 'table' and d.roomId ~= nil then return '#' .. tostring(d.roomId) end
    return ''
end

-- self.ui 面（冒号调用：首参 self 占位——与 LuaBehaviourAdapter 门面同形）
ui = {}
dropdowns = {}
ui.OnButton = function(_, name, fn) buttons[name] = fn end
ui.SetTextKey = function(_, name, key) calls[#calls + 1] = 'Key:' .. name .. '=' .. key end
ui.SetTextKeyArgs = function(_, name, key, a0, a1)
    calls[#calls + 1] = 'KeyArgs:' .. name .. '=' .. key .. '(' .. tostring(a0) .. ',' .. tostring(a1) .. ')'
end
ui.SetText = function(_, name, text) calls[#calls + 1] = 'Text:' .. name .. '=' .. tostring(text) end
ui.SetVisible = function(_, name, v) calls[#calls + 1] = 'Visible:' .. name .. '=' .. tostring(v) end
ui.SetInteractable = function(_, name, v) calls[#calls + 1] = 'Interact:' .. name .. '=' .. tostring(v) end
ui.Pulse = function(_, name) calls[#calls + 1] = 'Pulse:' .. name end
ui.ShowToast = function(_, text) calls[#calls + 1] = 'Toast:' .. tostring(text) end
ui.SetSlider = function(_, name, v) calls[#calls + 1] = 'Slider:' .. name .. '=' .. tostring(v) end
ui.OnSliderChanged = function(_, name, fn) controls['slider:' .. name] = fn end
ui.SetToggle = function(_, name, on) calls[#calls + 1] = 'Toggle:' .. name .. '=' .. tostring(on) end
ui.OnToggleChanged = function(_, name, fn) controls['toggle:' .. name] = fn end
ui.SetDropdownOptions = function(_, name, options) calls[#calls + 1] = 'Options:' .. name .. '=' .. tostring(#options) end
ui.SetDropdown = function(_, name, index)
    dropdowns[name] = index
    calls[#calls + 1] = 'Dropdown:' .. name .. '=' .. tostring(index)
end
ui.GetDropdown = function(_, name) return dropdowns[name] or 0 end
ui.OnDropdownChanged = function(_, name, fn) controls['dropdown:' .. name] = fn end
ui.SetList = function(_, name, rows) lists[name] = rows; calls[#calls + 1] = 'List:' .. name .. '=' .. tostring(#rows) end
ui.OnListClick = function(_, name, fn) lists['click:' .. name] = fn end
ui.ShowConfirm = function(_, title, message, okText, cancelText, fn)
    calls[#calls + 1] = 'Confirm:' .. tostring(title) .. '/' .. tostring(okText) .. '/' .. tostring(cancelText)
    confirm_fn = fn
end

-- Bridge.ui 面（点调用——与 C# 委托签名一致，无 self 位）
local bridgeUi = {}
bridgeUi.Show = function(path) calls[#calls + 1] = 'Show:' .. tostring(path) end
bridgeUi.Replace = function(path, d) calls[#calls + 1] = 'Replace:' .. tostring(path) .. dataTag(d) end
bridgeUi.Close = function(path) calls[#calls + 1] = 'Close:' .. tostring(path) end

-- meta 面（点调用；全局——各用例片直接驱动其回调）
meta = { configured = true, account = 'acc-1', server = 'http://meta:8080', version = 'bh-test' }
meta.IsConfigured = function() return meta.configured end
meta.AccountId = function() return meta.account end
meta.ServerUrl = function() return meta.server end
meta.Version = function() return meta.version end
meta.GuestLogin = function(cb) meta.cb_guest = cb end
-- 列表刷新批：投影查询本身也记一笔（断言 节拍/揭示/在途 三态下的查询次数）
meta.ListRooms = function(cb) calls[#calls + 1] = 'RoomsQuery'; meta.cb_rooms = cb end
meta.QuickEnter = function(cb) meta.cb_enter = cb end
-- 开房参数批：CreateRoom(mapId, expectedPlayers, cb)——两值 = 请求地图行键 / 期望人数（0 = 未指定）
meta.CreateRoom = function(mapId, expectedPlayers, cb)
    calls[#calls + 1] = 'CreateRoom:' .. tostring(mapId) .. ':' .. tostring(expectedPlayers)
    meta.cb_create = cb
end
meta.EnterRoom = function(roomId, cb)
    calls[#calls + 1] = 'EnterRoom:' .. tostring(roomId)
    meta.cb_enter = cb
end
meta.ListRecords = function(cb) meta.cb_records = cb end
meta.LeaveMatch = function() calls[#calls + 1] = 'LeaveMatch' end

settings = { volume = 0.5, sound = true, quality = 1 }
settings.Volume = function() return settings.volume end
settings.SetVolume = function(v) settings.volume = v; calls[#calls + 1] = 'SetVolume:' .. tostring(v) end
settings.SoundEnabled = function() return settings.sound end
settings.SetSoundEnabled = function(on) settings.sound = on; calls[#calls + 1] = 'SetSound:' .. tostring(on) end
settings.Quality = function() return settings.quality end
settings.SetQuality = function(i) settings.quality = i; calls[#calls + 1] = 'SetQuality:' .. tostring(i) end
settings.Save = function() calls[#calls + 1] = 'Save' end

-- room 面（点调用；全局——房主开局批：页面订阅房主事实/发提前开局；席位实时面批：页面订阅席位名单；
-- 测试手动驱动 owner_fn/members_fn 模拟回放与迁移；房规模拉取形同 settings.Quality——房间页等待人数分母）
room = {}
room.OnOwner = function(fn) room.owner_fn = fn end
room.StartMatch = function(cb) calls[#calls + 1] = 'StartMatch'; room.start_fn = cb end
room.OnMembers = function(fn) room.members_fn = fn end
room.ExpectedPlayers = function() return room.expected end

-- data 面（开房参数批：选图下拉的候选目录 = 地图表全行；此处桩两行钉表序与字段名）
local bridgeData = {}
bridgeData.ListMaps = function()
    calls[#calls + 1] = 'ListMaps'
    return { { Id = 1, Name = 'arena01', Scene = 'a' }, { Id = 2, Name = 'training_ground', Scene = 'b' } }
end

Bridge = {
    data = bridgeData,
    ui = bridgeUi,
    meta = meta,
    settings = settings,
    room = room,
    text = {
        Get = function(key) return '[' .. key .. ']' end,
        Format = function(key, a0) return '[' .. key .. ':' .. tostring(a0) .. ']' end,
        Locale = function() return text_locale end,
        SetLocale = function(locale) text_locale = locale; calls[#calls + 1] = 'SetLocale:' .. tostring(locale) end,
    },
}
text_locale = 'zh-CN'

function NewPage(path)
    local m = require(path)
    local inst = m.new()
    inst.ui = ui
    return inst
end
";

        private static LuaEnv NewEnv()
        {
            var env = new LuaEnv();
            env.AddLoader((ref string filepath) =>
            {
                var key = filepath.Replace('.', '/');
                var path = Path.Combine(Application.dataPath, "LiteGame/Lua", key + ".lua");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            });
            env.DoString(Harness, "meta_pages_harness");
            return env;
        }

        private static string Calls(LuaEnv env) => (string)env.DoString("return table.concat(calls, '|')")[0];

        // ---- Page_Login（§2）----

        [Test]
        public void 登录页_未配置_显性拒绝且按钮禁用()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
meta.configured = false
page = NewPage('UI.Page_Login')
page:OnShow(nil)");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_Status=UI.Login.NoServer", calls, "未配置服务器显性拒绝（fail-closed）");
                StringAssert.Contains("Interact:Button_GuestLogin=false", calls, "拒绝态按钮禁用");
                StringAssert.Contains("Key:Txt_Title=UI.Login.Title", calls);
                StringAssert.Contains("KeyArgs:Txt_Version=Common.Version(bh-test,nil)", calls, "版本 = 数据参数进文案键");
            }
        }

        [Test]
        public void 登录页_登录成功_替换进大厅()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Login')
page:OnShow(nil)
buttons['Button_GuestLogin']()");
                StringAssert.Contains("Key:Txt_Status=UI.Login.Connecting", Calls(env), "在途态给 Connecting 键");
                StringAssert.Contains("Interact:Button_GuestLogin=false", Calls(env), "在途禁用防重复点");

                env.DoString("meta.cb_guest(true, 'acc-9')");
                StringAssert.Contains("Replace:UI.Page_Lobby", Calls(env), "登录成功用替换（返回栈不残留——§1）");
            }
        }

        [Test]
        public void 登录页_登录失败_页内重试与Toast兜底()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Login')
page:OnShow(nil)
buttons['Button_GuestLogin']()
meta.cb_guest(false, 'Network')");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_Status=UI.Login.Failed", calls);
                StringAssert.Contains("Toast:[UI.Login.Failed]", calls, "系统反馈兜底（§2）");
                StringAssert.Contains("Interact:Button_GuestLogin=true", calls, "失败可页内重试");
            }
        }

        // ---- Page_Lobby（§4）----

        [Test]
        public void 大厅页_房间列表三态()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
meta.cb_rooms(true, {})");
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.RoomEmpty", Calls(env), "空表 → RoomEmpty");

                env.DoString("meta.cb_rooms(true, { { roomId = 'Room-A' } })");
                StringAssert.Contains("UI.Lobby.Ready", Calls(env), "非空 → Ready");

                env.DoString("meta.cb_rooms(false, 'Network')");
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.ListFailed", Calls(env), "查询失败 → ListFailed");
            }
        }

        [Test]
        public void 大厅页_快速进入_分配中到替换房间页带房号()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
meta.account = 'acc-77'
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
buttons['Button_QuickEnter']()");
                string mid = Calls(env);
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.Allocating", mid, "在途给 Allocating 键");
                StringAssert.Contains("Interact:Button_QuickEnter=false", mid);
                StringAssert.Contains("Text:Txt_PlayerName=acc-77", mid, "账号短号 = 数据面（SetText）");

                env.DoString("meta.cb_enter(true, 'Room-A')");
                StringAssert.Contains("Replace:UI.Page_Room#Room-A", Calls(env), "进房替换 + 房号随导航数据（§1/§3）");

                env.DoString(@"
page:OnShow(nil)
buttons['Button_QuickEnter']()
meta.cb_enter(false, 'Network')");
                StringAssert.Contains("Toast:[UI.Lobby.EnterFailed]", Calls(env), "进房失败 Toast 兜底");
            }
        }

        [Test]
        public void 大厅页_创建房间_在途到替换房间页带新号()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
buttons['Button_CreateRoom']()");
                string mid = Calls(env);
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.Creating", mid, "在途给 Creating 键");
                StringAssert.Contains("Interact:Button_CreateRoom=false", mid, "在途禁钮（防重入）");
                StringAssert.Contains("CreateRoom:1:2", mid,
                    "走服务端出号（Bridge.meta.CreateRoom；开房参数：首参=选图行键（默认第一行），次参=选人数（默认 2））");


                env.DoString("meta.cb_create(true, 'R-NEW001')");
                StringAssert.Contains("Replace:UI.Page_Room#R-NEW001", Calls(env), "成功替换房间页 + 新房号随导航数据（§1/§3）");

                // 改选后透传：下拉索引 → 行键/档位（0 基索引；索引 1 = 第二行训练场图 / 第二档 4 人）
                env.DoString(@"
page:OnShow(nil)
dropdowns['Dropdown_Map'] = 1
dropdowns['Dropdown_Players'] = 1
buttons['Button_CreateRoom']()");
                StringAssert.Contains("CreateRoom:2:4", Calls(env),
                    "选图/选人数随创建意图透传（地图行键 = 表行键；人数 = 档位表取值）");

                env.DoString(@"
page:OnShow(nil)
buttons['Button_CreateRoom']()
meta.cb_create(false, 'Network')");
                StringAssert.Contains("Toast:[UI.Lobby.CreateFailed]", Calls(env), "创建失败 Toast 兜底");
            }
        }

        [Test]
        public void 大厅页_战绩与设置入口()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
buttons['Button_Records']()
buttons['Button_Settings']()");
                string calls = Calls(env);
                StringAssert.Contains("Show:UI.Page_Records", calls, "最近战绩入口 → 战绩独立页（§5·战绩页）");
                StringAssert.Contains("Show:UI.Page_Settings", calls, "设置叠加打开");
            }
        }

        // ---- Page_Room（§3）----

        [Test]
        public void 房间页_房号展示_离开回大厅并请流程收尾()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Room')
page:OnShow({ roomId = 'Room-A' })
buttons['Button_Leave']()");
                string calls = Calls(env);
                StringAssert.Contains("KeyArgs:Txt_RoomId=UI.Room.Info(Room-A,nil)", calls, "房号 = 导航数据进文案键");
                StringAssert.Contains("Replace:UI.Page_Lobby", calls, "离开 = 回大厅（§3）");
                StringAssert.Contains("LeaveMatch", calls, "并请流程收尾（Match/Battle 阶段取消释放）");
                StringAssert.Contains("Visible:Txt_State=false", calls, "未收到席位快照 → 保持隐藏（不显假数）");
            }
        }

        // ---- Page_Result（§5）----

        [Test]
        public void 结算页_本局结果记分板()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Result')
page:OnShow({ reason = 'KillLimit', kills = 5, deaths = 2, hasPlayer = true })
buttons['Button_BackLobby']()");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_ResultTitle=UI.Result.Reason.KillLimit", calls, "结束原因拼键（值域随枚举）");
                StringAssert.Contains("KeyArgs:Txt_KillCount=UI.Result.Kills(5,nil)", calls);
                StringAssert.Contains("KeyArgs:Txt_DeathCount=UI.Result.Deaths(2,nil)", calls);
                StringAssert.Contains("Visible:Txt_Score=false", calls, "无 score 字段 → 隐藏（挂协议批）");
                StringAssert.Contains("Replace:UI.Page_Lobby", calls);
            }
        }

        [Test]
        public void 结算页_大厅入口_无本局数据_空态与空排行()
        {
            // 玩家排行批：战绩查询面已弃用（唯一消费面即本页）——大厅入口不再回落"最近一战"，
            // 无本局数据 = 空态 ＋ 空排行（不显上一局残留行）。
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Result')
page:OnShow(nil)");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_ResultTitle=UI.Result.NoMatchData", calls, "无本局数据 → 空态");
                StringAssert.Contains("Visible:Txt_KillCount=false", calls, "空态不显本机摘要");
                StringAssert.Contains("List:VirtualList_Rankboard=0", calls, "无本局数据 → 空排行");
            }
        }

        // ---- Page_Records（§5·战绩页——战绩面从结算页拆出）----

        [Test]
        public void 战绩页_列表行投影_返回大厅()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Records')
page:OnShow(nil)
meta.cb_records(true, { { matchId = 'M-1', kills = 3, reason = 'KillLimit', time = '2026-10-18 10:00' },
                       { matchId = 'M-2', kills = 0, reason = 'TimeLimit', time = '2026-10-17 21:00' } })");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_Title=UI.Lobby.Records", calls, "标题键（复用大厅入口键）");
                StringAssert.Contains("Key:Txt_Back=Common.BackLobby", calls);
                StringAssert.Contains("Visible:Txt_Empty=false", calls, "查询返回前不显空态（防加载闪空）");
                StringAssert.Contains("List:VirtualList_Records=2", calls, "战绩行填充（SetList 整表替换）");
                string row = (string)env.DoString(@"
local r = lists['VirtualList_Records'][1]
return r.key .. '|' .. r.Txt_MatchTime .. '|' .. r.Txt_Kill.key .. '|' .. tostring(r.Txt_Kill.arg0)
    .. '|' .. r.Txt_EndReason.key")[0];
                Assert.AreEqual("M-1|2026-10-18 10:00|UI.Result.Kills|3|UI.Result.Reason.KillLimit", row,
                    "行字段：key=matchId + 时间为数据 + 击杀文案键带参 + 结束原因拼键");

                env.DoString("buttons['Button_Back']()");
                StringAssert.Contains("Replace:UI.Page_Lobby", Calls(env), "返回 = 替换回大厅");
            }
        }

        [Test]
        public void 战绩页_空表与未配置_落空态()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Records')
page:OnShow(nil)
meta.cb_records(true, {})");
                string calls = Calls(env);
                StringAssert.Contains("Visible:Txt_Empty=true", calls, "空表 → 空态");
                StringAssert.Contains("Key:Txt_Empty=UI.Records.Empty", calls, "空态文案键（UI.Records.Empty）");
                StringAssert.Contains("List:VirtualList_Records=0", calls, "空表 → 空列表（不显残留行）");

                env.DoString(@"
meta.configured = false
calls = {}
page:OnShow(nil)");
                string offline = Calls(env);
                StringAssert.Contains("Visible:Txt_Empty=true", offline, "未配置服务器 → 显性拒绝（空态）");
                StringAssert.DoesNotContain("List:VirtualList_Records=2", offline, "未配置不查战绩");
            }
        }

        // ---- Page_Settings（§6）----

        [Test]
        public void 设置页_标签与只读数据_关闭()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
meta.account = 'acc-3'
meta.server = 'http://meta:8080'
page = NewPage('UI.Page_Settings')
page:OnShow(nil)
buttons['Button_Close']()");
                string calls = Calls(env);
                StringAssert.Contains("Key:Txt_Volume=UI.Settings.Volume", calls);
                StringAssert.Contains("Key:Txt_Sound=UI.Settings.Sound", calls);
                StringAssert.Contains("Key:Txt_Quality=UI.Settings.Quality", calls);
                StringAssert.Contains("Key:Txt_Language=UI.Settings.Language", calls);
                StringAssert.Contains("KeyArgs:Txt_Account=UI.Settings.Account(acc-3,nil)", calls);
                StringAssert.Contains("KeyArgs:Txt_ServerAddress=UI.Settings.Server(http://meta:8080,nil)", calls);
                StringAssert.Contains("KeyArgs:Txt_Version=Common.Version(bh-test,nil)", calls);
                StringAssert.Contains("Close:UI.Page_Settings", calls, "Button_Close = 关闭（叠加页 Back 同径）");
            }
        }

        [Test]
        public void 设置页_未登录_不显空账号标签()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
meta.account = ''
meta.server = ''
page = NewPage('UI.Page_Settings')
page:OnShow(nil)");
                string calls = Calls(env);
                StringAssert.Contains("Visible:Txt_Account=false", calls);
                StringAssert.Contains("Visible:Txt_ServerAddress=false", calls);
            }
        }

        // ---- HUD 批C 退场链：设置页退出对局（battleExit 门控 + 确认弹窗 + 先回大厅再离局）----

        [Test]
        public void 设置页_对局中入口_退场钮与确认退出链()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Settings')
page:OnShow({ battleExit = true })");
                string init = Calls(env);
                StringAssert.Contains("Visible:Button_Exit=true", init, "对局中入口（battleExit 数据）才显退场钮");
                StringAssert.Contains("Key:Txt_Exit=UI.Settings.Exit", init, "退场钮文案键");

                env.DoString(@"buttons['Button_Exit']()");
                string clicked = Calls(env);
                StringAssert.Contains("Interact:Button_Exit=false", clicked, "弹窗期间按钮锁点（防重入）");
                StringAssert.Contains("Confirm:[UI.Settings.ExitConfirmTitle]/[Common.Confirm]/[Common.Cancel]", clicked, "确认弹窗经受控面（文案/按钮键齐）");

                // 防重入：弹窗未落定前再点不重复发确认
                env.DoString(@"buttons['Button_Exit']()");
                Assert.AreEqual(1, Regex.Matches(Calls(env), "Confirm:").Count, "在途再点不重复弹确认");

                env.DoString("confirm_fn(true)");
                string confirmed = Calls(env);
                StringAssert.Contains("Replace:UI.Page_Lobby", confirmed, "确认 → 先回大厅（导航先行——Page_Room._Leave 同款先例）");
                StringAssert.Contains("LeaveMatch", confirmed, "确认 → 再请流程收尾（离房/离局同一窄接口）");
                StringAssert.Contains("Interact:Button_Exit=true", confirmed, "回调后按钮复位");
            }
        }

        [Test]
        public void 设置页_确认取消_不离局()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Settings')
page:OnShow({ battleExit = true })
buttons['Button_Exit']()
confirm_fn(false)");
                string calls = Calls(env);
                StringAssert.DoesNotContain("Replace:UI.Page_Lobby", calls, "取消不得离局");
                StringAssert.DoesNotContain("LeaveMatch", calls, "取消不得请流程收尾");
                StringAssert.Contains("Interact:Button_Exit=true", calls, "取消后按钮复位（可再发起）");
            }
        }

        [Test]
        public void 设置页_非对局入口_不显退场钮()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Settings')
page:OnShow(nil)");
                string calls = Calls(env);
                StringAssert.Contains("Visible:Button_Exit=false", calls, "大厅/登录的设置入口（无数据）不显退场钮");
                StringAssert.DoesNotContain("Key:Txt_Exit=", calls, "隐藏态不设文案（不产生多余键调用）");
            }
        }

        // ---- 批C-⑥：控件族 / 列表 / 设置门面 / 等待人数 / 排名 ----

        [Test]
        public void 设置页_控件初值与变化回调_落盘()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Settings')
page:OnShow(nil)");
                string init = Calls(env);
                StringAssert.Contains("Slider:Slider_Volume=0.5", init, "音量初值 = 设置值（程序 Set 不触发回调）");
                StringAssert.Contains("Toggle:Toggle_Sound=true", init, "音效初值");
                StringAssert.Contains("Options:Dropdown_Quality=3", init, "画质三档选项文本");
                StringAssert.Contains("Options:Dropdown_Language=2", init, "语言选项（语言自名）");
                StringAssert.Contains("Dropdown:Dropdown_Quality=1", init, "画质初值 = 设置值");
                StringAssert.Contains("Dropdown:Dropdown_Language=0", init, "语言初值 = 当前 locale（zh-CN）");

                env.DoString(@"controls['slider:Slider_Volume'](0.3)
controls['toggle:Toggle_Sound'](false)
controls['dropdown:Dropdown_Quality'](2)
controls['dropdown:Dropdown_Language'](1)");
                string after = Calls(env);
                StringAssert.Contains("SetVolume:0.3", after, "拖动 → 设置门面（拖动即生效）");
                StringAssert.Contains("SetSound:false", after, "音效开关 → 设置门面");
                StringAssert.Contains("SetQuality:2", after, "画质 → 设置门面");
                StringAssert.Contains("SetLocale:en", after, "语言切换 = Bridge.text.SetLocale（§10 裁决④）");
                StringAssert.Contains("Options:Dropdown_Quality=3", after, "切换语言后画质选项文本重设（下拉项不随 LText 自动刷新）");

                env.DoString("buttons['Button_Close']()");
                StringAssert.Contains("Save", Calls(env), "关闭 = 落盘（SettingService 三处纪律之设置界面关闭）");
                StringAssert.Contains("Close:UI.Page_Settings", Calls(env));

                env.DoString("calls = {}; page:OnHide()");
                StringAssert.Contains("Save", Calls(env), "OnHide（Back/ESC 路径）同径落盘");
            }
        }

        [Test]
        public void 大厅页_房间行投影_行点击选择式进房()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
meta.cb_rooms(true, { { roomId = 'Room-A', players = 2, expectedPlayers = 8 } })");
                StringAssert.Contains("List:VirtualList_Rooms=1", Calls(env), "行填充（SetList 整表替换）");
                string row = (string)env.DoString(@"
local r = lists['VirtualList_Rooms'][1]
return r.key .. '|' .. r.Txt_RoomName .. '|' .. r.Txt_RoomState.key .. '|' .. tostring(r.Txt_RoomState.arg0) .. '/' .. tostring(r.Txt_RoomState.arg1)")[0];
                Assert.AreEqual("Room-A|Room-A|UI.Lobby.RoomState|2/8", row, "行字段：key=roomId + 数据名 + 文案键带参人数");

                env.DoString("lists['click:VirtualList_Rooms']('Room-A')");
                StringAssert.Contains("EnterRoom:Room-A", Calls(env), "行点击 = 选择式进房（EnterRoom）");
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.Allocating", Calls(env), "在途态");

                env.DoString("meta.cb_enter(true, 'Room-A')");
                StringAssert.Contains("Replace:UI.Page_Room#Room-A", Calls(env), "进房成功替换 + 房号随导航数据");
            }
        }

        // ---- 列表刷新批：投影只在本页消费，新鲜度靠节拍轮询 + 揭示即重查 ----

        [Test]
        public void 大厅页_列表按节拍自轮询_投影未变不重推()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
meta.cb_rooms(true, { { roomId = 'Room-A', players = 1, expectedPlayers = 2 } })
calls = {}
page:OnUpdate(0.4)
page:OnUpdate(0.4)");
                Assert.AreEqual(0, Regex.Matches(Calls(env), "RoomsQuery").Count, "未到节拍不重查（累计 0.8s < 1s）");

                env.DoString("calls = {}; page:OnUpdate(0.4)");
                Assert.AreEqual(1, Regex.Matches(Calls(env), "RoomsQuery").Count, "累计满节拍即重查一次（1s）");

                // 同投影：不整表替换（SetList 是"空源过渡 + 换源"——会强迫已实体化条目全部重绑），状态行也不重写
                env.DoString(@"calls = {}
meta.cb_rooms(true, { { roomId = 'Room-A', players = 1, expectedPlayers = 2 } })");
                string same = Calls(env);
                StringAssert.DoesNotContain("List:VirtualList_Rooms", same, "投影未变不下发（免条目重绑/滚动复位）");
                StringAssert.DoesNotContain("Key:Txt_Status", same, "同状态键不重写");

                env.DoString(@"calls = {}
meta.cb_rooms(true, { { roomId = 'Room-B', players = 2, expectedPlayers = 4 } })");
                StringAssert.Contains("List:VirtualList_Rooms=1", Calls(env), "投影变了才重推");
            }
        }

        [Test]
        public void 大厅页_揭示即重查_在途不叠加_关页停轮询()
        {
            using (var env = NewEnv())
            {
                // 覆盖页（战绩/设置）关闭回大厅不重跑 OnShow（遮盖揭示走 OnReveal）——这条路径必须自己补一次
                env.DoString(@"
page = NewPage('UI.Page_Lobby')
page:OnShow(nil)
meta.cb_rooms(true, {})
calls = {}
page:OnReveal()");
                Assert.AreEqual(1, Regex.Matches(Calls(env), "RoomsQuery").Count, "揭示即重查一次");

                env.DoString("calls = {}; page:OnUpdate(1.5)");
                Assert.AreEqual(0, Regex.Matches(Calls(env), "RoomsQuery").Count, "查询在途不叠加第二次");

                // 关页：停轮询
                env.DoString(@"
meta.cb_rooms(true, {})                 -- 收口揭示那次查询
calls = {}
page:OnHide()
page:OnUpdate(1.5)");
                Assert.AreEqual(0, Regex.Matches(Calls(env), "RoomsQuery").Count, "关页后不再轮询");

                // 关页后到达的在途投影：丢弃，不写节点（关页后写节点会重挂文案绑定）
                env.DoString(@"
page:OnShow(nil)                        -- 重开（新查询在途）
page:OnHide()                           -- 秒关
calls = {}
meta.cb_rooms(true, { { roomId = 'Room-D' } })");
                string stale = Calls(env);
                StringAssert.DoesNotContain("List:", stale, "关页后到达的投影不写节点");
                StringAssert.DoesNotContain("Key:Txt_Status", stale, "同上（不写状态行）");

                // 在途进房：不轮询、不覆盖在途状态文案
                env.DoString(@"
page:OnShow(nil)
meta.cb_rooms(true, {})
calls = {}
buttons['Button_QuickEnter']()
page:OnUpdate(1.5)");
                string entering = Calls(env);
                Assert.AreEqual(0, Regex.Matches(entering, "RoomsQuery").Count, "在途进房期间不发起投影查询");
                StringAssert.Contains("Key:Txt_Status=UI.Lobby.Allocating", entering, "在途状态不被投影回填覆盖");
                StringAssert.DoesNotContain("Key:Txt_Status=UI.Lobby.Ready", entering, "同上（未回填就绪态）");
            }
        }

        [Test]
        public void 结算页_本机名次与玩家排行行投影()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Result')
page:OnShow({ reason = 'KillLimit', kills = 5, deaths = 2, rank = 2,
              ranks = { { rank = 1, name = '玩家1', kills = 6, deaths = 1 },
                        { rank = 2, name = '玩家2', kills = 5, deaths = 2 },
                        { rank = 2, name = '玩家3', kills = 5, deaths = 4 } } })");
                string calls = Calls(env);
                StringAssert.Contains("Visible:Txt_Score=true", calls, "本局有名次 → 显示");
                StringAssert.Contains("KeyArgs:Txt_Score=UI.Result.Rank(2,nil)", calls, "名次文案键");
                StringAssert.Contains("List:VirtualList_Rankboard=3", calls, "玩家排行行填充");
                string row = (string)env.DoString(@"
local r = lists['VirtualList_Rankboard'][3]
return r.key .. '|' .. r.Txt_Rank .. '|' .. r.Txt_Name .. '|' .. r.Txt_Kills.key .. '|' .. tostring(r.Txt_Kills.arg0)
    .. '|' .. r.Txt_Deaths.key .. '|' .. tostring(r.Txt_Deaths.arg0)")[0];
                Assert.AreEqual("3|2|玩家3|UI.Result.Kills|5|UI.Result.Deaths|4", row,
                    "行字段：名次/玩家名为数据 + 击杀与阵亡走文案键带参");

                env.DoString(@"
page2 = NewPage('UI.Page_Result')
page2:OnShow({ reason = 'TimeLimit', kills = 1, deaths = 3, rank = 0 })");
                StringAssert.Contains("Visible:Txt_Score=false", Calls(env), "rank=0（无名次面）→ 隐藏");
            }
        }

        [Test]
        public void 房间页_等待人数读本房席位面()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
room.expected = 4
page = NewPage('UI.Page_Room')
page:OnShow({ roomId = 'Room-A' })");
                StringAssert.DoesNotContain("Visible:Txt_State=true", Calls(env),
                    "未收到席位快照（本机在座 ⇒ 空名单只可能是未到）→ 保持隐藏，不显假数");

                env.DoString(@"
calls = {}
room.members_fn({ { playerId = 0, name = '玩家1', online = true } })");
                string one = Calls(env);
                StringAssert.Contains("Visible:Txt_State=true", one);
                StringAssert.Contains("KeyArgs:Txt_State=UI.Room.Waiting(1,4)", one,
                    "分子 = 席位面条数、分母 = 房规模（JoinAck）");

                env.DoString(@"
calls = {}
room.members_fn({ { playerId = 0, name = '玩家1', online = true },
                  { playerId = 1, name = '玩家2', online = true } })");
                StringAssert.Contains("KeyArgs:Txt_State=UI.Room.Waiting(2,4)", Calls(env),
                    "第二人进房 → 席位广播即刷（不再等心跳/重查大厅投影）");

                env.DoString(@"
calls = {}
room.members_fn({ { playerId = 0, name = '玩家1', online = true },
                  { playerId = 1, name = '玩家2', online = false } })");
                StringAssert.Contains("KeyArgs:Txt_State=UI.Room.Waiting(2,4)", Calls(env),
                    "断线席位仍占座 ⇒ 计数不因一次掉线回退（与名单同帧）");

                env.DoString(@"
calls = {}
room.expected = 0
room.members_fn({ { playerId = 0, name = '玩家1', online = true } })");
                string noExpected = Calls(env);
                StringAssert.Contains("Visible:Txt_State=false", noExpected, "房规模未标注（0）→ 隐藏");
                StringAssert.DoesNotContain("KeyArgs:Txt_State", noExpected, "不显 /0 假数");
            }
        }

        // ---- 房主开局（HUD 批C·房主开局批：Bridge.room 订阅 + 防重入 + 被拒反馈 + 迁移换归属）----

        [Test]
        public void 房间页_房主回放亮钮_点击防重入_被拒Toast复位()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Room')
page:OnShow({ roomId = 'Room-A' })");
                string init = Calls(env);
                StringAssert.Contains("Visible:Button_StartMatch=false", init, "缺省隐藏（回放前不显）");
                StringAssert.Contains("Key:Txt_StartMatch=UI.Room.StartMatch", init, "开局钮文案键");

                env.DoString("room.owner_fn(true)");          // 房主事实回放（门面 OnOwner）
                StringAssert.Contains("Visible:Button_StartMatch=true", Calls(env), "房主回放亮钮");

                env.DoString("buttons['Button_StartMatch']()");
                string clicked = Calls(env);
                StringAssert.Contains("StartMatch", clicked, "点击 → Bridge.room.StartMatch");
                StringAssert.Contains("Interact:Button_StartMatch=false", clicked, "在途锁钮（防重入）");

                env.DoString("room.start_fn(false, '只有房主可以开始对局')");
                string denied = Calls(env);
                StringAssert.Contains("Interact:Button_StartMatch=true", denied, "被拒回调后复位可再点");
                StringAssert.Contains("Toast:[UI.Room.StartDenied:只有房主可以开始对局]", denied,
                    "被拒 reason 经 Toast（UI.Room.StartDenied 带参——五页反馈口径）");
            }
        }

        [Test]
        public void 房间页_非房主隐藏_迁移失位同路径摘钮()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Room')
page:OnShow({ roomId = 'Room-A' })
room.owner_fn(false)");
                Assert.AreEqual(0, Regex.Matches(Calls(env), @"Visible:Button_StartMatch=true").Count,
                    "非房主：开钮全程不亮");

                env.DoString("room.owner_fn(true)");           // 迁移到位（新主）
                Assert.AreEqual(1, Regex.Matches(Calls(env), @"Visible:Button_StartMatch=true").Count,
                    "迁移到位亮钮（同一订阅路径）");

                env.DoString("room.owner_fn(false)");          // 再迁移失位（旧主让位）
                Assert.AreEqual(1, Regex.Matches(Calls(env), @"Visible:Button_StartMatch=true").Count,
                    "失位即摘钮——迁移推送与回放同一路径，不残留");
            }
        }

        [Test]
        public void 房间页_OnHide退订房主事实与席位名单()
        {
            using (var env = NewEnv())
            {
                env.DoString(@"
page = NewPage('UI.Page_Room')
page:OnShow({ roomId = 'Room-A' })
page:OnHide()
assert(room.owner_fn == nil, 'OnHide 应退订房主事实（门面持引用纪律——页面关闭即放手）')
assert(room.members_fn == nil, 'OnHide 应退订席位名单（同上）')");
                Assert.Pass("退订断言在 Lua 侧（owner_fn/members_fn == nil）");
            }
        }

        // ---- 文案键守卫（页面脚本 ⊆ 表源）----

        [Test]
        public void 文案键守卫_页面脚本引用的键全在表_含结束原因值域()
        {
            string uiDir = Path.Combine(Application.dataPath, "LiteGame/Lua/UI");
            var keys = new HashSet<string>();
            foreach (string file in Directory.GetFiles(uiDir, "Page_*.lua"))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"((?:UI|Common)\\.[A-Za-z0-9_.]+)\""))
                {
                    string key = m.Groups[1].Value;
                    // 页面路径（UI.Page_*）不是文案键；以 '.' 结尾的是拼键前缀（值域由下方枚举检查覆盖）
                    if (key.StartsWith("UI.Page_", StringComparison.Ordinal)) continue;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;
                    keys.Add(key);
                }
            }

            Assert.IsNotEmpty(keys, "至少应扫到页面文案键——扫不到说明正则与脚本写法脱节");

            var tables = LoadRealTables();
            foreach (string key in keys)
                Assert.IsNotNull(tables.Tbltext.GetOrDefault(key), $"页面脚本引用了未登记的文案键：{key}");

            // 结束原因值域 ↔ 键（枚举名拼键口径：Lua 不抄枚举值）
            foreach (LiteSim.MatchEndReason reason in Enum.GetValues(typeof(LiteSim.MatchEndReason)))
            {
                if (reason == LiteSim.MatchEndReason.None) continue;
                Assert.IsNotNull(tables.Tbltext.GetOrDefault("UI.Result.Reason." + reason),
                    $"结束原因缺文案键：UI.Result.Reason.{reason}");
            }
        }

        private static cfg.Tables LoadRealTables()
        {
            // 与 ConfigService 同款装载（同 LTextWiringEditModeTests：直读磁盘——YooAsset 异步拉不起，清单单源 TableLayout）
            string dir = Path.Combine(ProjectRoot(), ConfigService.DataDir);
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string file in ConfigService.TableDataFiles)
                cache[TableLayout.TableName(file)] = File.ReadAllBytes(Path.Combine(dir, file + ".bytes"));
            return new cfg.Tables(file => new ByteBuf(cache[file]));
        }

        private static string ProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            throw new DirectoryNotFoundException("找不到项目根");
        }
    }
}
