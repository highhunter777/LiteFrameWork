using System;
using LiteGame.Editor.UITools.Contracts;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>登记后刷新契约的生产实现：按新条目重生成（真实身份/维护字段）并原子写盘。</summary>
    public sealed class UIPageContractBatchRefresher : IUIPageContractRefresher
    {
        public bool TryRefresh(UIPageManifestEntry entry, out string error)
        {
            error = null;
            try
            {
                UIContractGenerationResult result = UIContractBatch.Regenerate(entry);
                if (!result.Ok)
                {
                    error = result.ErrorText;
                    return false;
                }

                UIContractStore.Save(entry.ContractPath, result.Contract);
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
