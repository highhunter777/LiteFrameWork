using System;
using System.Collections.Generic;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>写表结果：**备份先于写**，失败/超时也回传 <see cref="BackupPath"/>（登记核验回滚用）。</summary>
    public sealed class UiPageTableWriteResult
    {
        public bool Ok;
        public string BackupPath;
        public string Error;

        public static UiPageTableWriteResult Success(string backupPath)
            => new UiPageTableWriteResult { Ok = true, BackupPath = backupPath };

        public static UiPageTableWriteResult Failure(string error, string backupPath = null)
            => new UiPageTableWriteResult { Ok = false, Error = error, BackupPath = backupPath };
    }

    /// <summary>行核验三态：读失败必须与“行不存在”区分，否则回滚复核会假绿。</summary>
    public enum UiPageRowLookup
    {
        Unknown = 0,
        Present,
        Absent,
    }

    /// <summary>
    /// 表写/回滚/核验的端口（《UI 编辑器工具链专项设计》§3.6）：生产实现＝<see cref="UiPageTableHelper"/>；
    /// 夹具可注入“已落行但报失败/超时”的假实现，用于验证登记的核验→回滚→复核路径。
    /// </summary>
    public interface IUiPageTablePort
    {
        bool TryListPages(out List<UiPageTableRow> rows, out string error);

        int SuggestId();

        bool Check(string pageName, int id, int layer, bool fullScreen, out string error);

        UiPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen);

        bool Rollback(string backup, out string error);

        UiPageRowLookup LookupRow(string pageName, int id, out UiPageTableRow row, out string error);
    }

    /// <summary>默认端口：把调用转发到 <see cref="UiPageTableHelper"/>（表路径在构造时注入）。</summary>
    public sealed class UiPageTableHelperPort : IUiPageTablePort
    {
        private readonly string _tablePath;

        public UiPageTableHelperPort(string tablePath = null)
        {
            _tablePath = tablePath;
        }

        public bool TryListPages(out List<UiPageTableRow> rows, out string error)
            => UiPageTableHelper.TryListPages(out rows, out error, _tablePath);

        public int SuggestId() => UiPageTableHelper.SuggestId(_tablePath);

        public bool Check(string pageName, int id, int layer, bool fullScreen, out string error)
            => UiPageTableHelper.Check(pageName, id, layer, fullScreen, out error, _tablePath);

        public UiPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen)
            => UiPageTableHelper.Write(pageName, id, layer, fullScreen, _tablePath);

        public bool Rollback(string backup, out string error)
            => UiPageTableHelper.Rollback(backup, out error, _tablePath);

        public UiPageRowLookup LookupRow(string pageName, int id, out UiPageTableRow row, out string error)
            => UiPageTableHelper.LookupRow(pageName, id, out row, out error, _tablePath);
    }
}
