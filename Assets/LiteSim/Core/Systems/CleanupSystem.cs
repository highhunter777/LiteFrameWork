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

        public static void Run(EntitySlot[] entities, uint[] aliveBitmap)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((aliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;

                ref EntitySlot e = ref entities[i];
                if (e.Hp > 0) continue;                        // 活体

                // 尸体期：递减保留（死亡表现的权威载体窗——期间实体零交互：
                // InputSystem 输入作废 / ShootingSystem 不可命中不开火）
                if (e.CorpseFrames > 0)
                {
                    e.CorpseFrames--;
                    continue;
                }

                // 尸体期满：回收（清 AliveBitmap 位 + 槽位清零——空槽校验值恒定，§3.6）
                if ((e.Flags & EntityFlags.Player) != 0u) continue;
                aliveBitmap[i >> 5] &= ~(1u << (i & 31));
                entities[i] = default;
            }
        }
    }
}
