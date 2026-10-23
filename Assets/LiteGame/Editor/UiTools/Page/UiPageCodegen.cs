using System;
using LiteGame.Editor.UiTools.Contracts;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 代码生成链的生产实现（红线 1）：Luban 重生成表链 → BuildHash 复算两端握手常量。
    /// 夹具注入假实现验证调用顺序与失败回滚；本类只做工具调用与结果映射。
    /// </summary>
    public sealed class UiPageCodegenToolchain : IUiPageCodegenPipeline
    {
        public const string LubanToolId = "codegen.luban";
        public const string BuildHashToolId = "codegen.build-hash";

        public UiPageCodegenRun RunLuban() => Run(LubanToolId);

        public UiPageCodegenRun RefreshBuildHash() => Run(BuildHashToolId);

        private static UiPageCodegenRun Run(string toolId)
        {
            UiPageProcessRun run = UiPageToolProcess.RunToolCommand(toolId);
            return run.Ok
                ? UiPageCodegenRun.Success(run.Output)
                : UiPageCodegenRun.Failure(run.Error, run.Output);
        }
    }

    /// <summary>登记后刷新契约的生产实现：按新条目重生成（真实身份/维护字段）并原子写盘。</summary>
    public sealed class UiPageContractBatchRefresher : IUiPageContractRefresher
    {
        public bool TryRefresh(UiPageManifestEntry entry, out string error)
        {
            error = null;
            try
            {
                UiContractGenerationResult result = UiContractBatch.Regenerate(entry);
                if (!result.Ok)
                {
                    error = result.ErrorText;
                    return false;
                }

                UiContractStore.Save(entry.ContractPath, result.Contract);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
