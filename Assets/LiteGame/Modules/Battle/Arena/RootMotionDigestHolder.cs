using System.Threading;

namespace LiteGame.Modules.Battle.Arena
{
    /// <summary>
    /// 根位移内容摘要的持有者（<see cref="IRootMotionContentDigest"/> 的战斗域实现）：容器装配期注册，
    /// 对局装载 <see cref="RootMotionCatalog"/> 成功后由视图装配件写入摘要。
    ///
    /// **时序**：初始为空字符串——内容身份在首个对局之前即可读，但此时没有对局在跑，
    /// 摘要不参与任何准入判定；首个对局装载成功后才有值（此后各局重复写入同一摘要，
    /// 换内容包后写入新摘要）。
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
