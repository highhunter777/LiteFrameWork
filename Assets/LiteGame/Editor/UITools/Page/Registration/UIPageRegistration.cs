using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LiteGame.Editor.UITools.Binding;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Page
{
    /// <summary>登记失败步（结果可核验：夹具按步断言，不能只断言 Ok=false）。</summary>
    public enum UIPageRegisterFailure
    {
        None = 0,
        Validate,
        Contract,
        WriteTable,
        Codegen,
        Manifest,
        ContractRefresh,
        NotImplemented,
    }

    /// <summary>代码生成步骤结果（Luban / BuildHash 各一次）。</summary>
    public struct UIPageCodegenRun
    {
        public bool Ok;
        public string Output;
        public string Error;

        public static UIPageCodegenRun Success(string output = null)
            => new UIPageCodegenRun { Ok = true, Output = output };

        public static UIPageCodegenRun Failure(string error, string output = null)
            => new UIPageCodegenRun { Ok = false, Error = error, Output = output };
    }

    /// <summary>
    /// 代码生成链端口（§3.6 step 2 + 红线 1）：Luban 重生成表链后再复算 BuildHash。
    /// 夹具注入假实现验证调用顺序与失败回滚；生产实现走 `codegen.luban` → `codegen.build-hash`。
    /// </summary>
    public interface IUIPageCodegenPipeline
    {
        UIPageCodegenRun RunLuban();
        UIPageCodegenRun RefreshBuildHash();
    }

    /// <summary>登记后刷新页面契约（真实 formId/layer/fullScreen/cacheStrategy）。</summary>
    public interface IUIPageContractRefresher
    {
        bool TryRefresh(UIPageManifestEntry entry, out string error);
    }

    /// <summary>登记请求（路径与端口可注入；默认值＝生产面）。</summary>
    public sealed class UIPageRegisterRequest
    {
        public string PageName;
        public int Id;
        public int Layer = 1;
        public bool FullScreen = true;
        public string CacheStrategy = UIPageContract.DefaultCacheStrategy;

        public string ManifestPath = UIPageManifestPaths.DefaultPath;
        public string ContractPath;
        public string TablePath;
        public string BindManifestPath = Binding.UIBindGenerationHash.DefaultManifestPath;

        /// <summary>null＝生产代码生成链；注入表路径时必须同时注入，防止测试表配生产生成链。</summary>
        public IUIPageCodegenPipeline Codegen;

        /// <summary>null＝生产表门面（<see cref="UIPageTableHelperPort"/>，表路径取 <see cref="TablePath"/>）。</summary>
        public IUIPageTablePort Table;

        /// <summary>null＝默认刷新器（批量重生成 + 原子写盘）。</summary>
        public IUIPageContractRefresher ContractRefresher;
    }

    /// <summary>登记结果（显式失败步 + 恢复信息）。</summary>
    public sealed class UIPageRegisterResult
    {
        public bool Ok;
        public UIPageRegisterFailure FailedAt = UIPageRegisterFailure.None;
        public string Error;
        public string BackupPath;
        public int FormId;
        public string Status;
        public string CacheStrategy;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Log = new List<string>();
    }

    /// <summary>
    /// 页面登记内核（《UI 编辑器工具链专项设计》§3.6）：契约门槛 → 写表 → Luban/BuildHash → 清单状态/缓存 →
    /// 契约刷新 → 绑定 hash（仅路径 B）。窗口只做输入与呈现；失败显式、可核验、可恢复。
    /// </summary>
    public static class UIPageRegistration
    {
        public static UIPageRegisterResult Register(UIPageRegisterRequest request)
        {
            var result = new UIPageRegisterResult();
            if (request == null)
                return Fail(result, UIPageRegisterFailure.Validate, "登记请求为空");

            string pageName = request.PageName ?? string.Empty;
            string prefabPath = UIValidator.ScreensDir + "/" + pageName + ".prefab";
            string contractPath = string.IsNullOrEmpty(request.ContractPath)
                ? UIContractPaths.ForPage(pageName)
                : request.ContractPath;
            string manifestPath = string.IsNullOrEmpty(request.ManifestPath)
                ? UIPageManifestPaths.DefaultPath
                : request.ManifestPath;
            var table = request.Table ?? new UIPageTableHelperPort(request.TablePath);
            IUIPageCodegenPipeline codegen = request.Codegen ?? new UIPageCodegenToolchain();

            // 0) 注入一致性：真实 Luban 永远读生产表，注入表（路径或端口）必须同时注入生成链。
            if (request.Codegen == null && (request.Table != null || !string.IsNullOrEmpty(request.TablePath)))
                return Fail(result, UIPageRegisterFailure.Validate,
                    "注入表路径/表端口时必须同时注入代码生成链（真实 Luban 读生产表，混用会造成生成物错位）");

            // 1) 纯输入校验
            if (!UIPageScaffold.IsValidPageName(pageName))
                return Fail(result, UIPageRegisterFailure.Validate, "页面名须为 Page_<PascalCase>");
            if (request.Id <= 0)
                return Fail(result, UIPageRegisterFailure.Validate, "id 必须为正整数");
            if (request.Layer < 0 || request.Layer > 3)
                return Fail(result, UIPageRegisterFailure.Validate, "layer 必须在 0..3");
            if (!UIPageContract.IsValidCacheStrategy(request.CacheStrategy))
                return Fail(result, UIPageRegisterFailure.Validate, "缓存策略非法：" + request.CacheStrategy);

            // 2) 页面/清单/草稿状态
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return Fail(result, UIPageRegisterFailure.Validate, "prefab 不存在（先起页）：" + prefabPath);
            UIPageManifest manifest;
            try
            {
                manifest = UIPageManifestStore.LoadIfExists(manifestPath);
            }
            catch (Exception exception)
            {
                return Fail(result, UIPageRegisterFailure.Validate, "页面清单不可读：" + exception.Message);
            }
            if (manifest == null)
                return Fail(result, UIPageRegisterFailure.Validate, "页面清单不存在（先起页/对账）：" + manifestPath);
            UIPageManifestEntry entry = manifest.Pages?.Find(item => item != null &&
                string.Equals(item.Name, pageName, StringComparison.Ordinal));
            if (entry == null)
                return Fail(result, UIPageRegisterFailure.Validate, "页面清单无该页草稿（先起页）：" + pageName);
            if (entry.Forms != null && entry.Forms.Count > 0)
                return Fail(result, UIPageRegisterFailure.Validate, "页面已登记（forms 非空），不重复登记：" + pageName);
            if (!UIPageStatusRules.TryParse(entry.Status, out UIPageStatus status) || status != UIPageStatus.Draft)
                return Fail(result, UIPageRegisterFailure.Validate, "页面状态非 Draft：" + entry.Status);

            // 3) 契约门槛（§3.5：无契约不交接）：草稿契约、同名、formId=0、guid 与当前 prefab 一致。
            UIPageContract contract;
            try
            {
                contract = UIContractStore.Load(contractPath);
            }
            catch (Exception exception)
            {
                return Fail(result, UIPageRegisterFailure.Contract,
                    "草稿契约缺失/不可读（先跑 ui.contract）：" + exception.Message);
            }
            if (contract?.Page == null || !string.Equals(contract.Page.Name, pageName, StringComparison.Ordinal))
                return Fail(result, UIPageRegisterFailure.Contract, "契约页面名不匹配：" + contractPath);
            if (contract.Page.FormId != 0)
                return Fail(result, UIPageRegisterFailure.Contract,
                    "契约不是草稿（formId=" + contract.Page.FormId + "），该页可能已登记");
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (!string.Equals(contract.Page.PrefabGuid, guid, StringComparison.Ordinal))
                return Fail(result, UIPageRegisterFailure.Contract,
                    "草稿契约与当前 prefab 不一致（guid 不符），重跑 ui.contract");

            // 4) 表写前校验（Python 单源：id 冲突/保留区/路径/layer）
            if (!table.Check(pageName, request.Id, request.Layer, request.FullScreen, out string tableError))
                return Fail(result, UIPageRegisterFailure.Validate, tableError);

            // 6) 写表（备份先于写；失败/超时也回传备份句柄）
            UIPageTableWriteResult written = table.Write(pageName, request.Id, request.Layer, request.FullScreen);
            result.BackupPath = written.BackupPath;
            if (!written.Ok)
            {
                string recovery = RecoverRow(table, pageName, request.Id, written.BackupPath);
                return Fail(result, UIPageRegisterFailure.WriteTable,
                    "写表失败：" + written.Error + recovery);
            }
            result.Log.Add("表行已写入（备份：" + written.BackupPath + "）");

            // 7) 代码生成链：Luban → BuildHash；失败回滚表并重跑 Luban 复原生成读码面。
            UIPageCodegenRun luban = codegen.RunLuban();
            if (!luban.Ok)
                return FailCodegen(result, table, pageName, request.Id, written.BackupPath, codegen, "Luban", luban);
            result.Log.Add("Luban 重生成完成");

            UIPageCodegenRun hash = codegen.RefreshBuildHash();
            if (!hash.Ok)
                return FailCodegen(result, table, pageName, request.Id, written.BackupPath, codegen, "BuildHash", hash);
            result.Log.Add("BuildHash 已复算");

            // 8) 清单：对账 → 状态/缓存 → 保存（失败不回滚表，给恢复指令）。
            if (!table.TryListPages(out List<UIPageTableRow> rows, out string listError))
                return Fail(result, UIPageRegisterFailure.Manifest,
                    "表已写入但读取失败：" + listError + "（重跑 ui.page-manifest）");
            UIPageManifestSyncResult sync = UIPageManifestSync.Sync(rows, manifest);
            if (!sync.Ok)
                return Fail(result, UIPageRegisterFailure.Manifest,
                    "表已写入但清单对账失败：" + sync.ErrorText + "（重跑 ui.page-manifest）");
            if (!UIPageManifestEditor.SetCacheStrategy(sync.Manifest, pageName, request.CacheStrategy,
                    out string cacheError))
                return Fail(result, UIPageRegisterFailure.Manifest, "缓存策略写入失败：" + cacheError);
            if (!UIPageManifestEditor.SetStatus(sync.Manifest, pageName, UIPageStatus.Registered,
                    out string statusError))
                return Fail(result, UIPageRegisterFailure.Manifest, "状态迁移失败：" + statusError);

            UIPageManifestEntry registered = sync.Manifest.Pages.Find(item => item != null &&
                string.Equals(item.Name, pageName, StringComparison.Ordinal));
            if (registered == null)
                return Fail(result, UIPageRegisterFailure.Manifest, "清单对账后缺该页条目（重跑 ui.page-manifest）");
            registered.ContractPath = contractPath;
            registered.ContractPresent = File.Exists(FullPath(contractPath));
            try
            {
                UIPageManifestStore.Save(sync.Manifest, manifestPath);
            }
            catch (Exception exception)
            {
                return Fail(result, UIPageRegisterFailure.Manifest,
                    "表/生成物已提交，但清单写盘失败：" + exception.Message + "（重跑 ui.page-manifest）");
            }

            result.FormId = request.Id;
            result.Status = registered.Status;
            result.CacheStrategy = registered.CacheStrategy;
            result.Log.Add("清单：" + manifestPath + "（" + registered.Status + "）");

            // 9) 契约刷新（真实身份/维护字段）；失败显式报红，不回滚已完成的登记。
            IUIPageContractRefresher refresher = request.ContractRefresher ?? new UIPageContractBatchRefresher();
            if (!refresher.TryRefresh(registered, out string refreshError))
            {
                return Fail(result, UIPageRegisterFailure.ContractRefresh,
                    "表单已登记但契约未刷新，重跑 ui.contract：" + refreshError);
            }
            result.Log.Add("契约已刷新：" + contractPath);

            // 10) 绑定 hash：仅路径 B（挂 BindRoot）记录；路径 A 不伪造。
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null && prefab.GetComponent<LiteCodeGen.BindRoot>() != null)
            {
                try
                {
                    UIBindGenerationHash.Record(prefabPath, request.BindManifestPath);
                    result.Log.Add("绑定 hash 已记录：" + request.BindManifestPath);
                }
                catch (Exception exception)
                {
                    result.Warnings.Add("绑定 hash 记录失败（可经 ui.binding-hash-selected 重记）：" + exception.Message);
                }
            }
            else
            {
                result.Log.Add("无 BindRoot（路径 A）——不记录绑定 hash");
            }

            result.Ok = true;
            return result;
        }

        private static UIPageRegisterResult FailCodegen(UIPageRegisterResult result, IUIPageTablePort table,
            string pageName, int id, string backup, IUIPageCodegenPipeline codegen, string step,
            UIPageCodegenRun run)
        {
            string recovery = RollbackAndVerify(table, pageName, id, backup);
            UIPageCodegenRun restored = codegen.RunLuban();
            string restoreText = restored.Ok
                ? "已重跑 Luban 复原生成读码面"
                : "复原 Luban 失败：" + (restored.Error ?? "未知") + "（需手工重跑 gen.bat）";
            return Fail(result, UIPageRegisterFailure.Codegen,
                step + " 失败：" + (run.Error ?? run.Output) + "；" + recovery + "；" + restoreText);
        }

        private static string RecoverRow(IUIPageTablePort table, string pageName, int id, string backup)
        {
            UIPageRowLookup lookup = table.LookupRow(pageName, id, out _, out string lookupError);
            switch (lookup)
            {
                case UIPageRowLookup.Absent:
                    return "；核验：表未落行";
                case UIPageRowLookup.Unknown:
                    return "；核验失败（读表异常：" + lookupError + "），请人工核对";
                case UIPageRowLookup.Present:
                    return "；核验：表已落行" + RollbackAndVerify(table, pageName, id, backup);
                default:
                    return "；核验结果未知（" + lookup + "），请人工核对";
            }
        }

        private static string RollbackAndVerify(IUIPageTablePort table, string pageName, int id, string backup)
        {
            if (string.IsNullOrEmpty(backup))
                return "；无备份句柄，无法自动回滚（请手工核对表）";

            if (!table.Rollback(backup, out string rollbackError))
                return "；回滚失败：" + rollbackError + "（按备份手动恢复 " + backup + "）";

            UIPageRowLookup lookup = table.LookupRow(pageName, id, out _, out string lookupError);
            switch (lookup)
            {
                case UIPageRowLookup.Present:
                    return "；回滚后复核仍见行（按备份手动恢复 " + backup + "）";
                case UIPageRowLookup.Unknown:
                    return "；回滚后无法复核（读表异常：" + lookupError + "，按备份手动核对 " + backup + "）";
                case UIPageRowLookup.Absent:
                    return "；已回滚并复核（行不存在）";
                default:
                    return "；回滚结果未知（" + lookup + "），按备份手动核对 " + backup;
            }
        }

        private static UIPageRegisterResult Fail(UIPageRegisterResult result, UIPageRegisterFailure step,
            string error)
        {
            result.Ok = false;
            result.FailedAt = step;
            result.Error = error;
            result.Log.Add("失败：" + error);
            return result;
        }

        private static string FullPath(string assetPath)
            => Path.Combine(UIValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
