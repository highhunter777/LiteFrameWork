namespace LiteSim
{
    /// <summary>
    /// 单武器槽运行态（《游戏业务系统总设计》§3.1/§5.2 固定布局 WeaponRuntime[entity, 2]）。
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
}
