namespace LiteSim
{
    /// <summary>
    /// Sim 常量集中地（M8 决策 #6，《状态同步实施方案》§11-3 对账）：一处定义，禁止散落魔数。
    /// M9 消费 MaxRollbackFrames；M10 消费 InterpFrames / LagCompHistory（§3.4.1）。
    /// </summary>
    public static class SimConfig
    {
        /// <summary>逻辑帧率（§3.2：射击要求 60Hz，不接受可变 dt）。</summary>
        public const int TickRate = 60;

        /// <summary>逻辑帧步长（秒）——与 M7 同源（SimMath.Dt），禁止第二处定义。</summary>
        public const float Dt = SimMath.Dt;

        /// <summary>实体槽位容量（§3.1）。</summary>
        public const int MaxEntities = 256;

        /// <summary>输入延迟帧数（§11-3）。</summary>
        public const int InputDelay = 1;

        /// <summary>单次 Tick 最多追帧数（§3.2 FrameDriver）。</summary>
        public const int MaxCatchUp = 5;

        /// <summary>回滚深度（M9 SnapshotRing 容量对齐）。</summary>
        public const int MaxRollbackFrames = 8;

        /// <summary>插值帧数（M10，§3.4.1：开火者视角帧推导）。</summary>
        public const int InterpFrames = 4;

        /// <summary>命中延迟补偿回溯窗口（M10，§3.4.1；0 = 关闭，退化为提前量判定）。</summary>
        public const int LagCompHistory = 16;

        /// <summary>输入历史容量（§5.5：最近 32 帧全体输入——回滚重放 + M11 重连补发余量，M9 决策⑤）。</summary>
        public const int MaxInputHistory = 32;

        /// <summary>单渲染帧回滚次数上限（§5.4 防雪崩，M9 决策⑧）。</summary>
        public const int MaxRollbacksPerFrame = 2;

        /// <summary>每实体自定义状态字节数（#2：平面数组，slot*32+offset 寻址）。
        /// P0 起 CustomData 只留**版本化的状态效果/模组扩展**（业务总设计 §1 阻塞项 ③）——
        /// 需要跨端恢复的正式状态一律进定型数组（武器/动作/状态/局内包/比赛）。</summary>
        public const int CustomBytesPerEntity = 32;

        /// <summary>全局逻辑状态（版本化扩展 blob）字节数。正式比赛状态已契约化为 <see cref="MatchStateData"/>。</summary>
        public const int GlobalsBytes = 256;

        // ---- P0 战斗运行态定容（《游戏业务系统总设计》§3.1 固定布局；改值 = 协议/校验/布局测试同步改）----

        /// <summary>武器槽数/实体（§5.1 首版两把 hitscan：步枪 + 霰弹）。</summary>
        public const int WeaponSlotsPerEntity = 2;

        /// <summary>动作/技能槽数/实体（§6.1：0 = 主动作槽（冲刺/技能/交互共用），1..3 = Skill1..3 冷却/充能账本）。</summary>
        public const int ActionSlotsPerEntity = 4;

        /// <summary>状态效果槽数/实体（护盾/增益等；公开投影 = <see cref="EntitySlot.Shield"/>）。</summary>
        public const int StatusSlotsPerEntity = 4;

        /// <summary>局内背包格数/实体（§7.1：固定 12 格 + 4 快捷栏）。</summary>
        public const int MatchBagSlotsPerEntity = 12;

        // ---- 玩法数值已迁出（2026-09-19 解耦：手感参数与协议常量分离）----
        // MoveSpeed/Gravity/Hitscan*/BaseDamage/DamageSpread → CombatConfig（static 属性 + Luban
        // tb_combat_num 装载接缝，表设计见 Docs/玩法数值解耦审查与Luban表设计.md）。
        // 本类只保留确定性架构常量——协议/位布局/回滚深度这类"改了=两端不一致"的锁死项。

        // ---- M10 广播 / AOI 旋钮（《M10实施指导》决策 7/13；**初始值**，M10 实施时按实测校准）----

        /// <summary>快照广播频率（Hz）：权威循环每 `TickRate / SnapshotHz` 个逻辑帧广播一次（30 → 每 2 帧）。</summary>
        public const int SnapshotHz = 30;

        /// <summary>AOI 网格边长（米）：**只影响广播裁剪，不影响判定与回滚重放**（决策 13 / §4.5-7）。</summary>
        public const float AoiCellSize = 10f;

        /// <summary>AOI 广播半径（米）：同上；<c>0</c> = 关闭 AOI（全图广播，等价性验收用）。</summary>
        public const float AoiRadius = 30f;

        /// <summary>
        /// AOI 网格**覆盖半径**（米，2.5D XZ 平面）——栅格按 <c>±本值</c> 建立（格数 = 派生，不手写）。
        /// 必须 ≥ 地图半宽/半深：越界实体虽由 `AoiFilter` 按视点距离兜底（不会漏发），但每帧多一圈距离判定、
        /// 且失去网格裁剪的带宽意义。`AoiFilter.OutsideCount &gt; 0` 即此值配小了的信号（2026-09-19 审查加）。
        /// </summary>
        public const float AoiGridExtentMeters = 640f;
    }
}
