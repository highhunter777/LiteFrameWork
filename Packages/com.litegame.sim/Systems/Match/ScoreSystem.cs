namespace LiteSim
{
    /// <summary>Kill 的幂等消费者：每次死亡只登记一次；自杀/环境伤害不奖励击杀。</summary>
    public static class ScoreSystem
    {
        public static void Run(SimWorldState s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                SimCommand cmd = s.Cmds.Items[i];
                if (cmd.Kind == SimCommandKind.Kill) RecordKill(s, cmd.Target, cmd.Source); // lint-allow R3（枚举命令）
            }
        }

        public static void RecordKill(SimWorldState s, long victimId, long killerId)
        {
            if (s.Match.Phase != SimMatchPhase.Running || !s.TryResolve(victimId, out int victimSlot) // lint-allow R3（整数阶段）
                || !MatchSystem.IsPlayer(s, victimSlot)) return;
            ref EntitySlot victim = ref s.Entities[victimSlot];
            if (victim.Hp > 0 || victim.RespawnFrame != 0) return;
            victim.Deaths++;
            victim.RespawnFrame = s.Frame + s.Match.RespawnDelayFrames;
            if (killerId == victimId || !s.TryResolve(killerId, out int killerSlot) // lint-allow R3（整数 Id）
                || !MatchSystem.IsPlayer(s, killerSlot)) return;
            // 同帧互杀：死亡射手仍保留参赛身份，允许双方得分。
            s.Entities[killerSlot].Kills++;
        }
    }
}
