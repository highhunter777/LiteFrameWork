using System;
using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 页面逻辑解析与绑定（原"换表"域随页面 C# 化退出）：prefab 自带 C# 逻辑优先 → 可选 resolver（仅测试）
    /// → NullLogic；语言、主题、页面上下文与反馈入口在 OnInit 之前注入。
    /// </summary>
    internal sealed class UIFormLogicBinder
    {
        private readonly Func<PageDefinition, IUIFormLogic> _resolver;
        private readonly Action<string> _showToast;
        private readonly Func<ILocalizationService> _localeProvider;   // LText 装配缝（null = 不注入）
        private readonly Func<UIThemeAsset> _themeProvider;            // 主题装配缝（null = 页面保持烘焙值）
        private readonly Func<PageDefinition, object> _pageContextProvider;   // 页面上下文装配缝（null = 不注入）

        public UIFormLogicBinder(Func<PageDefinition, IUIFormLogic> resolver = null,
            Func<ILocalizationService> localeProvider = null,
            Func<UIThemeAsset> themeProvider = null,
            Func<PageDefinition, object> pageContextProvider = null, Action<string> showToast = null)
        {
            _resolver = resolver;
            _showToast = showToast;
            _localeProvider = localeProvider;
            _themeProvider = themeProvider;
            _pageContextProvider = pageContextProvider;
        }

        /// <summary>新实例绑定：解析逻辑并注入语言、主题、页面上下文与反馈入口。</summary>
        public void BindNew(UIForm form)
        {
            form.Logic = Resolve(form.Info, form.Root);
            form.LocaleProvider = _localeProvider;
            form.ThemeProvider = _themeProvider;
            form.PageContext = _pageContextProvider?.Invoke(form.Info);
            form.ToastHandler = _showToast;
        }

        /// <summary>逻辑解析：prefab 自带组件优先；可选 resolver 只服务测试/工具装配。</summary>
        private IUIFormLogic Resolve(PageDefinition info, UnityEngine.GameObject root)
        {
            if (root != null)
            {
                var carried = root.GetComponentInChildren<IUIFormLogic>(true);
                if (carried != null) return carried;
            }

            if (_resolver == null) return NullUIFormLogic.Instance;
            try
            {
                return _resolver(info) ?? NullUIFormLogic.Instance;
            }
            catch (Exception ex)
            {
                Log.Error("界面逻辑解析失败[" + (info != null ? info.Name : "?") + "]:" + ex.Message, LogTag.UI);
                return NullUIFormLogic.Instance;
            }
        }
    }
}
