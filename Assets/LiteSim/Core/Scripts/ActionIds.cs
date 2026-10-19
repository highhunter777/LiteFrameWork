namespace LiteSim
{
    /// <summary>
    /// 主动作摘要的动作 Id 取值域（《联机同步残差收敛专项设计》§3.2：主动作摘要即**公共动作面**）。
    /// 数值走**常量单源**（不硬编码字符串）；客户端表现按此 Id 选动画语义
    /// （<c>CharacterAnimationIds.Reload</c>），后续动作系统落地后在此扩表或迁 Luban 表——
    /// 协议无需改动（<c>SlotDelta.action_id</c> 已存在）。
    /// </summary>
    public static class ActionIds
    {
        /// <summary>无动作（<see cref="ActionRuntime.ActionId"/> 缺省值）。</summary>
        public const int None = 0;

        /// <summary>换弹（首版唯一消费者——<see cref="WeaponSystem"/> 在换弹开始/结束时写入）。</summary>
        public const int Reload = 1;
    }
}
