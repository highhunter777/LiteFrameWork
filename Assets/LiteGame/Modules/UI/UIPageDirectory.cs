using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面目录端口的生成物适配：目录本体 <see cref="UIPageCatalog"/>（`#uiform` 单源）
    /// ＋ 寻址单源 <see cref="UIPageResolver"/>。实例无状态，装配点一次 Put、消费方按端口取。
    /// </summary>
    public sealed class UIPageDirectory : IPageDirectory
    {
        /// <summary>共享实例（目录是生成物、本类只做转发——无需每装配点各建一份）。</summary>
        public static readonly UIPageDirectory Instance = new UIPageDirectory();

        private UIPageDirectory() { }

        /// <inheritdoc />
        public IReadOnlyList<PageDefinition> All => UIPageCatalog.All;

        /// <inheritdoc />
        public int Resolve(string luaPath) => UIPageResolver.Resolve(luaPath);

        /// <inheritdoc />
        public PageDefinition Get(int id) => UIPageCatalog.Instance.Get(id);
    }
}
