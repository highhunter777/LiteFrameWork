using UnityEngine;

namespace LiteFrameworkNext
{
    /// <summary>统一日志门面（AOT 侧）：Next 子树的引导件与热更代码只经本类写日志。</summary>
    public static class Log
    {
        private const string Prefix = "[LiteGameNext] ";

        /// <summary>普通信息。</summary>
        public static void Info(string message) => Debug.Log(Prefix + message);

        /// <summary>内容降级/可恢复异常。</summary>
        public static void Warning(string message) => Debug.LogWarning(Prefix + message);

        /// <summary>程序员错误/不可恢复失败。</summary>
        public static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
