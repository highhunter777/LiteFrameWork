using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>per-form 缓存策略（§5.2——关闭后实例去向）。</summary>
    public enum UICacheStrategy
    {
        /// <summary>LRU：关闭后入池，超预算按最近使用序淘汰（默认）。</summary>
        Lru = 0,
        /// <summary>常驻：关闭后入池但**不参与 LRU 淘汰**（主界面/高频页——预算满也保留）。</summary>
        Resident = 1,
        /// <summary>关闭即销毁：不进池，释放租约与 GameObject（低频/重资源页）。</summary>
        DestroyOnClose = 2,
    }

    /// <summary>
    /// 页面定义（PageDefinition）：运行时唯一页面制品。由 <c>gen-ui-page.py</c> 从
    /// <c>#uiform</c> + <c>UiPageManifest</c> + <c>UiPageContract</c> 生成；
    /// 运行时经 <see cref="IPageCatalog"/> 取件，不读表/契约/清单。
    /// </summary>
    public sealed class PageDefinition
    {
        public int Id;
        /// <summary>页面名（Page_/Hud_/Tmp_ 前缀；工具/诊断面）。</summary>
        public string Name;
        /// <summary>prefab 资源完整路径（如 "Assets/ShareResource/UI/Screens/Page_Login.prefab"）。</summary>
        public string Location;
        /// <summary>Lua 逻辑表路径（"UI.Page_Login"；空 = 无 Lua 逻辑）。</summary>
        public string LuaPath;
        /// <summary>语义层索引（0=Bottom 1=Window 2=Top 3=System）。</summary>
        public int Layer;
        /// <summary>全屏页：激活时批量遮盖更低层级组。</summary>
        public bool FullScreen;
        /// <summary>per-form 缓存策略（§5.2）。</summary>
        public UICacheStrategy CacheStrategy = UICacheStrategy.Lru;
        /// <summary>绑定表（契约 controls；运行时当前只读 Id/Location/LuaPath/Layer/FullScreen/CacheStrategy，工具/校验可用）。</summary>
        public IReadOnlyList<PageControlDefinition> Controls = Array.Empty<PageControlDefinition>();
        /// <summary>文案 key 表（契约 text；工具/校验可用）。</summary>
        public IReadOnlyList<PageTextKeyDefinition> TextKeys = Array.Empty<PageTextKeyDefinition>();
        /// <summary>样式引用表（契约 styles；工具/校验可用）。</summary>
        public IReadOnlyList<PageStyleRefDefinition> StyleRefs = Array.Empty<PageStyleRefDefinition>();
    }

    /// <summary>页面控件定义（契约 controls 的投影）。</summary>
    public readonly struct PageControlDefinition
    {
        public string BindName { get; }
        public string NodePath { get; }
        public string Type { get; }
        public string Kind { get; }
        public bool Required { get; }

        public PageControlDefinition(string bindName, string nodePath, string type, string kind, bool required)
        {
            BindName = bindName ?? string.Empty;
            NodePath = nodePath ?? string.Empty;
            Type = type ?? string.Empty;
            Kind = kind ?? string.Empty;
            Required = required;
        }
    }

    /// <summary>页面样式引用（契约 styles 的投影）。</summary>
    public readonly struct PageStyleRefDefinition
    {
        public string NodePath { get; }
        public string Role { get; }
        public string Token { get; }

        public PageStyleRefDefinition(string nodePath, string role, string token)
        {
            NodePath = nodePath ?? string.Empty;
            Role = role ?? string.Empty;
            Token = token ?? string.Empty;
        }
    }

    /// <summary>页面文案 key（契约 text 的投影）。</summary>
    public readonly struct PageTextKeyDefinition
    {
        public string NodePath { get; }
        public string Key { get; }
        public int Args { get; }

        public PageTextKeyDefinition(string nodePath, string key, int args)
        {
            NodePath = nodePath ?? string.Empty;
            Key = key ?? string.Empty;
            Args = args;
        }
    }

    /// <summary>
    /// 页面目录契约：壳只依赖"按 id 取页面定义"这一件事。生产实现 = 生成物
    /// <c>UiPageCatalog</c>；测试可注入替身，不必拉起配置链路。
    /// </summary>
    public interface IPageCatalog
    {
        PageDefinition Get(int id);
    }
}
