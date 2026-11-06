namespace LiteGame.Editor.UITools.Page
{
    /// <summary>行核验三态：读失败必须与“行不存在”区分，否则回滚复核会假绿。</summary>
    public enum UIPageRowLookup
    {
        Unknown = 0,
        Present,
        Absent,
    }
}
