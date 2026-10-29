namespace LiteFramework.UI
{
    /// <summary>
    /// 主题能力注入接口（**组件层级**；《UI 系统总设计》§6.2）：`UIForm` 装配期（PrepareForShow，
    /// ThemeProvider 非空时）遍历页面内本接口实现并注入 <see cref="UiStyleResolver"/> 实例——
    /// 形态先例＝静态文案 binder 的层级扫描（页面级一次遍历），能力接口先例＝ ILocaleBound（其派发面是页面 Logic）。
    /// 实现方持实例字段（禁静态状态）；未注入（编辑器域/测试）时走各自序列化 fallback，行为不变。
    /// </summary>
    public interface IThemeBound
    {
        /// <summary>注入主题解析器（幂等：重复注入以最新实例为准，实现方应刷新表现）。</summary>
        void OnThemeBound(UiStyleResolver resolver);
    }
}
