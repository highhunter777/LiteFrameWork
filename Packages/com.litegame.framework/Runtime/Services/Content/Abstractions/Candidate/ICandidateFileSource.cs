namespace LiteFramework
{
    /// <summary>
    /// 候选文件访问端口（IO 由装配点提供：Player 用文件系统，测试用内存假件）。
    /// **Core 不依赖 System.IO 具体实现**——与 <see cref="IActivationRecordIO"/> 同一纪律。
    /// </summary>
    public interface ICandidateFileSource
    {
        /// <summary>打开一个候选文件；不存在/不可读返回 null（调用方按缺失处理）。</summary>
        ICandidateFile Open(string path);
    }
}
