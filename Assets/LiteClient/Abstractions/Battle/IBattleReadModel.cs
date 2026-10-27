namespace LiteClient
{
    /// <summary>本地玩家读取事实（HUD 血条/弹药）。</summary>
    public readonly struct BattleLocalPlayerFacts
    {
        /// <summary>当前生命值。</summary>
        public readonly int Hp;

        /// <summary>已选武器槽（-1 = 未装备）。</summary>
        public readonly int WeaponSlot;

        /// <summary>弹匣余弹（未装备 = 0）。</summary>
        public readonly int MagAmmo;

        /// <summary>剩余备弹（未装备 = 0）。</summary>
        public readonly int ReserveAmmo;

        /// <summary>是否换弹中。</summary>
        public readonly bool Reloading;

        public BattleLocalPlayerFacts(int hp, int weaponSlot, int magAmmo, int reserveAmmo, bool reloading)
        {
            Hp = hp;
            WeaponSlot = weaponSlot;
            MagAmmo = magAmmo;
            ReserveAmmo = reserveAmmo;
            Reloading = reloading;
        }
    }

    /// <summary>参赛玩家读取事实（排行榜一行）。</summary>
    public readonly struct BattlePlayerFacts
    {
        /// <summary>实体 Id（局内唯一）。</summary>
        public readonly long Id;

        /// <summary>击杀数。</summary>
        public readonly int Kills;

        public BattlePlayerFacts(long id, int kills)
        {
            Id = id;
            Kills = kills;
        }
    }

    /// <summary>
    /// 对局只读事实端口（路线图 M0 接缝 2）：表现层（HUD/排行）只经本端口取事实，
    /// 不直读 <c>SimWorldState</c> 的槽位数组布局——测试可注入替身。
    ///
    /// **顺序契约**：<see cref="PlayerCount"/> 同时刷新内部规范序（EntityId 升序）；
    /// 随后按 0..PlayerCount-1 调 <see cref="TryGetPlayerAt"/> 取同一快照。
    /// </summary>
    public interface IBattleReadModel
    {
        /// <summary>当前帧号（读取快照的时间轴）。</summary>
        int Frame { get; }

        /// <summary>本地玩家事实；未对齐/未解析 = false（调用方如实收起面板）。</summary>
        bool TryGetLocalPlayer(out BattleLocalPlayerFacts local);

        /// <summary>参赛玩家数（读取即刷新规范序）。</summary>
        int PlayerCount { get; }

        /// <summary>按规范序（EntityId 升序）取第 <paramref name="index"/> 个玩家。</summary>
        bool TryGetPlayerAt(int index, out BattlePlayerFacts player);

        /// <summary>团队模式标记（0 = FFA）。</summary>
        int TeamMode { get; }

        /// <summary>团队比分（FFA 下无意义）。</summary>
        int TeamScore { get; }

        /// <summary>UI 显示名（身份占位单源；真名字归 Profile）。</summary>
        string DisplayName(long entityId);
    }
}
