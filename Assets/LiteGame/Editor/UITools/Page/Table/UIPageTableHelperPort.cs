using System.Collections.Generic;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>默认端口：把调用转发到 <see cref="UIPageTableHelper"/>（表路径在构造时注入）。</summary>
    public sealed class UIPageTableHelperPort : IUIPageTablePort
    {
        private readonly string _tablePath;

        public UIPageTableHelperPort(string tablePath = null)
        {
            _tablePath = tablePath;
        }

        public bool TryListPages(out List<UIPageTableRow> rows, out string error)
            => UIPageTableHelper.TryListPages(out rows, out error, _tablePath);

        public int SuggestId() => UIPageTableHelper.SuggestId(_tablePath);

        public bool Check(string pageName, int id, int layer, bool fullScreen, out string error)
            => UIPageTableHelper.Check(pageName, id, layer, fullScreen, out error, _tablePath);

        public UIPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen)
            => UIPageTableHelper.Write(pageName, id, layer, fullScreen, _tablePath);

        public bool Rollback(string backup, out string error)
            => UIPageTableHelper.Rollback(backup, out error, _tablePath);

        public UIPageRowLookup LookupRow(string pageName, int id, out UIPageTableRow row, out string error)
            => UIPageTableHelper.LookupRow(pageName, id, out row, out error, _tablePath);
    }
}
