using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;
using LiteClient;
using XLua; // lint-allow R12：结算页数据表（LuaUIData）的构造点在流程侧（数据源=对局冻结结果，App 持有；LuaBridge 不引用 App，依赖方向不可反）——与 MetaBridge 同判据

namespace LiteGame
{
    /// <summary>
    /// 五页页面流（批C-④，《上云测试页面层级设计》§1 页面关系）：页面寻址
    /// （lua_path → tbuiform id，单源 <see cref="Bridge.Data.ResolveUIFormId"/>）与**流程侧三处页面切换**——
    /// 首进 Main 开登录页；对局开局（StartGame）关房间页；对局结束开结算页（失败/离场回大厅）。
    ///
    /// **职责边界**：页面→页面 的跳转（登录成功→大厅、进房→房间、返回大厅、设置叠加）由 Lua 经
    /// <c>Bridge.ui</c> 驱动（同一导航单写者）；本件只接流程不认识 UI、UI 不认识流程的那三处接缝——
    /// 流程层（ProcedureMain/ProcedureBattle）持有本件，UI 层不知道它存在。
    /// </summary>
    public sealed class PageFlow
    {
        private readonly UINavigationController _nav;
        private readonly UIService _ui;
        private readonly Func<LuaEnv> _env;                 // 结算页数据表构造（LuaUIData）——懒取（env 随 DevReload 重建）

        private bool _entryOpened;

        public PageFlow(UINavigationController nav, UIService ui, LuaComponent lua)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            LuaComponent host = lua ?? throw new ArgumentNullException(nameof(lua));
            _env = () => host.Env;
        }

        /// <summary>首进 Main 开登录页（五页入口）。**只一次**——返回 Main 的其它路径（对局结束/离场）
        /// 由页面流自身接管（结算页/大厅在场），重开入口会把已在大厅的玩家弹回登录页。</summary>
        public void OpenEntryOnce()
        {
            if (_entryOpened) return;
            _entryOpened = true;
            Go(LuaKeys.UI.Page_Login, null);
        }

        /// <summary>对局开局（StartGame 已到）关房间页——对局场景接管画面（房间页若在），
        /// 页面栈随之清空（结算页压栈即新根）。</summary>
        public void CloseRoomIfOpen() => CloseIfOpen(LuaKeys.UI.Page_Room);

        /// <summary>
        /// 对局结束（BattleContext.Ended）：正常结束（Completed + 冻结结果在）→ 结算页（带本局结果）；
        /// 失败/主动离场 → 房间页在则替换回大厅（"断开 + 返回大厅"；房间页不在 = 无从谈回大厅，留在现状）。
        /// </summary>
        public void OnMatchEnded(bool completed, LiteNet.Proto.MatchEnded result, int localPlayerId)
        {
            if (completed && result != null)
            {
                Go(LuaKeys.UI.Page_Result, new LuaUIData(ResultData(result, localPlayerId)));
                return;
            }

            if (IsOpen(LuaKeys.UI.Page_Room)) Replace(LuaKeys.UI.Page_Lobby, null);
        }

        /// <summary>本局结果 → 结算页数据表（Lua 侧只认具名键；结束原因取枚举名——页面按值域拼文案键
        /// `UI.Result.Reason.&lt;名&gt;`，不在 Lua 侧抄枚举值）。**名次 rank**：FFA 无 score 字段，
        /// 按冻结结果 players[] 击杀序推导（1 + 击杀严格大于本机的人数；并列同名次；本机不在册 = 0 = 页面隐藏）。</summary>
        private LuaTable ResultData(LiteNet.Proto.MatchEnded result, int localPlayerId)
        {
            int kills = 0, deaths = 0, rank = 0;
            bool found = false;
            if (result.Players != null)
            {
                // 两遍式（先定本机行，再数"击杀严格大于本机"的人数——单遍会依赖行序）
                foreach (LiteNet.Proto.PlayerMatchResult row in result.Players)
                {
                    if (row.PlayerId != localPlayerId) continue;
                    kills = row.Kills;
                    deaths = row.Deaths;
                    found = true;
                    break;
                }
                if (found)
                {
                    int better = 0;
                    foreach (LiteNet.Proto.PlayerMatchResult row in result.Players)
                        if (row.PlayerId != localPlayerId && row.Kills > kills) better++;
                    rank = better + 1;                         // 并列同名次
                }
            }

            LuaTable table = _env().NewTable();
            table.Set("hasPlayer", found);
            table.Set("kills", kills);
            table.Set("deaths", deaths);
            table.Set("rank", rank);                           // 0 = 无名次面（页面隐藏 Txt_Score）
            table.Set("reason", ((LiteSim.MatchEndReason)result.GameplayEndReason).ToString());
            return table;
        }

        private void Go(string luaPath, IUIData data)
        {
            if (!TryResolve(luaPath, "Go", out int id)) return;
            var aw = _nav.GoAsync(id, data).GetAwaiter();
            aw.OnCompleted(() => Observe(aw, "Go", luaPath));
        }

        private void Replace(string luaPath, IUIData data)
        {
            if (!TryResolve(luaPath, "Replace", out int id)) return;
            var aw = _nav.ReplaceAsync(id, data).GetAwaiter();
            aw.OnCompleted(() => Observe(aw, "Replace", luaPath));
        }

        private void CloseIfOpen(string luaPath)
        {
            if (!TryResolve(luaPath, "Close", out int id) || !_ui.IsOpen(id)) return;
            _ui.CloseAsync(id).Forget();
        }

        private bool IsOpen(string luaPath)
            => TryResolve(luaPath, "IsOpen", out int id) && _ui.IsOpen(id);

        /// <summary>lua_path → id；未登记 = 装配错误（表漏登记）→ 错误日志 + 跳过（不炸流程）。</summary>
        private static bool TryResolve(string luaPath, string method, out int id)
        {
            id = Bridge.Data.ResolveUIFormId(luaPath);
            if (id < 0) Log.Error($"PageFlow.{method}: 未登记的 lua_path '{luaPath}'（核对 tbuiform）", "UI");
            return id >= 0;
        }

        private static void Observe(UniTask<UIForm>.Awaiter aw, string method, string luaPath)
        {
            try { aw.GetResult(); }
            catch (Exception ex) { Log.Error($"PageFlow.{method}[{luaPath}]:{ex.Message}", "UI"); }
        }
    }
}
