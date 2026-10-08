using System;

namespace LiteSim
{
    /// <summary>FFA 房间规则。服务器模板装配一次，运行态随 Match 公共快照下发。</summary>
    public readonly struct MatchRules
    {
        public readonly int DurationFrames;
        public readonly int KillLimit;
        public readonly int RespawnDelayFrames;
        public readonly int SpawnProtectionFrames;

        public MatchRules(int durationFrames, int killLimit, int respawnDelayFrames, int spawnProtectionFrames)
        {
            if (durationFrames < 0 || durationFrames > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(durationFrames));
            if (killLimit < 0) throw new ArgumentOutOfRangeException(nameof(killLimit));
            if (respawnDelayFrames < 1 || respawnDelayFrames > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(respawnDelayFrames));
            if (spawnProtectionFrames < 0 || spawnProtectionFrames > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(spawnProtectionFrames));
            DurationFrames = durationFrames;
            KillLimit = killLimit;
            RespawnDelayFrames = respawnDelayFrames;
            SpawnProtectionFrames = spawnProtectionFrames;
        }

        public static MatchRules Default => new MatchRules(3 * 60 * SimConfig.TickRate, 20,
            3 * SimConfig.TickRate, 2 * SimConfig.TickRate);
    }

    public static class SimMatchPhase
    {
        public const int Unmanaged = 0;
        public const int Running = 1;
        public const int Finished = 2;
    }

    public enum MatchEndReason
    {
        None = 0,
        TimeLimit = 1,
        KillLimit = 2,
        External = 3,
    }
}
