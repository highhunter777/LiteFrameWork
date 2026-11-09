using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面上下文（产品注入面）：页面只认端口，不碰静态门面；成员为延迟工厂，装配序不耦合。
    /// </summary>
    public interface IPageContext
    {
        /// <summary>导航单写者（页面开关唯一入口）。</summary>
        UINavigationController Nav { get; }

        /// <summary>联机业务端口（null = Meta 未装配——页面显性拒绝）。</summary>
        IMetaSession Meta { get; }

        /// <summary>地图目录端口（null = 表链未装配——页面显性拒绝）。</summary>
        IMapCatalog Maps { get; }

        /// <summary>房间会话读口（null = 未装配——页面按"未进房"处理）。</summary>
        IRoomView Room { get; }

        /// <summary>确认弹窗端口（null = 未装配——页面按取消处理）。</summary>
        IConfirmDialog Confirm { get; }

        /// <summary>UI 服务（关闭自身等页面级操作；null = 未装配）。</summary>
        UIService UI { get; }
    }
}
