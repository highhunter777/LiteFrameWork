namespace LiteSim
{
    /// <summary>
    /// 伤害结算系统（§3.3 顺序第 4 位，§3.7 当帧延迟命令的消费者）：
    /// 结算 Damage 命令 → 扣血 → 跨越死亡线（Hp 由正变非正）时写 Kill 命令 + Death 帧事件；
    /// 测试房（<see cref="SimTestRules.NoDeath"/>）下跨死线保底 Hp=1、不写 Kill/Death（命中反馈保留）。
    /// 命令的二次产生（Kill）走 FlushCommands 的固定轮次——下一轮窗口处理（无 Kill 消费者，
    /// 掉落/得分为后续里程碑预留位）。
    /// </summary>
    public static class DamageSystem
    {
        /// <summary>结算 [commandStart, commandEnd) 区段内的 Damage 命令（轮次窗口，见 SimStep.FlushCommands）。</summary>
        public static void Run(SimWorldState s, int commandStart, int commandEnd)
        {
            for (int i = commandStart; i < commandEnd; i++)
            {
                SimCommand cmd = s.Cmds.Items[i];

                switch (cmd.Kind)
                {
                    case SimCommandKind.Damage:
                        ApplyDamage(s, cmd);
                        break;
                    // Kill/SpawnPickup/AddScore：无消费者（掉落/得分系统留空位）
                }
            }
        }

        private static void ApplyDamage(SimWorldState s, in SimCommand cmd)
        {
            // 目标已死 = 引用失效 = 正常路径（#7），命令作废
            if (!s.TryResolve(cmd.Target, out int slotIndex)) return;

            ref EntitySlot e = ref s.Entities[slotIndex];
            int hpBefore = e.Hp;
            e.Hp -= cmd.Amount;

            // 测试房全房免死：跨死亡线保底 1、不写 Kill/Death（命中/受击反馈在命中帧照旧）
            if (SimTestRules.NoDeath && hpBefore > 0 && e.Hp <= 0)
            {
                e.Hp = 1;
                return;
            }

            // 只在跨越死亡线的这一次写 Kill + Death（过量伤害堆叠不重复击杀）
            if (hpBefore > 0 && e.Hp <= 0)
            {
                e.CorpseFrames = (byte)CombatConfig.CorpseFrames;   // 尸体期置满：槽位保留窗（死亡表现/掉落载体）
                e.Vel.X = 0f;                                       // 尸体定身：清水平速度（InputSystem 已不再写 Vel——
                e.Vel.Z = 0f;                                       //   不清则按末速度滑行整个尸体期）；竖直分量留重力沉降
                s.Cmds.Write(SimCommandKind.Kill, cmd.Target, cmd.Source, 0);
                s.Events.Write(FrameEventKind.Death, cmd.Target, cmd.Source, 0, e.Pos);
            }
        }
    }
}
