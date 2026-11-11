using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.UI;
using LiteGame.Modules.UI.Pages.Screens.Match;
using LiteGame.Modules.UI.Pages.Screens.Settings;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 五页页面流（批C-④，《上云测试页面层级设计》§1 页面关系）：页面寻址走生成的 <see cref="PageIds"/>。
    /// </summary>
    public sealed class PageFlow : ILoginSurface
    {
        private readonly UINavigationController _nav;
        private readonly UIService _ui;
        private readonly ISettlementSummary _settlement;     // 结算摘要端口（行表全序/名次口径归 Battle）

        private bool _entryOpened;
        private Landing _landing;                            // 启动落点（对局失败回大厅的语境判据：dev 直入不强行开大厅）
        private Func<UniTask<bool>> _savedPlatformBack;      // 对局菜单 Back 策略启用前的原装订（还原用）
        private long _recoveryGeneration;                    // 实例恢复代次：登录/宿主退出使旧恢复失效（不跨实例）

        public PageFlow(UINavigationController nav, UIService ui, ISettlementSummary settlement)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
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
                case Landing.Login: Go(PageIds.Page_Login, null); break;
                case Landing.Lobby: Go(PageIds.Page_Lobby, null); break;
                default: break;                                        // None：直入形态，场景/面板接管
            }
        }

        /// <summary>登录页是否已在场（账号重入的幂等判据）。</summary>
        public bool IsLoginOpen => IsOpen(PageIds.Page_Login);

        /// <summary>
        /// 账号重入（恢复链单点）：把页面栈替换为登录页——**忽略启动落点的一次性守卫** （OpenLanding 只在首进 Main 开一次页）。
        /// </summary>
        public void ShowLogin(string reason)
        {
            _landing = Landing.Login;
            InvalidateRecovery();                                // 旧房间清理随后完成也不得覆盖登录页
            Log.Info($"账号重入：回登录页（{reason}）", LogTag.UI);
            Replace(PageIds.Page_Login, null);
        }

        /// <summary>当前入口代次（操作开始时签发；恢复完成时比对）。</summary>
        internal long RecoveryGeneration => _recoveryGeneration;

        /// <summary>入口是否仍是签发代次（未登记代次 = 不按代次拦截，仍受登录页在场保护）。</summary>
        internal bool IsRecoveryGenerationCurrent(long generation)
            => generation == long.MinValue || generation == _recoveryGeneration;

        /// <summary>使所有在途恢复失效（登录/宿主退出）；不触发导航。</summary>
        internal void InvalidateRecovery()
        {
            unchecked { _recoveryGeneration++; }
        }

        /// <summary>进对局前关掉在开的页面（**dev 测试入口**路径用：从大厅/开发控制台直接进局时，
        /// 页面会盖住对局 HUD——产品路径由"房间页→开局"自然收口，此处补 dev 入口的同一语义）。
        /// 幂等：无页面 = no-op。</summary>
        public void CloseOpenPagesForBattle() => _ui.CloseAllOpen().ForgetLogged("PageFlow");

        /// <summary>对局开局（StartGame 已到）关房间页——对局场景接管画面（房间页若在），
        /// 页面栈随之清空（结算页压栈即新根）。</summary>
        public void CloseRoomIfOpen() => CloseIfOpen(PageIds.Page_Room);

        /// <summary>
        /// 对局结束（对局运行端口 Ended）：正常结束（Completed + 冻结结果在）→ 结算页（带本局结果）。
        /// </summary>
        public void OnMatchEnded(bool completed, LiteNet.Proto.MatchEnded result, int localPlayerId,
            long recoveryGeneration = long.MinValue)
        {
            // 登录页是账号重入的更高优先落点：旧房间清理/旧代次完成一律不得覆盖。
            if (IsLoginOpen) return;
            if (!IsRecoveryGenerationCurrent(recoveryGeneration)) return;

            // 对局菜单随对局结束收口：菜单开着时自然结束/离场——结算页/大厅须直接可见，不被设置页压着。
            CloseIfOpen(PageIds.Page_Settings);
            if (completed && result != null)
            {
                Go(PageIds.Page_Result, ResultData(result, localPlayerId));
                return;
            }

            ReturnToLobby();
        }

        /// <summary>失败/离场统一回大厅（已在大厅 = no-op；dev 直入不涉大厅）。</summary>
        private void ReturnToLobby()
        {
            if (_landing == Landing.None && !IsOpen(PageIds.Page_Lobby)) return;
            if (IsOpen(PageIds.Page_Lobby)) return;
            Replace(PageIds.Page_Lobby, null);
        }

        /// <summary>对局中打开设置页（HUD 菜单钮与空栈 Back 共用入口）：导航数据带 <c>battleExit</c>——
        /// 设置页按它门控"退出对局"按钮（登录/大厅/结算的设置入口不带数据，不显退场钮）。</summary>
        public void OpenBattleSettings()
            => Go(PageIds.Page_Settings, new SettingsPageData(true));

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

        /// <summary>
        /// 本局结果 → 结算页数据（排行行 ＋ 本机摘要；名次/击杀/阵亡单源，页面不重算）。
        /// </summary>
        private ResultPageData ResultData(LiteNet.Proto.MatchEnded result, int localPlayerId)
        {
            IReadOnlyList<MatchSettlementRow> rows = _settlement.Build(result.Players);
            var rankRows = new List<ResultPageData.Row>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                rankRows.Add(new ResultPageData.Row(
                    rows[i].PlayerId, rows[i].Rank, rows[i].Name, rows[i].Kills, rows[i].Deaths));
            }
            return new ResultPageData(rankRows, localPlayerId,
                ((LiteSim.MatchEndReason)result.GameplayEndReason).ToString());
        }

        private void Go(int formId, IUIData data)
        {
            var aw = _nav.GoAsync(formId, data).GetAwaiter();
            aw.OnCompleted(() => Observe(aw, "Go", formId));
        }

        private void Replace(int formId, IUIData data)
        {
            var aw = _nav.ReplaceAsync(formId, data).GetAwaiter();
            aw.OnCompleted(() => Observe(aw, "Replace", formId));
        }

        private void CloseIfOpen(int formId)
        {
            if (!_ui.IsOpen(formId)) return;
            _ui.CloseAsync(formId).ForgetLogged("PageFlow");
        }

        private bool IsOpen(int formId) => _ui.IsOpen(formId);

        private static void Observe(UniTask<UIForm>.Awaiter aw, string method, int formId)
        {
            try { aw.GetResult(); }
            catch (Exception ex) { Log.Error($"PageFlow.{method}[{formId}]:{ex.Message}", LogTag.UI); }
        }
    }
}
