using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// 构建形态的**运行时事实**（替代产品文件里的开发宏分支，M3-6b）： 编辑器或 Development Player = 开发形态——与 `UNITY_EDITOR || DEVELOPMENT_BUILD` 同义 。
    /// </summary>
    public static class DevBuildFacts
    {
        /// <summary>是否开发形态（编辑器 / Development Player）。</summary>
        public static bool IsDevelopment => Application.isEditor || Debug.isDebugBuild;
    }
}
