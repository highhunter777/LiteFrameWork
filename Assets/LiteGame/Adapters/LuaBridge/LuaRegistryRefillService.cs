using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>
    /// 运行期 Lua 注册表增量重填（设计方案 §2.3 的"合法重入之二"）： **不重建 env** —— 重预载 → 释放旧缓存 → 清 require 缓存 → 注册表 Clear → 走同一条 RegistryFi。
    /// </summary>
    public sealed class LuaRegistryRefillService : IModuleStats
    {
        /// <summary>注册表根（与 `Luban/gen_lua_keys.py` 的 ROOTS 同源：封闭集，
        /// 也是 `package.loaded` 里可安全清理的前缀集）。</summary>
        private static readonly string[] RegistryRoots = { "UI" };

        private readonly ILuaHost _lua;
        private readonly UIService _ui;
        private readonly IReadOnlyList<PageDefinition> _pages;
        private readonly IUILuaRegistry _uiRegistry;

        public LuaRegistryRefillService(ILuaHost lua, UIService ui, IReadOnlyList<PageDefinition> pages,
            IUILuaRegistry uiRegistry)
        {
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            _uiRegistry = uiRegistry ?? throw new ArgumentNullException(nameof(uiRegistry));
        }

        /// <summary>最近一次重填报告（未重填过为 null）。</summary>
        public RegistryFillReport LastReport { get; private set; }

        /// <summary>注册表失效纪元（HUD 读数；**不作失效判据**——判据是 `UIService.StaleLogicCount`）。</summary>
        public int Generations => _uiRegistry.Generation;

        /// <summary>
        /// 执行一次增量重填。返回填充报告（失败项已逐条记日志，不抛——单项失败不阻断整批，§3.4）。
        /// </summary>
        public async UniTask<RegistryFillReport> RefillAsync(CancellationToken ct = default)
        {
            // ① 先打标：此刻起界面"逻辑已过期"，但旧表仍可用——缩小"新字节已上线、旧逻辑还在跑"的窗口。
            _ui.MarkLogicStale();

            // ② 重预载：变更后的 .lua 字节进缓存（不重建 env）。
            await _lua.RepreloadAsync(ct);

            // ③ 释放 Bridge 缓存位持有的旧 LuaTable 引用（与 DevReload 同一前置动作）。
            Bridge.Data.ClearLuaCaches();

            // ④ 清 require 缓存：否则 require 命中 package.loaded 里的旧 chunk，重填等于没填。
            int cleared = _lua.ClearRequireCacheByRoots(RegistryRoots);

            // ⑤ 注册表清空（Fill 重复抛——清是重填的前置；Generation 前进一位）。
            _uiRegistry.Clear();

            // ⑥ 同一条填充路径（与启动期 / DevReload 共用 RegistryFiller，不写第二份）。
            var filler = new RegistryFiller(_pages, _lua, _uiRegistry);
            var report = filler.FillAll(ct);
            LastReport = report;

            // ⑦ 池中界面立刻换表；Active 界面保留标记，等 Close 时换。
            _ui.ApplyStaleLogic();

            if (report.HasFailures)
            {
                foreach (var f in report.Failures)
                    Log.Error($"增量重填失败 [{f.kind}] {f.key}:{f.reason}", LogTag.LuaRefill);
            }
            Log.Info($"增量重填完成：清 require {cleared} 项、填充 {report.Filled}/{report.Total}、失败 {report.Failed}、" +
                     $"纪元 {Generations}、待更新界面 {_ui.StaleLogicCount}", LogTag.LuaRefill);
            return report;
        }

        // ---- IModuleStats（DevHud：注册即发现）----。

        string IModuleStats.StatsName => "LuaRefill";

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into.Clear();                                    // 契约：实现 Clear + 填（HUD 不清，全实现统一）
            into["纪元"] = Generations.ToString();
            into["填充"] = LastReport != null ? $"{LastReport.Filled}/{LastReport.Total}" : "-";
            into["失败"] = LastReport != null ? LastReport.Failed.ToString() : "-";
            into["待更新"] = _ui.StaleLogicCount.ToString();
        }
    }
}
