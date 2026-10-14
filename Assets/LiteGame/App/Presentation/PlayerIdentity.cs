using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 参赛玩家**身份序号单源**（排行榜与阵亡归属的占位名单源共用——防两份序号逻辑漂移）：
    /// 序号 k = 该玩家在"**参赛玩家按 EntityId 升序**"中的位置（1-based）——局内**稳定**
    /// （对局中拒加入 ⇒ 玩家集合开局固定；阵亡/断线保留槽位仍在册）。
    ///
    /// **真名字（昵称）面无来源**（`JoinAck.members` 只有席位 id、实体无名字字段、游客登录无昵称）——
    /// 故本件给占位 <c>玩家{k}</c>；真名字归 G3 Profile（见审计登记）。
    /// </summary>
    public static class PlayerIdentity
    {
        /// <summary>占位名前缀（真名字面落地时只换本处来源，格结构/排序不受影响）。</summary>
        public const string PlaceholderNamePrefix = "玩家";

        /// <summary>参赛玩家身份序号（1-based；0 = 非参赛 / 不在册）。零分配（单趟计数，不做排序）。</summary>
        public static int OrdinalOf(SimWorldState state, long entityId)
        {
            if (state == null || entityId == 0L) return 0;

            EntitySlot[] entities = state.Entities;
            bool found = false;
            int rank = 0;
            for (int slot = 0; slot < entities.Length; slot++)
            {
                ref EntitySlot e = ref entities[slot];
                if (!IsPlayer(in e)) continue;
                if (e.Id == entityId) found = true;      // 目标自身不算在"更小者"里
                else if (e.Id < entityId) rank++;
            }
            return found ? rank + 1 : 0;
        }

        /// <summary>占位名（<c>玩家{k}</c>；非参赛/不在册 → 空串——调用方据此不写文本）。</summary>
        public static string PlaceholderName(SimWorldState state, long entityId)
        {
            int ordinal = OrdinalOf(state, entityId);
            return ordinal > 0 ? PlaceholderNamePrefix + ordinal : string.Empty;
        }

        /// <summary>参赛玩家判据（<c>EntityFlags.Player</c> 且槽位在册；阵亡/断线保留槽位故不看存活）。</summary>
        public static bool IsPlayer(in EntitySlot e)
            => e.Id != 0L && (e.Flags & EntityFlags.Player) != 0u;
    }
}
