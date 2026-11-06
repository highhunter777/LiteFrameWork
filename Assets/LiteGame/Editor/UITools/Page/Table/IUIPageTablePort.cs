using System.Collections.Generic;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>
    /// 表写/回滚/核验的端口（《UI 编辑器工具链专项设计》§3.6）：生产实现＝<see cref="UIPageTableHelper"/>；
    /// 夹具可注入“已落行但报失败/超时”的假实现，用于验证登记的核验→回滚→复核路径。
    /// </summary>
    public interface IUIPageTablePort
    {
        bool TryListPages(out List<UIPageTableRow> rows, out string error);

        int SuggestId();

        bool Check(string pageName, int id, int layer, bool fullScreen, out string error);

        UIPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen);

        bool Rollback(string backup, out string error);

        UIPageRowLookup LookupRow(string pageName, int id, out UIPageTableRow row, out string error);
    }
}
