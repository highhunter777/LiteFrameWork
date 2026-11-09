namespace LiteFramework
{
    /// <summary>
    /// 内容代次推进端口：ContentGeneration 持有固定资源目录、脚本集合、配置快照及依赖 ID。
    /// </summary>
    public interface IGenerationSink
    {
        void Advise(ContentGeneration generation);
    }
}
