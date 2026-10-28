using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// 构建形态的**运行时事实**（替代产品文件里的开发宏分支，M3-6b）：
    /// 编辑器或 Development Player = 开发形态——与 `UNITY_EDITOR || DEVELOPMENT_BUILD` 同义
    /// （"Development Build" 勾选即 Unity 定义 `DEVELOPMENT_BUILD`，`Debug.isDebugBuild` 返回同一事实）。
    ///
    /// **只读事实、无状态**：产品代码用它选择"开发期 fail-fast / 发布期降级"，不再各自写 `#if`；
    /// 需要更细的开发能力（GM 面板/弱网/调试绘制）仍走 `IDevSession` 端口或 `LiteGame.Dev` 装配层。
    /// 注：`LITEFRAMEWORK_DEBUG` 只存在于 dotnet 侧（Core/测试 csproj），Unity 程序集从不定义它。
    /// </summary>
    public static class DevBuildFacts
    {
        /// <summary>是否开发形态（编辑器 / Development Player）。</summary>
        public static bool IsDevelopment => Application.isEditor || Debug.isDebugBuild;
    }
}
