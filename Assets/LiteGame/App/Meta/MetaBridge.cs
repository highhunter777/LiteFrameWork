using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using XLua; // lint-allow R12：联机门面本体住 App（须引用 MetaEntryService——LuaBridge 不引用 App，依赖方向不可反），本体只做 env 装订/行展开

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
    /// Bridge.Data §1d 同判据）。页面导航仍由 Lua 经 <c>Bridge.ui</c> 驱动（进房成功后开 Page_Room 归页面接线批 C-④）。
    /// </summary>
    public static class MetaBridge
    {
        private static MetaEntryService s_meta;

        /// <summary>装配期注入编排件（ContainerModule——未配置部署时也注入：IsConfigured=false 由页面显性拒绝）。</summary>
        public static void Bind(MetaEntryService meta)
        {
            s_meta = meta ?? throw new ArgumentNullException(nameof(meta));
        }

        /// <summary>装订 meta 门面进全局 Bridge 表（ProductBridges 调用——在框架四门面之后）。</summary>
        public static void BindGlobals(LuaEnv env)
        {
            var meta = env.NewTable();
            meta.Set("IsConfigured", new Func<bool>(() => IsConfigured));
            meta.Set("AccountId", new Func<string>(() => AccountId));
            meta.Set("ServerUrl", new Func<string>(() => ServerUrl));
            meta.Set("GuestLogin", new Action<LuaFunction>(GuestLogin));
            meta.Set("ListRooms", new Action<LuaFunction>(cb => ListRooms(env, cb)));
            meta.Set("QuickEnter", new Action<LuaFunction>(QuickEnter));
            meta.Set("EnterRoom", new Action<string, LuaFunction>(EnterRoom));
            meta.Set("ListRecords", new Action<LuaFunction>(cb => ListRecords(env, cb)));
            env.Global.Get<LuaTable>("Bridge").Set("meta", meta);
        }

        public static bool IsConfigured => s_meta != null && s_meta.IsConfigured;
        public static string AccountId => s_meta != null ? s_meta.AccountId : "";
        public static string ServerUrl => s_meta != null ? s_meta.ServerUrl : "";

        /// <summary>战绩查询条数（《上云测试专项设计》§4 裁决点 ③"最近 N 局"——服务端上限 100 裁剪）。</summary>
        public const int RecordsLimit = 20;

        public static void GuestLogin(LuaFunction cb)
            => Run(s_meta.GuestLoginAsync(), cb, r => r.Value.AccountId).Forget();

        public static void ListRooms(LuaEnv env, LuaFunction cb)
            => Run(s_meta.ListRoomsAsync(), cb, r => RoomsTable(env, r.Value)).Forget();

        public static void QuickEnter(LuaFunction cb)
            => Run(s_meta.EnterRoomAsync(""), cb, _ => true).Forget();

        public static void EnterRoom(string roomId, LuaFunction cb)
            => Run(s_meta.EnterRoomAsync(roomId), cb, _ => true).Forget();

        public static void ListRecords(LuaEnv env, LuaFunction cb)
            => Run(s_meta.ListRecordsAsync(RecordsLimit), cb, r => RecordsTable(env, r.Value)).Forget();

        /// <summary>统一异步收口：恰好一次回调；异常不外抛（转 reason）——页面回调不得炸壳。
        /// fire-and-forget 显式化（.Forget()——CS4014 收口，Bridge.Ui.Close 同款）。</summary>
        private static async UniTask Run<T>(UniTask<MetaCall<T>> task, LuaFunction cb,
            Func<MetaCall<T>, object> okPayload)
        {
            try
            {
                MetaCall<T> call = await task;
                Done(cb, call.Ok, call.Ok ? okPayload(call) : (object)Reason(call.Failure));
            }
            catch (Exception ex)
            {
                Log.Error($"Bridge.meta 异常:{ex.Message}", "Meta");
                Done(cb, false, "Internal");
            }
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

        /// <summary>房间列表 → LuaTable 数组（行字段只取页面消费面：roomId/players/expectedPlayers）。</summary>
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
                    rows.Set(index++, row);
                }
            }
            return rows;
        }

        /// <summary>战绩 → LuaTable 数组（结束原因原值——页面按枚举值取文案键）。</summary>
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
                row.Set("endReason", record.EndReason);
                row.Set("gameplayEndReason", record.GameplayEndReason);
                row.Set("finishedAtMs", record.FinishedAtMs);
                rows.Set(index++, row);
            }
            return rows;
        }
    }
}
