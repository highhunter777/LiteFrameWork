using System;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>HUD 只读模型：生命/弹药/击杀/阵亡；战斗会话写入，视图只读（不改判定）。</summary>
    public sealed class BattleHudModel
    {
        public int Health { get; private set; } = 100;

        public int MaxHealth { get; private set; } = 100;

        public int Ammo { get; private set; }

        public int Kills { get; private set; }

        public int Deaths { get; private set; }

        public void SetVitals(int health, int maxHealth, int ammo)
        {
            MaxHealth = Math.Max(1, maxHealth);
            Health = Math.Clamp(health, 0, MaxHealth);
            Ammo = Math.Max(0, ammo);
        }

        public void SetAmmo(int ammo) => Ammo = Math.Max(0, ammo);

        public void SetScore(int kills, int deaths)
        {
            Kills = Math.Max(0, kills);
            Deaths = Math.Max(0, deaths);
        }
    }
}
