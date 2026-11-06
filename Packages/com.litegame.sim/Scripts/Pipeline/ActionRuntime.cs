namespace LiteSim
{
    /// <summary>
    /// 单动作/技能槽运行态（§3.1 固定布局 ActionRuntime[entity, 4]）。
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
}
