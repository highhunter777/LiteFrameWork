namespace LiteSim
{
    /// <summary>
    /// 武器槽状态机（《游戏业务系统总设计》§5.2）。P0 只冻结契约与枚举值；
    /// 状态推进（射速节拍/换弹/切枪）归 P1 WeaponSystem——本枚举同时是线上私有快照的 state 字段取值域。
    /// </summary>
    public enum WeaponSlotState : byte
    {
        Unequipped = 0,
        Ready = 1,
        Reloading = 2,
        Switching = 3,
    }

    /// <summary>
    /// 单武器槽运行态（《游戏业务系统总设计》§3.1/§5.2 固定布局 WeaponRuntime[entity, 2]）。
    /// 纯值类型（blittable）：进快照/进校验/Array.Copy 整块深拷的前提（与 <see cref="EntitySlot"/> 同纪律）。
    /// 数组布局：<c>Weapons[slot * <see cref="SimConfig.WeaponSlotsPerEntity"/> + w]</c>，w 即武器槽位（0=步枪 1=霰弹）。
    /// 公开/私有边界（§1 P0 阻塞项）：弹药/换弹等属**本人私有面**（PrivateStateSnapshot），不上公共 SlotDelta；
    /// 其他玩家只见 <see cref="EntitySlot.SelectedWeapon"/>。
    /// </summary>
    public partial struct WeaponRuntime
    {
        /// <summary>武器定义 Id（Luban <c>tb_weapon.weapon_id</c>；0 = 空槽）。</summary>
        [StateLayer(StateLayer.Private)]
        public int WeaponDefId;

        /// <summary>弹匣内弹药。</summary>
        [StateLayer(StateLayer.Private)]
        public int MagAmmo;

        /// <summary>备弹。</summary>
        [StateLayer(StateLayer.Private)]
        public int ReserveAmmo;

        /// <summary>下一帧可开火帧号（整数帧节拍：<c>fireInterval = ceil(60/rpm)</c>，禁止浮点倒计时）。</summary>
        [StateLayer(StateLayer.Private)]
        public int NextFireFrame;

        /// <summary>换弹结束帧（0 = 未在换弹）。</summary>
        [StateLayer(StateLayer.Private)]
        public int ReloadEndFrame;

        /// <summary>切枪/装备完成帧（0 = 无切换）。</summary>
        [StateLayer(StateLayer.Private)]
        public int EquipEndFrame;

        /// <summary>槽位状态机（见 <see cref="WeaponSlotState"/>）。</summary>
        [StateLayer(StateLayer.Private)]
        public WeaponSlotState State;

        /// <summary>开火序号（单调递增；VFX/事件对齐与回放去重锚点）。</summary>
        [StateLayer(StateLayer.Private)]
        public int ShotSeq;
    }

    /// <summary>
    /// 动作四阶段（《游戏业务系统总设计》§6.1：None/Windup/Active/Recovery）。
    /// 主动作槽用 None..Recovery；技能槽常态停在 None（执行期占用主动作槽）。
    /// </summary>
    public enum ActionPhase : byte
    {
        None = 0,
        Windup = 1,
        Active = 2,
        Recovery = 3,
    }

    /// <summary>
    /// 单动作/技能槽运行态（§3.1 固定布局 ActionRuntime[entity, 4]）。
    /// 数组布局：<c>Actions[slot * <see cref="SimConfig.ActionSlotsPerEntity"/> + a]</c>——
    /// <b>0 = 主动作槽</b>（冲刺/技能/交互共用，§6.1 单实体一个主动作），<b>1..3 = Skill1..3</b>（冷却/充能账本）。
    /// 公开/私有边界：主动作槽的 <c>ActionId/Phase/StartFrame</c> 属公共动作摘要（SlotDelta）；
    /// 技能槽的 <c>CooldownEnd/Charges</c> 属本人私有面。
    /// </summary>
    public partial struct ActionRuntime
    {
        /// <summary>动作定义 Id（Luban <c>tb_action</c>；0 = 无）。主动作槽（索引 0）属公共摘要。</summary>
        [StateLayer(StateLayer.Public, onlySlotIndex: 0)]   // 槽 0 公共摘要；槽 1..3 私有
        public int ActionId;

        /// <summary>动作起始帧（进度/表现对齐；主动作槽语义）。主动作槽（索引 0）属公共摘要。</summary>
        [StateLayer(StateLayer.Public, onlySlotIndex: 0)]   // 槽 0 公共摘要；槽 1..3 私有
        public int StartFrame;

        /// <summary>当前阶段。主动作槽（索引 0）属公共摘要。</summary>
        [StateLayer(StateLayer.Public, onlySlotIndex: 0)]   // 槽 0 公共摘要；槽 1..3 私有
        public ActionPhase Phase;

        /// <summary>施放令牌（= 输入面 action_seq；服务器按 (playerId, frame, seq) 去重，§3.2）。
        /// **私有面**：去重用的服务端内部账本，客户端不需要也不该看见。</summary>
        [StateLayer(StateLayer.Private)]
        public int CastToken;

        /// <summary>冷却结束帧（技能槽语义；0 = 无冷却）。**私有面**（本人技能账本）。</summary>
        [StateLayer(StateLayer.Private)]
        public int CooldownEnd;

        /// <summary>可用充能数（技能槽语义）。**私有面**（本人技能账本）。</summary>
        [StateLayer(StateLayer.Private)]
        public int Charges;
    }

    /// <summary>
    /// 单状态效果槽（§3.1 固定布局 StatusSlot[entity, 4]）。
    /// 数组布局：<c>Status[slot * <see cref="SimConfig.StatusSlotsPerEntity"/> + i]</c>。
    /// 公开投影：护盾值落 <see cref="EntitySlot.Shield"/>（公共面）；
    /// 效果明细（Id/结束帧/参数）属本人私有面（PrivateStateSnapshot.status），远端只看投影值。
    /// EffectId = 0 表示空槽。
    /// </summary>
    public partial struct StatusSlotData
    {
        /// <summary>效果定义 Id（0 = 空槽）。**私有面**：他人只见 <see cref="EntitySlot.Shield"/> 投影。</summary>
        [StateLayer(StateLayer.Private)]
        public int EffectId;

        /// <summary>效果结束帧。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int EndFrame;

        /// <summary>效果参数（护盾=剩余护盾值；按效果释义）。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int Param;
    }
}
