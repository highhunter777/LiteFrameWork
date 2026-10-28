using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using LiteFramework;
using XLua; // lint-allow R12：联机门面本体住 App（须引用 MetaEntryService——LuaBridge 不引用 App，依赖方向不可反），本体只做 env 装订/行展开
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 联机门面（Bridge.meta，批C-③）：《上云测试专项设计》§2 客户端闭环的 Lua 侧入口。
    /// 住 App 侧（服务编排件在 App——LuaBridge 程序集不引用 App，依赖方向不可反），
    /// 经 <see cref="ProductBridges.BindGlobals"/> 与框架四门面同 env 装订。
    ///
    /// **回调契约**：每个异步入口回调**恰好一次**（主线程续延），首参 ok:bool，成功随结果形状、
    /// 失败随 reason:string（稳定错误码 / "Network" / "InvalidResponse"——不含敏感原文）。
    /// **令牌/票据不出桥**：服务面只回 accountId 与投影形状（房间列表/战绩行手工展开成 LuaTable——
    /// Bridge.Data §1d 同判据）。页面导航由 Lua 经 <c>Bridge.ui</c> 驱动（按 lua_path 寻址）；
    /// 流程侧三处页面切换（首进开入口 / 开局关房间页 / 结束开结算页）经 <see cref="PageFlow"/>（批C-④）。
    /// </summary>
    public static class MetaBridge
    {
        private static MetaEntryService s_meta;
        private static IdentityContext s_identity;
        private static Func<cfg.Tables> s_tables;      // 地图表（多图批：房间行地图名显示面；可空 = 不解析名）
        private static IAccountReentry s_reentry;      // 凭证失效（401）→ 账号重入单点（可空 = 未接）

        /// <summary>装配期注入编排件（ContainerModule——未配置部署时也注入：IsConfigured=false 由页面显性拒绝）。</summary>
        public static void Bind(MetaEntryService meta, IdentityContext identity = null,
            Func<cfg.Tables> tables = null, IAccountReentry reentry = null)
        {
            s_meta = meta ?? throw new ArgumentNullException(nameof(meta));
            s_identity = identity;
            s_tables = tables;
            s_reentry = reentry;
        }

        /// <summary>装订 meta 门面进全局 Bridge 表（ProductBridges 调用——在框架四门面之后）。</summary>
        public static void BindGlobals(LuaEnv env)
        {
            var meta = env.NewTable();
            meta.Set("IsConfigured", new Func<bool>(() => IsConfigured));
            meta.Set("AccountId", new Func<string>(() => AccountId));
            meta.Set("ServerUrl", new Func<string>(() => ServerUrl));
            meta.Set("Version", new Func<string>(() => Version));
            meta.Set("LeaveMatch", new Action(LeaveMatch));
            meta.Set("GuestLogin", new Action<LuaFunction>(GuestLogin));
            meta.Set("ListRooms", new Action<LuaFunction>(cb => ListRooms(env, cb)));
            meta.Set("QuickEnter", new Action<LuaFunction>(QuickEnter));
            meta.Set("CreateRoom", new Action<int, int, LuaFunction>((mapId, expectedPlayers, cb)
                => CreateRoom(mapId, expectedPlayers, cb)));
            meta.Set("EnterRoom", new Action<string, LuaFunction>(EnterRoom));
            meta.Set("ListRecords", new Action<LuaFunction>(cb => ListRecords(env, cb)));
            env.Global.Get<LuaTable>("Bridge").Set("meta", meta);
        }

        public static bool IsConfigured => s_meta != null && s_meta.IsConfigured;
        public static string AccountId => s_meta != null ? s_meta.AccountId : "";
        public static string ServerUrl => s_meta != null ? s_meta.ServerUrl : "";

        /// <summary>客户端构建身份（buildHash）：页面版本展示（`Common.Version {0}`，登录/设置页）——与 Join 握手同值。</summary>
        public static string Version => s_identity != null ? s_identity.Build.BuildHash : string.Empty;

        /// <summary>离房/离局（Page_Room「离开」）：请求流程机回 Main——Match/Battle 阶段由既有管道取消、
        /// 资源在各自 finally 收尾；页面侧再导航替换回 Page_Lobby（"断开 + 返回大厅"，《页面层级设计》§3）。</summary>
        public static void LeaveMatch() => s_meta?.LeaveMatch();

        /// <summary>战绩查询条数（《上云测试专项设计》§4 裁决点 ③"最近 N 局"——服务端上限 100 裁剪）。</summary>
        public const int RecordsLimit = 20;

        public static void GuestLogin(LuaFunction cb)
            => Run(s_meta.GuestLoginAsync(), cb, r => r.Value.AccountId).ForgetLogged("MetaBridge");

        public static void ListRooms(LuaEnv env, LuaFunction cb)
            => Run(s_meta.ListRoomsAsync(), cb, r => RoomsTable(env, r.Value)).ForgetLogged("MetaBridge");

        /// <summary>快速进入（空 roomId = 服务端缺省房/容量分配）。成功载荷 = 换票结果的房号（房间页展示用）。</summary>
        public static void QuickEnter(LuaFunction cb)
            => Run(s_meta.EnterRoomAsync(""), cb, r => r.Value.RoomId).ForgetLogged("MetaBridge");

        /// <summary>创建房间（服务端出唯一新号 → 票据即达，客户端不发明房号）。成功载荷 = 新房号
        /// （房间页展示用）——与 <see cref="QuickEnter"/>/<see cref="EnterRoom"/> 同：成功后流程机接管进房。
        /// <paramref name="mapId"/> 0 = 未指定（宿主默认图）、<paramref name="expectedPlayers"/> 0 = 未指定
        /// （按房间模板）——（开房参数批）两值随票据盖章，房间端建房时校验（越界拒建房）。</summary>
        public static void CreateRoom(int mapId, int expectedPlayers, LuaFunction cb)
            => Run(s_meta.CreateRoomAsync(mapId, expectedPlayers), cb, r => r.Value.RoomId).ForgetLogged("MetaBridge");

        /// <summary>选择式进房（房间列表行点击——列表行协议挂批C-⑥）。成功载荷 = 房号（同 QuickEnter）。</summary>
        public static void EnterRoom(string roomId, LuaFunction cb)
            => Run(s_meta.EnterRoomAsync(roomId), cb, r => r.Value.RoomId).ForgetLogged("MetaBridge");

        public static void ListRecords(LuaEnv env, LuaFunction cb)
            => Run(s_meta.ListRecordsAsync(RecordsLimit), cb, r => RecordsTable(env, r.Value)).ForgetLogged("MetaBridge");

        /// <summary>统一异步收口：恰好一次回调；异常不外抛（转 reason）——页面回调异常不得外溢。
        /// fire-and-forget 显式化（.Forget()——CS4014 收口，Bridge.Ui.Close 同款）。</summary>
        private static async UniTask Run<T>(UniTask<MetaCall<T>> task, LuaFunction cb,
            Func<MetaCall<T>, object> okPayload)
        {
            try
            {
                MetaCall<T> call = await task;
                Done(cb, call.Ok, call.Ok ? okPayload(call) : (object)Reason(call.Failure));
                // 凭证失效 = 恢复链的"重新登录"（先回报页面失败，再走单点重入——不拿旧令牌反复重试）
                if (!call.Ok && call.Failure.IsAuthLost)
                    await ReenterAsync("meta." + Reason(call.Failure));
            }
            catch (Exception ex)
            {
                Log.Error($"Bridge.meta 异常:{ex.Message}", LogTag.Meta);
                Done(cb, false, "Internal");
            }
        }

        /// <summary>账号重入（失败不外溢——页面回调已回报，重入只是恢复动作）。</summary>
        private static async UniTask ReenterAsync(string reason)
        {
            if (s_reentry == null) return;
            try { await s_reentry.ReturnToLoginAsync(reason); }
            catch (Exception ex) { Log.Error($"账号重入失败:{ex.Message}", LogTag.Meta); }
        }

        private static void Done(LuaFunction cb, bool ok, object payload)
        {
            if (cb == null) return;
            SafeCall.Invoke(() => cb.Call(ok, payload), "Bridge.meta");
        }

        private static string Reason(MetaFailure failure)
        {
            switch (failure.Kind)
            {
                case MetaFailureKind.Network: return "Network";
                case MetaFailureKind.InvalidResponse: return "InvalidResponse";
                default:
                    return string.IsNullOrEmpty(failure.Code)
                        ? "HTTP" + failure.HttpStatus
                        : failure.Code;
            }
        }

        /// <summary>房间列表 → LuaTable 数组（行字段只取页面消费面：roomId/players/expectedPlayers/地图）。
        /// 多图批：`mapId` = 上报行键，`mapName` = 客户端按地图表 `name` 列解析（表未装载/缺行 → 空串——
        /// 显示面不因缺名失败；判定面不读本字段）。</summary>
        private static LuaTable RoomsTable(LuaEnv env, IReadOnlyList<LobbyInstanceView> instances)
        {
            var rows = env.NewTable();
            int index = 1;
            foreach (LobbyInstanceView instance in instances)
            {
                if (instance.Rooms == null) continue;
                foreach (LobbyRoomEntry room in instance.Rooms)
                {
                    var row = env.NewTable();
                    row.Set("roomId", room.RoomId);
                    row.Set("players", room.Players);
                    row.Set("expectedPlayers", room.ExpectedPlayers);
                    row.Set("mapId", room.MapId);
                    row.Set("mapName", MapDisplayName(room.MapId));
                    rows.Set(index++, row);
                }
            }
            return rows;
        }

        /// <summary>地图行键 → 显示名（地图表 `name` 列；0/缺行/表未装载 = 空串——纯显示面，不抛）。</summary>
        private static string MapDisplayName(int mapId)
        {
            if (mapId <= 0 || s_tables == null) return string.Empty;
            cfg.map row = s_tables().Tbmap?.GetOrDefault(mapId);
            return row?.Name ?? string.Empty;
        }

        /// <summary>本地时间显示（**数据面在 C#**：Lua 受控沙箱不依赖 os 库做时间格式化；
        /// 恒 InvariantCulture 防区域小数点/时制漂移，值 0 = 无时间面）。</summary>
        private static string FormatTime(long finishedAtMs)
            => finishedAtMs <= 0
                ? string.Empty
                : DateTimeOffset.FromUnixTimeMilliseconds(finishedAtMs).ToLocalTime()
                    .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>战绩 → LuaTable 数组（结束原因给**枚举名**——页面按值域拼文案键 `UI.Result.Reason.&lt;名&gt;`，
        /// Lua 侧不抄枚举值；int 原值一并保留供诊断）。</summary>
        private static LuaTable RecordsTable(LuaEnv env, IReadOnlyList<MatchRecordInfo> records)
        {
            var rows = env.NewTable();
            int index = 1;
            foreach (MatchRecordInfo record in records)
            {
                var row = env.NewTable();
                row.Set("matchId", record.MatchId);
                row.Set("kills", record.Kills);
                row.Set("deaths", record.Deaths);
                row.Set("reason", ((LiteSim.MatchEndReason)record.GameplayEndReason).ToString());
                row.Set("time", FormatTime(record.FinishedAtMs));
                row.Set("endReason", record.EndReason);
                row.Set("gameplayEndReason", record.GameplayEndReason);
                row.Set("finishedAtMs", record.FinishedAtMs);
                rows.Set(index++, row);
            }
            return rows;
        }
    }
}
