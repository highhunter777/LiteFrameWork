namespace LiteSim
{
    /// <summary>保留玩家 Id/KD/背包；固定出生点复活，重置战斗运行态并开启保护期。</summary>
    public static class RespawnSystem
    {
        public static void Run(SimWorldState s, in SimMapData map, in CombatValues values)
        {
            if (s.Match.Phase != SimMatchPhase.Running) return; // lint-allow R3（整数阶段）
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!MatchSystem.IsPlayer(s, i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp > 0 || e.RespawnFrame <= 0 || s.Frame < e.RespawnFrame) continue;
                long id = e.Id;
                int kills = e.Kills, deaths = e.Deaths, spawnIndex = e.SpawnPointIndex;
                s.ResetCombatRuntime(i);
                e = new EntitySlot
                {
                    Id = id, Hp = values.EntityHp, Kills = kills, Deaths = deaths,
                    Flags = EntityFlags.Player, SpawnPointIndex = spawnIndex,
                    Pos = map.SpawnPoints[spawnIndex],
                    LifeStartFrame = s.Frame,
                    InvulnerableUntilFrame = s.Frame + s.Match.SpawnProtectionFrames,
                };
                s.Events.Write(FrameEventKind.Respawn, id, 0L, 0, e.Pos);
            }
        }
    }
}
