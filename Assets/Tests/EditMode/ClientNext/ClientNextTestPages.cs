using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Content;
using LiteGameNext.Platform.Ui;

namespace LiteGame.Tests.EditMode
{
    /// <summary>ClientNext 用例的页面工厂：真实 prefab 由内容服务按文件名装载（编辑器分支）。</summary>
    internal static class ClientNextTestPages
    {
        public static System.Func<PageSpec, CancellationToken, UniTask<PageBase>> PageFactory()
        {
            var content = new NextContentService();
            return (spec, ct) => EditorPageFactory.CreateAsync(spec, ct, content);
        }
    }
}
