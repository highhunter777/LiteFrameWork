using System;
using System.Collections.Generic;
using System.Threading;
using LiteFramework;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// 注册表填充器（M3 步骤 2.4，手册步骤 4③）：读注册表三件套，逐行 require/校验/包适配 → Fill →
    /// <see cref="RegistryFillReport"/>。单项失败记入报告继续，整批跑完统一判定（§3.4，不遇错即停）；
    /// 错误语义区分"未注册"（配置漏配——表行引用的 Lua 文件不在预载缓存）与"注册失败"（lua 炸了/未返回表）。
    /// 路径常量一律走 LuaKeys 生成物（§2.0 收口判据，设计方案 §277）。策略行 LuaPath 为空 = C# 默认实现，
    /// 跳过不计。M3 注册表直存 require 所得逻辑表（LuaTable）；LuaBehaviourAdapter 包适配器随 M4 UI 壳接入。
    /// DevReload（§2.7）重填走本类同一条路径，不写第二份。
    /// </summary>
    public sealed class RegistryFiller
    {
        private readonly IConfigService _config;
        private readonly LuaComponent _lua;
        private readonly IUILuaRegistry _ui;
        private readonly IContentLuaRegistry _content;
        private readonly IStrategyLuaRegistry _strategies;

        public RegistryFiller(IConfigService config, LuaComponent lua,
            IUILuaRegistry ui, IContentLuaRegistry content, IStrategyLuaRegistry strategies)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        }

        /// <summary>
        /// 整批填充（同步——require 走 env 同步签名，loader 同步契约 §4.2；ct 为表间检查点）。
        /// 调用契约：config.LoadAsync 与 lua.DoMain 已完成（ProcedurePreload 五步序 ③）。
        /// </summary>
        public RegistryFillReport FillAll(CancellationToken ct = default)
        {
            var report = new RegistryFillReport();
            var failed = new HashSet<string>(StringComparer.Ordinal);      // "kind|key"——LuaKeys 校验去重

            FillRows(report, failed, "UI", _config.Tables.Tbuiform.DataList, static r => r.LuaPath, _ui, ct);
            FillRows(report, failed, "Content", _config.Tables.Tbcontententry.DataList, static r => r.Entry, _content, ct);
            FillRows(report, failed, "Strategies", _config.Tables.Tbstrategy.DataList, static r => r.LuaPath, _strategies, ct);
            ValidateKnownKeys(report, failed);
            return report;
        }

        private void FillRows<TRow>(RegistryFillReport report, HashSet<string> failed, string kind,
            IReadOnlyList<TRow> rows, Func<TRow, string> pathOf, ILuaRegistry<LuaTable> registry, CancellationToken ct)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                string fullPath = pathOf(row);
                if (string.IsNullOrWhiteSpace(fullPath)) continue;         // 留空 = C# 默认实现（§4.4），不计入 Total

                report.Total++;
                FillRow(report, failed, kind, fullPath.Trim(), registry);
            }
        }

        private void FillRow(RegistryFillReport report, HashSet<string> failed, string kind,
            string fullPath, ILuaRegistry<LuaTable> registry)
        {
            try
            {
                int dot = fullPath.IndexOf('.');
                if (dot <= 0 || dot == fullPath.Length - 1)
                    throw new FormatException("非两段式路径（§4.4 约定:根.名字）");
                if (fullPath.Substring(0, dot) != kind)
                    throw new FormatException($"路径根与所在表不符（应为 {kind}.<名字>）");
                if (!_lua.HasCached(fullPath))
                    throw new InvalidOperationException("预载缓存未命中");   // 归类"未注册"（配置漏配）

                // require 经诊断口 DoString（启动期低频入口，动态转换符合 §4.3 热路径纪律）
                object[] result = _lua.DoString($"return require('{fullPath}')", "registry_fill");
                if (result == null || result.Length == 0 || result[0] is not LuaTable table)
                    throw new InvalidOperationException("模块未返回逻辑表（缺 return 或返回非 table）");

                registry.Fill(fullPath, table);                            // 重复 Fill 由注册表抛（双向校验另一侧）
                report.Filled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string reason = ex is FormatException
                    ? $"路径非法:{ex.Message}"
                    : ex.Message.Contains("预载缓存未命中")
                        ? $"未注册(配置漏配——表行引用的 Lua 文件不存在):{ex.Message}"
                        : $"注册失败(lua 异常):{ex.Message}";
                report.RecordFailure(fullPath, kind, reason);
                failed.Add($"{kind}|{fullPath}");
            }
        }

        /// <summary>LuaKeys 常量校验（§2.0 消费点）：编译期已知的约定路径必须可解析——漏配在启动期当场暴露。</summary>
        private void ValidateKnownKeys(RegistryFillReport report, HashSet<string> failed)
        {
            ValidateKey(report, failed, "UI", _ui, LuaKeys.UI.UIMain);
            ValidateKey(report, failed, "Content", _content, LuaKeys.Content.DemoEntry);
        }

        private void ValidateKey(RegistryFillReport report, HashSet<string> failed,
            string kind, ILuaRegistry<LuaTable> registry, string key)
        {
            if (registry.Has(key) || failed.Contains($"{kind}|{key}")) return;

            report.Total++;
            report.RecordFailure(key, kind, "LuaKeys 常量未注册——三件套行漏配或该行填充失败（核对 xlsx 路径列与 gen 产物）");
        }
    }
}
