namespace LiteSim
{
    /// <summary>
    /// 清理系统：玩家仅衰减尸体窗、保留槽位；其他死亡实体期满经 Despawn 完整清零。
    /// 槽位清零保证空槽校验值恒定（§3.6 全槽位参与校验的前提）；
    /// 版本不在此递增（#8：版本只在 Spawn 时递增）。
    /// </summary>
    public static class CleanupSystem
    {
        public static void Run(SimWorldState s)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i) || s.Entities[i].Hp > 0) continue;
                if (s.Entities[i].CorpseFrames > 0) { s.Entities[i].CorpseFrames--; continue; }
                if ((s.Entities[i].Flags & EntityFlags.Player) != 0u) continue;
                s.Despawn(s.Entities[i].Id);
            }
        }
    }
}
