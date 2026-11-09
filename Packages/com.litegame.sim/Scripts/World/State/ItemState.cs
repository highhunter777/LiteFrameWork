namespace LiteSim
{
    /// <summary>
    /// 地面道具分型行（《Sim组织专项设计》§2）：<c>Items[slot]</c>——行有效 ⟺ 槽位活体且 <see cref="EntityFlags.KindItem"/> 置位（kind 位 = "该槽位持有哪张。）。
    /// </summary>
    public partial struct ItemState
    {
        /// <summary>tbitemconfig 行 id（道具类型语义：1医疗包 2护盾电池 3手雷 4EMP 5雷达 6钩爪 7传送）。</summary>
        [StateLayer(StateLayer.Public)]
        public int ItemDefId;

        /// <summary>堆叠数量（同格同型叠加；拾取时并入 MatchBag，本行随之清零）。</summary>
        [StateLayer(StateLayer.Public)]
        public int Count;

        /// <summary>归属实体 Id（0 = 无主地面件；被拾取判定的近侧候选标记，由道具分型系统维护——尚未实现，G2 排期）。</summary>
        [StateLayer(StateLayer.Public)]
        public long OwnerId;

        /// <summary>已存在时长（逻辑帧计——刷新节拍与"超时未拾取回收"用）。</summary>
        [StateLayer(StateLayer.Public)]
        public int AgeFrames;
    }
}
