using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.Ui
{
    /// <summary>
    /// 页面目录端口的生成物适配：目录本体 <see cref="UiPageCatalog"/>（`#uiform` 单源）
    /// ＋ 寻址单源 <see cref="UiPageResolver"/>。实例无状态，装配点一次 Put、消费方按端口取。
    /// </summary>
    public sealed class UiPageDirectory : IPageDirectory
    {
        /// <summary>共享实例（目录是生成物、本类只做转发——无需每装配点各建一份）。</summary>
        public static readonly UiPageDirectory Instance = new UiPageDirectory();

        private UiPageDirectory() { }

        /// <inheritdoc />
        public IReadOnlyList<PageDefinition> All => UiPageCatalog.All;

        /// <inheritdoc />
        public int Resolve(string luaPath) => UiPageResolver.Resolve(luaPath);

        /// <inheritdoc />
        public PageDefinition Get(int id) => UiPageCatalog.Instance.Get(id);
    }
}
