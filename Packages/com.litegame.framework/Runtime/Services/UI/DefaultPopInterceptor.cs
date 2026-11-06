namespace LiteFramework.UI
{
    /// <summary>默认出栈拦截：全放行（灰盒；具体界面的"未保存拦截"由业务替换策略实现）。</summary>
    public sealed class DefaultPopInterceptor : IPopInterceptor
    {
        public bool CanClose(UIForm form) => true;
    }
}
