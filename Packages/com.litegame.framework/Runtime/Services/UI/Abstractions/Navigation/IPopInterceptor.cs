namespace LiteFramework.UI
{
    /// <summary>出栈拦截：Close 的统一闸口——返回键/程序关闭都经此处，可否决。</summary>
    public interface IPopInterceptor
    {
        bool CanClose(UIForm form);
    }
}
