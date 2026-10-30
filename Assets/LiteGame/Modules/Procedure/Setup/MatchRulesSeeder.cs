using LiteFramework;
using LiteSim;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Dev;

namespace LiteGame
{
    /// <summary>
    /// 每局规则播种（**装配根显式步骤**，不属于传输工厂）：
    /// 建本局 <see cref="MatchTestRules"/> 持有件并复位宿主钩子（测试模式开关 / bot 冻结与满血 /
    /// 传送分发 / 重置请求）。规则实例由调用方随传输/会话显式移交（路线图 §4.4：无全局"当前局"登记口）。
    ///
    /// **为什么单独成类**：规则播种是全局可变状态的写通道，不能藏在传输工厂里——传输工厂只产传输。
    /// 播种与传输创建同刻（装配根在 <c>CreateTransport</c> 前调用），但职责分开：漏调在测试房形态
    /// 显式失败（见 <see cref="ProcedureMatch.CreateTransport"/>），不再静默退回"无规则本地服"。
    ///
    /// 规则初值经 <see cref="IDevSession"/> 读（**不再直读开发静态**）；发布形态端口 `IsDevelopmentBuild=false`
    /// → 恒返回 null（正式包没有测试规则面）。
    /// </summary>
    public static class MatchRulesSeeder
    {
        /// <summary>播种本局规则与宿主钩子，返回本局规则持有件（正式包 = null）。
        /// `dev` = 开发会话端口（装配根注入；null 等价发布形态）。</summary>
        public static MatchTestRules Seed(bool testRoom, IDevSession dev = null)
        {
            if (dev == null || !dev.IsDevelopmentBuild) return null;   // 发布形态：无测试规则面

            // 每局规则持有件：初值随入口定（常规入口一律 false——与既有"随入口复位"同语义）；
            // 登记后客户端预测（对局运行端口 StartGame 绑定）与本地服（ForRoom 注入）同实例。
            // 爆头带覆写随端口待用值（本地服形态的调参延续；真服务器无此链）。
            var rules = new MatchTestRules
            {
                NoDeath = testRoom && dev.NoDeath,
                InfiniteAmmo = testRoom && dev.InfiniteAmmo,
                HeadHitLineOverride = dev.HeadHitLineOverride,
                HeadshotRadiusOverride = dev.HeadshotRadiusOverride,
                DamageOverride = dev.WeaponDamageOverride,
                RpmOverride = dev.WeaponRpmOverride,
            };
            ILocalTestHooks hooks = dev.LocalTest ?? NullLocalTestHooks.Instance;
            hooks.Active = testRoom;                // 宿主钩子（冻结/传送/假人操作）开关
            hooks.BotFrozen = testRoom && dev.BotFrozen;
            hooks.BotInfiniteHp = testRoom && dev.BotInfiniteHp;   // 进房初值镜像
            hooks.TeleportDispatch = false;         // 进房清残留请求
            hooks.BotResetRequested = false;        // 同上（进房前的面板误点不带到对局）
            return rules;
        }
    }
}
