using System;
using LiteGame.Editor.UITools.Contracts;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>
    /// 代码生成链的生产实现（红线 1）：Luban 重生成表链 → BuildHash 复算两端握手常量。
    /// 夹具注入假实现验证调用顺序与失败回滚；本类只做工具调用与结果映射。
    /// </summary>
    public sealed class UIPageCodegenToolchain : IUIPageCodegenPipeline
    {
        public const string LubanToolId = "codegen.luban";
        public const string BuildHashToolId = "codegen.build-hash";

        public UIPageCodegenRun RunLuban() => Run(LubanToolId);

        public UIPageCodegenRun RefreshBuildHash() => Run(BuildHashToolId);

        private static UIPageCodegenRun Run(string toolId)
        {
            UIPageProcessRun run = UIPageToolProcess.RunToolCommand(toolId);
            return run.Ok
                ? UIPageCodegenRun.Success(run.Output)
                : UIPageCodegenRun.Failure(run.Error, run.Output);
        }
    }
}
