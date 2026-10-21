using LiteGame.Toolchain;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱页件契约：继承平台 <see cref="IEditorToolPage"/>，生命周期与轮询由宿主壳驱动。
    /// GM 页件仍然是“一页一职责，说明→操作→报告自包含”；运行态读数经 GmEditorResolve 取容器服务。
    /// </summary>
    internal interface IGmPage : IEditorToolPage
    {
    }
}
