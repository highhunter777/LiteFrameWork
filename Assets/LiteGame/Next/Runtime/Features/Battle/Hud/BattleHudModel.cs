using System;
using System.Collections.Generic;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>排行榜单元格：名字/击杀/是否本地/是否高亮。</summary>
    public readonly struct RankRow
    {
        public RankRow(long entityId, string name, int kills, bool isLocal)
        {
            EntityId = entityId;
            Name = name;
            Kills = kills;
            IsLocal = isLocal;
        }

        public long EntityId { get; }
        public string Name { get; }
        public int Kills { get; }
        public bool IsLocal { get; }
    }

    /// <summary>击杀流一行：击杀者/被击杀者/武器槽（0=未知）。</summary>
    public readonly struct KillFeedRow
    {
        public KillFeedRow(string killer, string victim, int weapon, bool localInvolved)
        {
            Killer = killer;
            Victim = victim;
            Weapon = weapon;
            LocalInvolved = localInvolved;
        }

        public string Killer { get; }
        public string Victim { get; }
        public int Weapon { get; }
        public bool LocalInvolved { get; }
    }

    /// <summary>HUD 只读模型：生命/弹药/备弹/换弹/准心/排行/团队分/重生/保护/击杀流/网络，共 10 类读数。</summary>
    public sealed class BattleHudModel
    {
        private readonly List<RankRow> _rank = new List<RankRow>();
        private readonly List<KillFeedRow> _killFeed = new List<KillFeedRow>();

        public int Health { get; private set; } = 100;

        public int MaxHealth { get; private set; } = 100;

        /// <summary>弹匣内弹药。</summary>
        public int Ammo { get; private set; }

        /// <summary>备弹。</summary>
        public int ReserveAmmo { get; private set; }

        /// <summary>是否在换弹。</summary>
        public bool IsReloading { get; private set; }

        /// <summary>准心：是否开镜（hip/ads 形态）。</summary>
        public bool IsAiming { get; private set; }

        /// <summary>准心：当前散布（度），由武器定义与连射增长算出。</summary>
        public float Spread { get; private set; }

        /// <summary>准心表现参数（来自 `tbcrosshair`）；未注入时用中性值。</summary>
        public LiteGameNext.Platform.Config.CrosshairFeel Feel { get; private set; }
            = LiteGameNext.Platform.Config.CrosshairFeel.Neutral;

        public int Kills { get; private set; }

        public int Deaths { get; private set; }

        /// <summary>团队总分（非团队模式为本地击杀数）。</summary>
        public int TeamScore { get; private set; }

        /// <summary>是否阵亡。</summary>
        public bool IsDead { get; private set; }

        /// <summary>复活倒计时（秒，0 = 无倒计时）。</summary>
        public float RespawnSeconds { get; private set; }

        /// <summary>是否处于出生保护。</summary>
        public bool IsProtected { get; private set; }

        /// <summary>网络往返（毫秒）。</summary>
        public int RttMs { get; private set; }

        /// <summary>丢包率（千分比）。</summary>
        public int LossPermille { get; private set; }

        /// <summary>排行榜（按击杀降序，最多 8 行）。</summary>
        public IReadOnlyList<RankRow> Rankboard => _rank;

        /// <summary>击杀流（最新在前，最多 5 行）。</summary>
        public IReadOnlyList<KillFeedRow> KillFeed => _killFeed;

        public void SetVitals(int health, int maxHealth, int ammo)
        {
            MaxHealth = Math.Max(1, maxHealth);
            Health = Math.Clamp(health, 0, MaxHealth);
            Ammo = Math.Max(0, ammo);
        }

        public void SetAmmo(int ammo) => Ammo = Math.Max(0, ammo);

        /// <summary>弹药全量面：弹匣/备弹/换弹。</summary>
        public void SetAmmoDetail(int mag, int reserve, bool reloading)
        {
            Ammo = Math.Max(0, mag);
            ReserveAmmo = Math.Max(0, reserve);
            IsReloading = reloading;
        }

        /// <summary>准心形态与散布。</summary>
        public void SetAim(bool aiming, float spreadDegrees)
        {
            IsAiming = aiming;
            Spread = Math.Max(0f, spreadDegrees);
        }

        /// <summary>注入准心表现参数（`tbcrosshair` 投影）。</summary>
        public void SetCrosshairFeel(LiteGameNext.Platform.Config.CrosshairFeel feel) => Feel = feel;

        /// <summary>准心外圈尺寸（像素）：静态满张 + 散布×张开，封顶 Cap。</summary>
        public float CrosshairSize(float basePixels)
        {
            float scaled = basePixels * Math.Max(0.01f, Feel.StaticMaxScale);
            float grown = scaled + Spread * Feel.BurstGrowth;
            float cap = basePixels * Math.Max(1f, Feel.BurstCap);
            return Math.Min(cap, grown);
        }

        public void SetScore(int kills, int deaths, int teamScore = -1)
        {
            Kills = Math.Max(0, kills);
            Deaths = Math.Max(0, deaths);
            TeamScore = teamScore >= 0 ? teamScore : Kills;
        }

        /// <summary>阵亡/复活/保护三态。</summary>
        public void SetLifeState(bool dead, float respawnSeconds, bool protectedNow)
        {
            IsDead = dead;
            RespawnSeconds = Math.Max(0f, respawnSeconds);
            IsProtected = protectedNow;
        }

        /// <summary>网络读数（RTT 毫秒 / 丢包千分比）。</summary>
        public void SetNetwork(int rttMs, int lossPermille)
        {
            RttMs = Math.Max(0, rttMs);
            LossPermille = Math.Clamp(lossPermille, 0, 1000);
        }

        /// <summary>整体替换排行榜（调用方负责排序）。</summary>
        public void SetRankboard(IReadOnlyList<RankRow> rows)
        {
            _rank.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count && i < 8; i++) _rank.Add(rows[i]);
        }

        /// <summary>压入一行击杀流（最新在前，最多 5 行）。</summary>
        public void PushKillFeed(KillFeedRow row)
        {
            _killFeed.Insert(0, row);
            while (_killFeed.Count > 5) _killFeed.RemoveAt(_killFeed.Count - 1);
        }

        /// <summary>换局清空。</summary>
        public void Reset()
        {
            Health = 100;
            MaxHealth = 100;
            Ammo = 0;
            ReserveAmmo = 0;
            IsReloading = false;
            IsAiming = false;
            Spread = 0f;
            Kills = 0;
            Deaths = 0;
            TeamScore = 0;
            IsDead = false;
            RespawnSeconds = 0f;
            IsProtected = false;
            RttMs = 0;
            LossPermille = 0;
            _rank.Clear();
            _killFeed.Clear();
        }
    }
}
