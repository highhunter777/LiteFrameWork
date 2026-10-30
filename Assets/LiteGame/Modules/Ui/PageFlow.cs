using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using XLua; // lint-allow R12：页面数据表（LuaUIData，模块层包装）与结算数据构造点在流程侧——与 MetaBridge/ProductSettings 同判据（模块自持 Lua 门面形）

namespace LiteGame
{
    /// <summary>
    /// 五页页面流（批C-④，《上云测试页面层级设计》§1 页面关系）：页面寻址
    /// （lua_path → PageDefinition id，单源 <see cref="UI.UiPageResolver.Resolve"/>）与**流程侧页面切换**——
    /// 首进 Main 开登录页；对局开局（StartGame）关房间页；对局结束开结算页（失败/离场回大厅）；
    /// 对局中打开设置页（HUD 菜单钮 / 空栈 Back 的对局菜单语义——导航数据 <c>battleExit</c> 供
    /// 设置页门控"退出对局"按钮）。
    ///
    /// **职责边界**：页面→页面 的跳转（登录成功→大厅、进房→房间、返回大厅、设置叠加）由 Lua 经
    /// <c>Bridge.ui</c> 驱动（同一导航单写者）；本件只接流程不认识 UI、UI 不认识流程的那几处接缝——
    /// 流程层（ProcedureMain/ProcedureBattle）持有本件，UI 层不知道它存在。
    /// </summary>
    public sealed class PageFlow : ILoginSurface
    {
        private readonly UINavigationController _nav;
        private readonly UIService _ui;
        private readonly Func<LuaEnv> _env;                 // 结算页数据表构造（LuaUIData）——懒取（env 随 DevReload 重建）

        private bool _entryOpened;
        private Landing _landing;                            // 启动落点（对局失败回大厅的语境判据：dev 直入不强行开大厅）
        private Func<UniTask<bool>> _savedPlatformBack;      // 对局菜单 Back 策略启用前的原装订（还原用）

        public PageFlow(UINavigationController nav, UIService ui, LuaComponent lua)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            LuaComponent host = lua ?? throw new ArgumentNullException(nameof(lua));
            _env = () => host.Env;
        }

        /// <summary>启动落点（首进 Main 的开页决策；《UI工作台设计》§5 入口矩阵）。</summary>
        public enum Landing
        {
            /// <summary>不开页：dev 直入形态（直入训练场/直入工作台）——对应场景/面板已接管画面。</summary>
            None,

            /// <summary>登录页：正式形态（Meta 已配置）。</summary>
            Login,

            /// <summary>大厅页：本地服务器/开发形态——跳过登录步骤（房间列表按服务可用性显文案）。</summary>
            Lobby,
        }

        /// <summary>首进 Main 按**落点**开页。**只一次**——返回 Main 的其它路径（对局结束/离场）
        /// 由页面流自身接管（结算页/大厅在场），重开入口会把已在大厅的玩家弹回登录页。</summary>
        public void OpenLanding(Landing landing)
        {
            _landing = landing;
            if (_entryOpened) return;
            _entryOpened = true;
            switch (landing)
            {
                case Landing.Login: Go(LuaKeys.UI.Page_Login, null); break;
                case Landing.Lobby: Go(LuaKeys.UI.Page_Lobby, null); break;
                default: break;                                        // None：直入形态，场景/面板接管
            }
        }

        /// <summary>登录页是否已在场（账号重入的幂等判据）。</summary>
        public bool IsLoginOpen => IsOpen(LuaKeys.UI.Page_Login);

        /// <summary>
        /// 账号重入（恢复链单点）：把页面栈替换为登录页——**忽略启动落点的一次性守卫**
        /// （<see cref="OpenLanding"/> 只在首进 Main 开一次页；登录失效发生在运行期，必须能再次出现）。
        /// 重入的登录态清理/离场由 <see cref="AccountReentry"/> 先行完成，本方法只做导航。
        /// </summary>
        public void ShowLogin(string reason)
        {
            _landing = Landing.Login;
            Log.Info($"账号重入：回登录页（{reason}）", LogTag.UI);
            Replace(LuaKeys.UI.Page_Login, null);
        }

        /// <summary>进对局前关掉在开的页面（**dev 测试入口**路径用：从大厅/开发控制台直接进局时，
        /// 页面会盖住对局 HUD——产品路径由"房间页→开局"自然收口，此处补 dev 入口的同一语义）。
        /// 幂等：无页面 = no-op。</summary>
        public void CloseOpenPagesForBattle() => _ui.CloseAllOpen().ForgetLogged("PageFlow");

        /// <summary>对局开局（StartGame 已到）关房间页——对局场景接管画面（房间页若在），
        /// 页面栈随之清空（结算页压栈即新根）。</summary>
        public void CloseRoomIfOpen() => CloseIfOpen(LuaKeys.UI.Page_Room);

        /// <summary>
        /// 对局结束（BattleContext.Ended）：正常结束（Completed + 冻结结果在）→ 结算页（带本局结果）；
        /// 失败/主动离场 → **回大厅**（"断开 + 返回大厅"）。回大厅不依赖房间页是否还开着：
        /// 对局中房间页已随开局关闭，会话失败（RoomClient Failed → BattleContext SessionFailed）后
        /// 若无页面可回，玩家会停在 Main 的空栈上——故空栈也开大厅；dev 直入（Landing.None）无大厅语境，不强行开。
        /// </summary>
        public void OnMatchEnded(bool completed, LiteNet.Proto.MatchEnded result, int localPlayerId)
        {
            // 对局菜单随对局结束收口：菜单开着时自然结束/离场——结算页/大厅须直接可见，不被设置页压着
            CloseIfOpen(LuaKeys.UI.Page_Settings);
            if (completed && result != null)
            {
                Go(LuaKeys.UI.Page_Result, new LuaUIData(ResultData(result, localPlayerId)));
                return;
            }

            ReturnToLobby();
        }

        /// <summary>失败/离场统一回大厅（已在大厅 = no-op；dev 直入不涉大厅）。</summary>
        private void ReturnToLobby()
        {
            if (_landing == Landing.None && !IsOpen(LuaKeys.UI.Page_Lobby)) return;
            if (IsOpen(LuaKeys.UI.Page_Lobby)) return;
            Replace(LuaKeys.UI.Page_Lobby, null);
        }

        /// <summary>对局中打开设置页（HUD 菜单钮与空栈 Back 共用入口）：导航数据带 <c>battleExit</c>——
        /// 设置页按它门控"退出对局"按钮（登录/大厅/结算的设置入口不带数据，不显退场钮）。</summary>
        public void OpenBattleSettings()
        {
            LuaTable table = _env().NewTable();
            table.Set("battleExit", true);
            Go(LuaKeys.UI.Page_Settings, new LuaUIData(table));
        }

        /// <summary>启用对局菜单 Back 策略（对局视图建立后启用、离场收尾停用）：平台返回先走原 Back 链
        /// （Loading 拦截/最顶弹窗/设置页照常关闭），空栈（无可返回目标）时打开设置页——对局中
        /// "ESC＝菜单"语义。替换 <see cref="UIService"/> 的平台返回装订并保存原值；停用时还原
        /// （若期间被外部改写则不覆盖——只认自己的包装）。</summary>
        public void EnableBattleMenuBack()
        {
            if (_ui.Focus.PlatformBack == BattleBack) return;        // 幂等（重复启用不叠加包装）
            _savedPlatformBack = _ui.Focus.PlatformBack;
            _ui.Focus.PlatformBack = BattleBack;
        }

        /// <summary>停用对局菜单 Back 策略并还原原装订（对局收尾必调——ESC 不得在对局外开设置页）。</summary>
        public void DisableBattleMenuBack()
        {
            if (_ui.Focus.PlatformBack == BattleBack)
                _ui.Focus.PlatformBack = _savedPlatformBack;
            _savedPlatformBack = null;
        }

        /// <summary>对局菜单平台返回：先走原 Back 链，无可返回目标（false / 未装订）才开设置页——
        /// 两条路径都消费本次返回。</summary>
        private async UniTask<bool> BattleBack()
        {
            Func<UniTask<bool>> original = _savedPlatformBack;
            if (original != null && await original()) return true;
            OpenBattleSettings();
            return true;
        }

        /// <summary>本局结果 → 结算页数据表（Lua 侧只认具名键；结束原因取枚举名——页面按值域拼文案键
        /// `UI.Result.Reason.&lt;名&gt;`，不在 Lua 侧抄枚举值）。**名次与玩家排行同源**：都取自
        /// <see cref="ResultRanking.Build"/> 的同一份行表（击杀降序 → EntityId 升序；名次 = 1 + 击杀严格
        /// 大于本机的人数，并列同名次；名字 = 玩家{k}）。本机不在册 = rank 0（页面隐藏 Txt_Score）。
        /// `ranks`＝排行行数组（`{rank, name, kills, deaths}`）——供结算页列表（原 Cot_Records 位）填充。</summary>
        private LuaTable ResultData(LiteNet.Proto.MatchEnded result, int localPlayerId)
        {
            ResultRanking.Row[] rows = ResultRanking.Build(result.Players);

            // 本机行（本局摘要）与排行同一份行表：名次/击杀/阵亡单源，页面侧不重算
            bool found = false;
            int kills = 0, deaths = 0, rank = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].PlayerId != localPlayerId) continue;
                kills = rows[i].Kills;
                deaths = rows[i].Deaths;
                rank = rows[i].Rank;
                found = true;
                break;
            }

            LuaTable table = _env().NewTable();
            table.Set("hasPlayer", found);
            table.Set("kills", kills);
            table.Set("deaths", deaths);
            table.Set("rank", rank);                           // 0 = 无名次面（页面隐藏 Txt_Score）
            table.Set("reason", ((LiteSim.MatchEndReason)result.GameplayEndReason).ToString());

            LuaTable ranks = _env().NewTable();
            for (int i = 0; i < rows.Length; i++)
            {
                LuaTable row = _env().NewTable();
                row.Set("rank", rows[i].Rank);
                row.Set("name", rows[i].Name);
                row.Set("kills", rows[i].Kills);
                row.Set("deaths", rows[i].Deaths);
                ranks.Set(i + 1, row);
            }
            table.Set("ranks", ranks);
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
            _ui.CloseAsync(id).ForgetLogged("PageFlow");
        }

        private bool IsOpen(string luaPath)
            => TryResolve(luaPath, "IsOpen", out int id) && _ui.IsOpen(id);

        /// <summary>lua_path → id；未登记 = 装配错误（表漏登记）→ 错误日志 + 跳过（不炸流程）。</summary>
        private static bool TryResolve(string luaPath, string method, out int id)
        {
            id = UI.UiPageResolver.Resolve(luaPath);
            if (id < 0) Log.Error($"PageFlow.{method}: 未登记的 lua_path '{luaPath}'（核对生成页面目录）", LogTag.UI);
            return id >= 0;
        }

        private static void Observe(UniTask<UIForm>.Awaiter aw, string method, string luaPath)
        {
            try { aw.GetResult(); }
            catch (Exception ex) { Log.Error($"PageFlow.{method}[{luaPath}]:{ex.Message}", LogTag.UI); }
        }
    }
}
