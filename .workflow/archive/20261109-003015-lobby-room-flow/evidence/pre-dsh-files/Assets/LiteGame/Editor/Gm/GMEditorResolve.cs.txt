using System;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱页件的运行态取件单源：经 <c>GameEntry.TakeContainer()</c> 解析容器服务 （DevReload 同口径——编辑器工具豁免形态，业务侧禁此形态）。
    /// </summary>
    internal static class GMEditorResolve
    {
        internal static T ResolveFromContainer<T>() where T : class
        {
            var entry = UnityEngine.Object.FindAnyObjectByType<GameEntry>();
            if (entry == null) return null;
            try { return entry.TakeContainer().Resolve<T>(); }
            catch (Exception) { return null; }   // 未装配/未注册：面板各区显示空态
        }

        /// <summary>剥 TargetInvocationException 外壳（反射执行命令的异常归因到处理器本体）。</summary>
        internal static Exception Unwrap(Exception ex)
            => ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : ex;
    }
}
