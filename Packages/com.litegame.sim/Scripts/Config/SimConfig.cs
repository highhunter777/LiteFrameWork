namespace LiteSim
{
    /// <summary>
    /// Sim 常量集中地（《状态同步实施方案》§11-3 对账）：一处定义，禁止散落魔数。
    /// 各回滚/插值/延迟补偿常量的消费者见对应字段注释（§3.4.1）。
    /// </summary>
    public static class SimConfig
    {
        /// <summary>逻辑帧率（§3.2：射击要求 60Hz，不接受可变 dt）。</summary>
        public const int TickRate = 60;

        /// <summary>逻辑帧步长（秒）——与 <see cref="SimMath.Dt"/> 同源，禁止第二处定义。</summary>
        public const float Dt = SimMath.Dt;

        /// <summary>实体槽位容量（§3.1）。</summary>
        public const int MaxEntities = 256;

        /// <summary>输入延迟帧数（§11-3）。</summary>
        public const int InputDelay = 1;

        /// <summary>单次 Tick 最多追帧数（§3.2 FrameDriver）。</summary>
        public const int MaxCatchUp = 5;

        /// <summary>
        /// 预测领先上限（帧）：权威快照和解的重放**不得**把本地帧号带过「快照帧 + 本值」。
        /// </summary>
        public const int MaxPredictionLead = MaxCatchUp + 1;

        /// <summary>回滚深度（SnapshotRing 容量对齐）。</summary>
        public const int MaxRollbackFrames = 8;

        /// <summary>插值帧数（§3.4.1：开火者视角帧推导）。</summary>
        public const int InterpFrames = 4;

        /// <summary>命中延迟补偿回溯窗口（§3.4.1；0 = 关闭，退化为提前量判定）。</summary>
        public const int LagCompHistory = 16;

        /// <summary>输入历史容量（§5.5：最近 32 帧全体输入——回滚重放 + 重连补发余量）。</summary>
        public const int MaxInputHistory = 32;

        /// <summary>单渲染帧回滚次数上限（§5.4 防雪崩）。</summary>
        public const int MaxRollbacksPerFrame = 2;

        /// <summary>每实体自定义状态字节数（#2：平面数组，slot*32+offset 寻址）。
        /// CustomData 只留**版本化的状态效果/模组扩展**（业务总设计 §1 阻塞项 ③）——
        /// 需要跨端恢复的正式状态一律进定型数组（武器/动作/状态/局内包/比赛）。</summary>
        public const int CustomBytesPerEntity = 32;

        /// <summary>全局逻辑状态（版本化扩展 blob）字节数。正式比赛状态已契约化为 <see cref="MatchStateData"/>。</summary>
        public const int GlobalsBytes = 256;

        // ---- P0 战斗运行态定容（《游戏业务系统总设计》§3.1 固定布局；改值 = 协议/校验/布局测试同步改）----

        /// <summary>武器槽数/实体（§5.1：两把 hitscan——步枪 + 霰弹）。</summary>
        public const int WeaponSlotsPerEntity = 2;

        /// <summary>动作/技能槽数/实体（§6.1：0 = 主动作槽（冲刺/技能/交互共用），1..3 = Skill1..3 冷却/充能账本）。</summary>
        public const int ActionSlotsPerEntity = 4;

        /// <summary>状态效果槽数/实体（护盾/增益等；公开投影 = <see cref="EntitySlot.Shield"/>）。</summary>
        public const int StatusSlotsPerEntity = 4;

        /// <summary>局内背包格数/实体（§7.1：固定 12 格 + 4 快捷栏）。</summary>
        public const int MatchBagSlotsPerEntity = 12;

        // ---- 玩法数值已迁出（手感参数与协议常量分离）---- MoveSpeed/Gravity/Hitscan*/BaseDamage/DamageSpread → CombatConfig（static 属性 + Luban tb_combat_num 装载接缝）。

        // ---- 广播 / AOI 旋钮不在本类（Sim 核不该知道网络怎么发）----
        // 快照频率是房级参数（RoomConfig.SnapshotHz，随 JoinAck 下发）；AOI 裁剪参数见
        // AoiSettings（LiteSync/Shared，按房间实例化）。两者都只影响广播，不影响判定与回滚重放。
        // 本类只保留确定性架构常量——协议/位布局/回滚深度这类"改了=两端不一致"的锁死项。
    }
}
