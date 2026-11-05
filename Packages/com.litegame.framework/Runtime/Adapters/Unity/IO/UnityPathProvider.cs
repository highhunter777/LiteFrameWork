using UnityEngine;

namespace LiteFramework
{
    /// <summary>IPathProvider 的 Unity 实现。**纯实现类,不是 MonoBehaviour**——由 GameEntry.Awake
    /// `new` 出来注入 FileSystem.Init。persistentDataPath 只能在主线程读,此处属性直读即可。</summary>
    public sealed class UnityPathProvider : IPathProvider
    {
        public string RootPath => Application.persistentDataPath;
    }
}
