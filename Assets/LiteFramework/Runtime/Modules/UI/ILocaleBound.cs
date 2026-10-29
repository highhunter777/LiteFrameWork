using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 界面逻辑的可选能力：接受语言服务注入（《UI框架总设计》§5.1"可选能力"扩展——不进七回调主契约，
    /// 也不把语言服务焊进 IUIFormLogic：不关心文案的逻辑零成本不实现）。
    ///
    /// 实现者：<see cref="UIBindIndex"/> 承载逻辑的两侧——LuaBehaviourAdapter 与
    /// UIBindBase。注入时机 = <see cref="UIForm"/> 在 OnInit 之后（绑定索引已建、
    /// OnShow 之前——页内 SetTextKey 当即有效）；语言切换经 <see cref="ILocalizationService.OnLocaleChanged"/>
    /// 自动重写已绑定文案，不重跑 OnShow（§9）。
    /// </summary>
    public interface ILocaleBound
    {
        /// <summary>绑定语言服务（幂等——重复注入先退订旧订阅再挂新，见 <c>UIBindIndex.BindLocale</c>）。</summary>
        void BindLocale(ILocalizationService localization);
    }
}
