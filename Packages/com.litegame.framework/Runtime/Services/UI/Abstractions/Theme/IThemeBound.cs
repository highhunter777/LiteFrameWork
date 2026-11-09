namespace LiteFramework.UI
{
    /// <summary>
    /// 主题能力注入接口（**组件层级**；《UI 系统总设计》§6.2）：`UIForm` 装配期（PrepareForShow。）。
    /// </summary>
    public interface IThemeBound
    {
        /// <summary>注入主题解析器（幂等：重复注入以最新实例为准，实现方应刷新表现）。</summary>
        void OnThemeBound(UIStyleResolver resolver);
    }
}
