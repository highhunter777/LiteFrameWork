using System;
using System.Collections.Generic;
using System.Threading;
using LiteFramework;
using XLua;
using LiteFramework.UI;
using LiteGame.Modules.Shared;
using LiteGame.Modules.UI;

namespace LiteGame
{
    /// <summary>
    /// 注册表填充器：读页面目录，逐行 require/校验/包适配 → Fill → RegistryFillReport。
    /// </summary>
    public sealed class RegistryFiller : ILuaRegistryFiller
    {
        /// <summary>启动期 LuaKeys 校验锚：取真实产品首屏（UIMain 样例页已退役）；改首屏只改这一处。</summary>
        public const string KnownUIKey = LuaKeys.UI.Page_Login;
        private readonly IReadOnlyList<PageDefinition> _pages;
        private readonly ILuaHost _lua;
        private readonly IUILuaRegistry _ui;

        public RegistryFiller(IReadOnlyList<PageDefinition> pages, ILuaHost lua, IUILuaRegistry ui)
        {
            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        /// <summary>
        /// 整批填充（同步——require 走 env 同步签名，loader 同步契约 §4.2；ct 为表间检查点）。
        /// 调用契约：config.LoadAsync 与 lua.DoMain 已完成（ProcedurePreload 五步序 ③）。
        /// </summary>
        public RegistryFillReport FillAll(CancellationToken ct = default)
        {
            var report = new RegistryFillReport();
            var failed = new HashSet<string>(StringComparer.Ordinal);      // "kind|key"——LuaKeys 校验去重

            FillRows(report, failed, "UI", _pages, static p => p.LuaPath, _ui, ct);
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

                // require 经诊断口 DoString（启动期低频入口，动态转换符合 §4.3 热路径纪律）。
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
            ValidateKey(report, failed, "UI", _ui, KnownUIKey);
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
