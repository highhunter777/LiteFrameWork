using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 运行期 Lua 注册表增量重填（设计方案 §2.3 的"合法重入之二"，2026-09-17 交付）：
    /// **不重建 env** —— 重预载 → 释放旧缓存 → 清 require 缓存 → 三注册表 Clear → 走同一条
    /// <see cref="RegistryFiller.FillAll"/> 重填 → 通知 UI 壳换表。
    ///
    /// 与 DevReload 的分工：DevReload 是**重建 env 的重锤**（Editor 期，先全关界面）；
    /// 本服务是**不动 env 的轻路径**（运行期热更用），因此必须自己处理"旧 LuaTable 与旧适配器"的交接：
    ///  - 已打开的界面：`UIService.MarkLogicStale()` 打标（**先标后清**），界面继续用旧表跑到关闭，
    ///    关闭后换表（最终一致，不假装瞬间切换——§2.3 第 4 点）；
    ///  - 池中界面：`ApplyStaleLogic()` 立刻换表并标记补跑 OnInit。
    ///
    /// 顺序钉死（半更新窗口尽量短，见 <see cref="RefillAsync"/> 内注释）。
    /// 触发点：M11 流程（回主城 / 战斗结束的检查点）+ 调试菜单；Specify 的安全窗口纪律由调用方承担。
    /// </summary>
    public sealed class LuaRegistryRefillService : IModuleStats
    {
        /// <summary>注册表根（与 `Luban/gen_lua_keys.py` 的 ROOTS 同源：封闭集，
        /// 也是 `package.loaded` 里可安全清理的前缀集）。</summary>
        private static readonly string[] RegistryRoots = { "UI", "Content", "Strategies" };

        private readonly LuaComponent _lua;
        private readonly UIService _ui;
        private readonly IConfigService _config;
        private readonly IUILuaRegistry _uiRegistry;
        private readonly IContentLuaRegistry _contentRegistry;
        private readonly IStrategyLuaRegistry _strategyRegistry;

        public LuaRegistryRefillService(LuaComponent lua, UIService ui, IConfigService config,
            IUILuaRegistry uiRegistry, IContentLuaRegistry contentRegistry, IStrategyLuaRegistry strategyRegistry)
        {
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _uiRegistry = uiRegistry ?? throw new ArgumentNullException(nameof(uiRegistry));
            _contentRegistry = contentRegistry ?? throw new ArgumentNullException(nameof(contentRegistry));
            _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
        }

        /// <summary>最近一次重填报告（未重填过为 null）。</summary>
        public RegistryFillReport LastReport { get; private set; }

        /// <summary>三注册表失效纪元之和（HUD 读数；**不作失效判据**——判据是 `UIService.StaleLogicCount`）。</summary>
        public int Generations => _uiRegistry.Generation + _contentRegistry.Generation + _strategyRegistry.Generation;

        /// <summary>
        /// 执行一次增量重填。返回填充报告（失败项已逐条记日志，不抛——单项失败不阻断整批，§3.4）。
        /// </summary>
        public async UniTask<RegistryFillReport> RefillAsync(CancellationToken ct = default)
        {
            // ① 先打标：此刻起界面"逻辑已过期"，但旧表仍可用——缩小"新字节已上线、旧逻辑还在跑"的窗口
            _ui.MarkLogicStale();

            // ② 重预载：变更后的 .lua 字节进缓存（不重建 env）
            await _lua.RepreloadAsync(ct);

            // ③ 释放 Bridge 缓存位持有的旧 LuaTable 引用（与 DevReload 同一前置动作）
            Bridge.Data.ClearLuaCaches();

            // ④ 清 require 缓存：否则 require 命中 package.loaded 里的旧 chunk，重填等于没填
            int cleared = _lua.ClearRequireCacheByRoots(RegistryRoots);

            // ⑤ 三注册表清空（Fill 重复抛——清是重填的前置；Generation 各前进一位）
            _uiRegistry.Clear();
            _contentRegistry.Clear();
            _strategyRegistry.Clear();

            // ⑥ 同一条填充路径（与启动期 / DevReload 共用 RegistryFiller，不写第二份）
            var filler = new RegistryFiller(_config, _lua, _uiRegistry, _contentRegistry, _strategyRegistry);
            var report = filler.FillAll(ct);
            LastReport = report;

            // ⑦ 池中界面立刻换表；Active 界面保留标记，等 Close 时换
            _ui.ApplyStaleLogic();

            if (report.HasFailures)
            {
                foreach (var f in report.Failures)
                    Log.Error($"增量重填失败 [{f.kind}] {f.key}:{f.reason}", "LuaRefill");
            }
            Log.Info($"增量重填完成：清 require {cleared} 项、填充 {report.Filled}/{report.Total}、失败 {report.Failed}、" +
                     $"纪元 {Generations}、待更新界面 {_ui.StaleLogicCount}", "LuaRefill");
            return report;
        }

        // ---- IModuleStats（DevHUD：注册即发现）----

        string IModuleStats.StatsName => "LuaRefill";

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into["纪元"] = Generations.ToString();
            into["填充"] = LastReport != null ? $"{LastReport.Filled}/{LastReport.Total}" : "-";
            into["失败"] = LastReport != null ? LastReport.Failed.ToString() : "-";
            into["待更新"] = _ui.StaleLogicCount.ToString();
        }
    }
}
