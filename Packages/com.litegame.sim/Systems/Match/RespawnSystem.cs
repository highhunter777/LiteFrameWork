namespace LiteSim
{
    /// <summary>保留玩家 Id/KD/背包；在未被占用的出生点中确定性随机复活，重置战斗运行态并开启保护期。</summary>
    public static class RespawnSystem
    {
        public static void Run(SimWorldState s, in SimMapData map, in CombatValues values)
        {
            if (s.Match.Phase != SimMatchPhase.Running) return; // lint-allow R3（整数阶段）
            int spawnCount = map.SpawnPointCount;
            if (spawnCount <= 0) return;

            // 本帧已占用的出生点：存活玩家当前点位 + 本帧已选给其他复活者的点位。
            // 不消费 RngState（它是射击系统的确定性审计面）；用帧号+实体 Id 混洗选点，两端同帧同结果。
            uint used = 0u;
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!MatchSystem.IsPlayer(s, i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp > 0 && e.SpawnPointIndex >= 0 && e.SpawnPointIndex < spawnCount)
                    used |= 1u << e.SpawnPointIndex;
            }

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!MatchSystem.IsPlayer(s, i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp > 0 || e.RespawnFrame <= 0 || s.Frame < e.RespawnFrame) continue;
                int spawnIndex = SpawnPointSelector.Pick(map, used,
                    SpawnPointSelector.Mix((uint)s.Frame ^ SpawnPointSelector.Mix((uint)e.Id ^ (uint)(e.Id >> 32))));
                used |= 1u << spawnIndex;
                long id = e.Id;
                int kills = e.Kills, deaths = e.Deaths;
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
