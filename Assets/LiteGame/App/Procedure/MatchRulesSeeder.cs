using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 每局规则播种（**装配根显式步骤**，不属于传输工厂）：
    /// 建本局 <see cref="MatchTestRules"/> 持有件、登记 <see cref="SimTestRules.ActiveMatch"/>，
    /// 并复位宿主钩子（测试模式开关 / bot 冻结与满血 / 传送分发 / 重置请求）。
    ///
    /// **为什么单独成类**：规则播种是全局可变状态的写通道，不能藏在传输工厂里——传输工厂只产传输。
    /// 播种与传输创建同刻（装配根在 <c>CreateTransport</c> 前调用），但职责分开：漏调在测试房形态
    /// 显式失败（见 <see cref="ProcedureMatch.CreateTransport"/>），不再静默退回"无规则本地服"。
    ///
    /// 开发三宏内才有测试规则面；正式包恒返回 null（测试身份入口已在 ProcedureMatch 拒绝）。
    /// </summary>
    public static class MatchRulesSeeder
    {
        /// <summary>播种本局规则与宿主钩子，返回本局规则持有件（正式包 = null）。</summary>
        public static MatchTestRules Seed(bool testRoom)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 每局规则持有件：初值随入口定（常规入口一律 false——与既有"随入口复位"同语义）；
            // 登记后客户端预测（BattleContext.StartGame 绑定）与本地服（ForRoom 注入）同实例。
            // 爆头带覆写随 TestModeRuntime 待用值（本地服形态的调参延续；真服务器无此链）。
            var rules = new MatchTestRules
            {
                NoDeath = testRoom && TestModeRuntime.NoDeath,
                InfiniteAmmo = testRoom && TestModeRuntime.InfiniteAmmo,
                HeadHitLineOverride = TestModeRuntime.HeadHitLineOverride,
                HeadshotRadiusOverride = TestModeRuntime.HeadshotRadiusOverride,
                DamageOverride = TestModeRuntime.WeaponDamageOverride,
                RpmOverride = TestModeRuntime.WeaponRpmOverride,
            };
            SimTestRules.ActiveMatch = rules;             // 活动局登记口（GM 中局开合的写通道；判定路径不读）
            SimTestRules.Active = testRoom;                // 宿主钩子（冻结/传送/假人操作）开关
            SimTestRules.BotFrozen = testRoom && TestModeRuntime.BotFrozen;
            SimTestRules.BotInfiniteHp = testRoom && TestModeRuntime.BotInfiniteHp;   // 进房初值镜像
            SimTestRules.TeleportDispatch = false;         // 进房清残留请求
            SimTestRules.BotResetRequested = false;        // 同上（进房前的面板误点不带到对局）
            return rules;
#else
            return null;
#endif
        }
    }
}
