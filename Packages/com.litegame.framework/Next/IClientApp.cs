using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFrameworkNext
{
    /// <summary>热更侧应用契约：AOT 引导件只认本接口，编译期不引用任何热更类型。</summary>
    public interface IClientApp
    {
        /// <summary>构造后初始化（不打开页面、不进流程）。</summary>
        UniTask InitializeAsync(CancellationToken ct);

        /// <summary>进顶层流程；取消即关闭（各功能在 finally 内清理）。</summary>
        UniTask RunAsync(CancellationToken ct);

        /// <summary>逐帧驱动（由 AOT 引导件的 Update 调用）。</summary>
        void Tick(float deltaTime);

        /// <summary>逆序关闭并释放资源。</summary>
        UniTask ShutdownAsync();
    }
}
