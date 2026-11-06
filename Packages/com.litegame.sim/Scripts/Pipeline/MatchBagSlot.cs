namespace LiteSim
{
    /// <summary>
    /// 局内背包单格（《游戏业务系统总设计》§7.1：固定 12 格、4 快捷栏，不放对象引用）。
    /// </summary>
    public partial struct MatchBagSlot
    {
        /// <summary>物品定义 Id（Luban <c>tb_item</c>；0 = 空格）。**私有面**（本人局内背包）。</summary>
        [StateLayer(StateLayer.Private)]
        public int ItemDefId;

        /// <summary>堆叠数量。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int Count;

        /// <summary>快捷栏绑定（0 = 未绑定；1..4）。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int QuickSlot;
    }
}
