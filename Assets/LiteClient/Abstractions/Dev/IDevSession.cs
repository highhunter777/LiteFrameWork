using LiteSim;

namespace LiteClient
{
    /// <summary>
    /// 开发会话端口（路线图 M0 接缝 6）：产品代码只经本端口读开发/测试面开关与请求，
    /// 不直接触碰 <c>TestModeRuntime</c> 静态（该静态只在开发装配里存在，release 整段剥离）。
    ///
    /// 现有直读点按"只减不增"基线保留（见 L1 allowlist 守卫）；**新增**读取一律经本端口，
    /// 由装配根注入具体实现（开发 = 转发静态；发布 = 全关的空实现）。
    /// </summary>
    public interface IDevSession
    {
        /// <summary>是否处于本地测试模式。</summary>
        bool Active { get; }

        bool NoDeath { get; }
        bool InfiniteAmmo { get; }
        bool LaserSight { get; }
        bool DamageNumbers { get; }
        bool AutoHeadshot { get; }
        bool TeleportEnabled { get; }
        bool BotFrozen { get; }
        bool BotInfiniteHp { get; }

        /// <summary>补位 bot 数量（总席位 = 1 + BotCount）。</summary>
        int BotCount { get; }

        /// <summary>爆头带跨局待用覆写（&lt; 0 = 不覆写）。</summary>
        float HeadHitLineOverride { get; }

        /// <summary>爆头半径跨局待用覆写（&lt; 0 = 不覆写）。</summary>
        float HeadshotRadiusOverride { get; }

        /// <summary>武器伤害跨局待用覆写（&lt; 0 = 不覆写）。</summary>
        int WeaponDamageOverride { get; }

        /// <summary>武器射速跨局待用覆写（&lt;= 0 = 不覆写）。</summary>
        int WeaponRpmOverride { get; }

        /// <summary>身位/爆头调试绘制开关。</summary>
        bool DrawHeadshotDebug { get; }

        /// <summary>当前对局规则持有件（dev 投影的读面：HUD/面板/GM/绘制；null = 无对局或常规房）。
        /// 由对局会话在进房时绑定、离场解绑——模拟侧不再有"当前局"静态。</summary>
        MatchTestRules MatchRules { get; }

        /// <summary>绑定本局规则（对局会话唯一调用方）。</summary>
        void AttachMatchRules(MatchTestRules rules);

        /// <summary>解绑本局规则（只解绑自己绑定的同一实例）。</summary>
        void DetachMatchRules(MatchTestRules rules);

        void RequestEnter();
        void RequestExit();
        void RequestTeleport();

        bool TakeEnterRequest();
        bool TakeExitRequest();
        bool TakeTeleportRequest();
    }
}
