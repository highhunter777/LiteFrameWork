namespace LiteSim
{
    /// <summary>FFA 比分/整数帧时限。整帧伤害结算完后裁决；并列最高击杀为平局。</summary>
    public static class MatchSystem
    {
        public static void Start(SimWorldState s, in MatchRules rules)
        {
            // 同时验证 default(MatchRules)，防止绕过构造器装配非法规则。
            var validated = new MatchRules(rules.DurationFrames, rules.KillLimit,
                rules.RespawnDelayFrames, rules.SpawnProtectionFrames);
            s.Match = new MatchStateData
            {
                Phase = SimMatchPhase.Running,
                Round = 1,
                Timer = validated.DurationFrames,
                KillLimit = validated.KillLimit,
                RespawnDelayFrames = validated.RespawnDelayFrames,
                SpawnProtectionFrames = validated.SpawnProtectionFrames,
            };
        }

        public static void Tick(SimWorldState s)
        {
            if (s.Match.Phase != SimMatchPhase.Running) return; // lint-allow R3（整数阶段）
            if (s.Match.KillLimit > 0)
            {
                for (int i = 0; i < SimConfig.MaxEntities; i++)
                {
                    if (IsPlayer(s, i) && s.Entities[i].Kills >= s.Match.KillLimit)
                    {
                        Finish(s, MatchEndReason.KillLimit);
                        return;
                    }
                }
            }
            if (s.Match.Timer <= 0) return; // 0 = 不限时
            s.Match.Timer--;
            if (s.Match.Timer == 0) Finish(s, MatchEndReason.TimeLimit);
        }

        public static void Finish(SimWorldState s, MatchEndReason reason)
        {
            if (s.Match.Phase == SimMatchPhase.Finished) return; // lint-allow R3（整数阶段）
            long winner = 0L;
            int best = -1;
            bool tied = false;
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!IsPlayer(s, i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Kills > best) { best = e.Kills; winner = e.Id; tied = false; }
                else if (e.Kills == best) tied = true; // lint-allow R3（整数比分）
            }
            s.Match.Phase = SimMatchPhase.Finished;
            s.Match.EndReason = reason;
            s.Match.Timer = 0;
            s.Match.Winner = tied ? 0L : winner;
            s.Cmds.Clear();
        }

        public static bool IsPlayer(SimWorldState s, int slot) =>
            s.IsAlive(slot) && (s.Entities[slot].Flags & EntityFlags.Player) != 0u;
    }
}
