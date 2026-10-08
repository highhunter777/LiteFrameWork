namespace LiteSim
{
    /// <summary>
    /// Step 纯函数（《状态同步实施方案》§5.1 前提）：
    /// 固定顺序——复活 → 输入 → 移动 → 武器 → 射击 → 命令轮次/计分 → 清理 → 帧推进 → 对局裁决。
    /// 不用自动扫描（系统集合编译期确定）；无任何状态同步专属假设（两范式同构）。
    /// 帧事件不在此清空——由驱动在消费后清。
    /// **玩法数值实例 <paramref name="values"/> 与武器表实例 <paramref name="weapons"/> 由调用方显式传入**
    /// （技术债 #1：不再读全局静态——服务端传房间快照值、客户端传装载实例、测试自由构造；系统签名见各 Run）。
    /// 注意：inputs 会被**就地按 EntityId 升序稳定排序**（§3.3"同帧多请求按 playerId 升序"，
    /// 由此乱序输入与升序输入结果一致——数组顺序不是处理顺序的来源）。
    /// </summary>
    public static class SimStep
    {
        public static void Step(SimWorldState s, in SimMapData map, SimInputFrame[] inputs, in CombatValues values,
            WeaponTable weapons, bool advanceMatch = true)
        {
            if (s.Match.Phase == SimMatchPhase.Finished) { s.Cmds.Clear(); return; } // lint-allow R3（整数阶段）
            SortInputs(inputs);

            RespawnSystem.Run(s, map, values);

            InputSystem.Run(s, inputs, values);
            MovementSystem.Run(s.Entities, s.AliveBitmap, map, values);
            WeaponSystem.Run(s, inputs, weapons);         // 武器：懒装备/换弹与切枪到帧推进/切枪·换弹请求/半自动重臂（节拍与弹匣在 ShootingSystem 经 TryConsumeShot 消费）
            ShootingSystem.Run(s, map, inputs, values, weapons);   // 射击判定参与障碍遮挡（SimRaycast 单源——子弹不穿墙；散布偏转 + 多弹丸）

            FlushCommands(s); // 伤害结算经命令缓冲（当帧延迟，§3.7）

            CleanupSystem.Run(s);

            s.Frame++;
            if (advanceMatch) MatchSystem.Tick(s);
        }

        /// <summary>
        /// 命令结算：固定轮次（最多 3 轮，§3.7）——每轮只消费上一轮产生的区段，
        /// 轮内产生的新命令进入下一轮窗口；轮末清空缓冲（命令是帧内瞬态，不进快照）。
        /// 空轮无任何效果，提前收敛与固定跑满 3 轮等价（确定性不受影响）。
        /// 第一轮处理伤害，下一轮消费死亡产生的 Kill；其余命令类型由后续系统接入。
        /// </summary>
        public static void FlushCommands(SimWorldState s)
        {
            int start = 0;
            for (int round = 0; round < 3; round++)
            {
                int end = s.Cmds.Count;
                if (end <= start) break;

                DamageSystem.Run(s, start, end);
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
