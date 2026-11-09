namespace LiteGame
{
    /// <summary>
    /// 应用流程请求端口（联机入口 → 应用壳）：只表达"要做什么"，**不认识阶段机/阶段参数类型**。
    /// </summary>
    public interface IAppFlow
    {
        /// <summary>是否已在主菜单（离场/重进的幂等判据；无对局时的 no-op 依据）。</summary>
        bool IsAtMain { get; }

        /// <summary>请求进入对局（进房/建房成功之后；携带联机入场事实）。</summary>
        void RequestMatch(MetaJoin join);

        /// <summary>请求回主菜单（在途阶段取消；已在主菜单 = no-op）。</summary>
        void RequestMain();
    }
}
