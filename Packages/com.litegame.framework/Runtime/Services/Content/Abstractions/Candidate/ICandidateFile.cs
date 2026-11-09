namespace LiteFramework
{
    /// <summary>
    /// 候选文件读句柄（下载、候选校验与容量）。
    /// </summary>
    public interface ICandidateFile
    {
        /// <summary>相对候选根的正斜杠路径（与 <see cref="ReleaseFileEntry.Path"/> 同形）。</summary>
        string Path { get; }

        /// <summary>实际字节长度。</summary>
        long Length { get; }

        /// <summary>读取完整字节；失败返回 null（视为缺失/不可读——调用方按校验失败处理，不抛）。</summary>
        byte[] ReadAll();
    }
}
