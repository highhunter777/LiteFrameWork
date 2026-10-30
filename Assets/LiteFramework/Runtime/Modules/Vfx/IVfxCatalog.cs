namespace LiteFramework
{
    /// <summary>特效名→定义读口（产品目录实现；框架服务只认本接口）。</summary>
    public interface IVfxCatalog
    {
        VfxDef Resolve(string name);
    }

    /// <summary>空目录（未注入产品目录时的合法退化：所有名字解析为无效定义）。</summary>
    internal sealed class EmptyVfxCatalog : IVfxCatalog
    {
        public static readonly EmptyVfxCatalog Instance = new EmptyVfxCatalog();
        public VfxDef Resolve(string name) => default;
    }
}
