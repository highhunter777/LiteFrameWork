using System.Threading;
using LiteGame.Modules.Battle.Abstractions.View;

namespace LiteGame.Modules.Battle.Arena
{
    /// <summary>
    /// 根位移内容摘要的持有者（IRootMotionContentDigest 的战斗域实现）：容器装配期注册， 对局装载 RootMotionCatalog 成功后由视图装配件写入摘要。
    /// </summary>
    public sealed class RootMotionDigestHolder : IRootMotionContentDigest
    {
        private string _digest = string.Empty;

        /// <inheritdoc />
        public string Digest => Volatile.Read(ref _digest);

        /// <summary>登记已装载产物的摘要（null 归一为空字符串）。</summary>
        public void Set(string digest)
        {
            Volatile.Write(ref _digest, digest ?? string.Empty);
        }
    }
}
