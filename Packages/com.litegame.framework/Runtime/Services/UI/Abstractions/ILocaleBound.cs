using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 界面逻辑的可选能力：接受语言服务注入（《UI框架总设计》§5.1"可选能力"扩展——不进七回调主契约， 也不把语言服务焊进 IUIFormLogic：不关心文案的逻辑零成本不实现）。
    /// </summary>
    public interface ILocaleBound
    {
        /// <summary>绑定语言服务（幂等——重复注入先退订旧订阅再挂新，见 <c>UIBindIndex.BindLocale</c>）。</summary>
        void BindLocale(ILocalizationService localization);
    }
}
