namespace LiteFramework
{
    /// <summary>命令标记接口（《命令中心专项设计》§2）：类型即命令、零字段命令合法——
    /// 命令对象生命周期归调用方（Send 同步返回后可复用/池化，中心不持有引用）。</summary>
    public interface ICommand { }
}
