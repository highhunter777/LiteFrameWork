using UnityEngine;

namespace LiteFramework
{
    /// <summary>ILogHelper 的 Unity 实现。**纯实现类,不是 MonoBehaviour**——由 GameEntry.Awake
    /// `new` 出来注入 Log.SetHelper(Unity 组件不进 DI,桥接实现同样不是组件)。</summary>
    public sealed class UnityLogHelper : ILogHelper
    {
        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Info: Debug.Log(message); break;
                case LogLevel.Warning: Debug.LogWarning(message); break;
                case LogLevel.Error: Debug.LogError(message); break;
                case LogLevel.Fatal: Debug.LogError($"[FATAL] {message}"); break;
            }
        }
    }
}
