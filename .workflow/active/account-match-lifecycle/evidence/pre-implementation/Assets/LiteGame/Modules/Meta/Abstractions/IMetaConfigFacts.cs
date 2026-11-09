namespace LiteGame
{
    /// <summary>联机配置事实（入口落点判据）：页面/流程只需要"配没配服务器"，不需要联机服务本体。</summary>
    public interface IMetaConfigFacts
    {
        /// <summary>是否配置了联机入口（false = 一切入口显性失败——页面据此显示"未配置服务器"）。</summary>
        bool IsConfigured { get; }
    }
}
