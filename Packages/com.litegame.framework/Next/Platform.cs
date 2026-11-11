using System;

namespace LiteFrameworkNext
{
    /// <summary>与热更无关的平台初始化面（日志/版本事实）：AOT 引导件构造后交给热更应用，热更不反向构造它。</summary>
    public sealed class Platform
    {
        /// <summary>构造：名称与版本进入启动日志。</summary>
        public Platform(string name, string version)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("平台名不能为空", nameof(name));
            Name = name;
            Version = version ?? string.Empty;
        }

        /// <summary>平台名（启动日志标识）。</summary>
        public string Name { get; }

        /// <summary>版本串（取自 PlayerSettings 版本）。</summary>
        public string Version { get; }

        /// <summary>是否已初始化；Shutdown 后复位，可再次初始化。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>初始化平台设施；重复调用幂等。</summary>
        public void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            Log.Info("平台初始化：name=" + Name + " version=" + Version);
        }

        /// <summary>关闭平台设施（逆序于 Initialize）；未初始化时为空操作。</summary>
        public void Shutdown()
        {
            if (!IsInitialized) return;
            IsInitialized = false;
            Log.Info("平台已关闭：" + Name);
        }
    }
}
