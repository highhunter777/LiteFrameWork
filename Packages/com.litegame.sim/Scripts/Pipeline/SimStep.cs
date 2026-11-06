namespace LiteSim
{
    /// <summary>
    /// Step 纯函数（《状态同步实施方案》§5.1 前提）： 固定顺序——复活 → 输入 → 移动 → 武器 → 射击 → 命令轮次/计分 → 清理 → 帧推进 → 对局裁决。
    /// </summary>
    public static class SimStep
    {
        public static void Step(SimWorldState s, in SimMapData map, SimInputFrame[] inputs, in CombatValues values,
            WeaponTable weapons, bool advanceMatch = true, MatchTestRules rules = null, bool resolveCombat = true)
        {
            if (s.Match.Phase == SimMatchPhase.Finished) { s.Cmds.Clear(); return; } // lint-allow R3（整数阶段）
            s.Values = values;
            SortInputs(inputs);

            RespawnSystem.Run(s, map, values);

            InputSystem.Run(s, inputs, values);
            MovementSystem.Run(s.Entities, s.AliveBitmap, map, values);
            WeaponSystem.Run(s, inputs, weapons, rules);   // 武器：懒装备/换弹与切枪到帧推进/切枪·换弹请求/半自动重臂（节拍与弹匣在 ShootingSystem 经 TryConsumeShot 消费）
            // 射击判定参与障碍遮挡（SimRaycast 单源——子弹不穿墙；散布偏转 + 多弹丸）。
            ShootingSystem.Run(s, map, inputs, values, weapons, rules, resolveCombat);

            FlushCommands(s, rules); // 伤害结算经命令缓冲（当帧延迟，§3.7）

            CleanupSystem.Run(s);

            s.Frame++;
            if (advanceMatch) MatchSystem.Tick(s);
        }

        /// <summary>
        /// 命令结算：固定轮次（最多 3 轮，§3.7）——每轮只消费上一轮产生的区段， 轮内产生的新命令进入下一轮窗口；轮末清空缓冲（命令是帧内瞬态，不进快照）。
        /// </summary>
        public static void FlushCommands(SimWorldState s, MatchTestRules rules = null)
        {
            int start = 0;
            for (int round = 0; round < 3; round++)
            {
                int end = s.Cmds.Count;
                if (end <= start) break;

                DamageSystem.Run(s, start, end, rules);
                ScoreSystem.Run(s, start, end);
                start = end;
            }
            s.Cmds.Clear();
        }

        /// <summary>就地稳定插入排序（按 EntityId 升序；零分配，R4 合规——不用 LINQ/库排序）。</summary>
        private static void SortInputs(SimInputFrame[] inputs)
        {
            for (int i = 1; i < inputs.Length; i++)
            {
                SimInputFrame key = inputs[i];
                int j = i - 1;
                while (j >= 0 && inputs[j].EntityId > key.EntityId)
                {
                    inputs[j + 1] = inputs[j];
                    j--;
                }
                inputs[j + 1] = key;
            }
        }
    }
}
