namespace LiteGame.Feedback
{
    /// <summary>
    /// 本地玩家与帧事件的相对角色（命中反馈的过滤键——《命中反馈与伤害数字专项设计》§4.1）。
    /// </summary>
    public enum HitLocalRole : byte
    {
        /// <summary>旁观——事件与本地玩家无关（伤害数字滤掉；特效/音效照常）。</summary>
        Bystander = 0,

        /// <summary>造成——本地玩家是射手/击杀者（白色档）。</summary>
        Caused = 1,

        /// <summary>承受——本地玩家是命中目标/死者（红色档；自伤优先记承受）。</summary>
        Received = 2,
    }
}
