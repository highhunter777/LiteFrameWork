using System;
using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>
    /// Bridge 能力声明（《热更与内容发布专项设计》§10"BridgeApiVersion/能力集合覆盖方法签名、语义、
    /// 委托、IL2CPP/AOT 适配与裁剪保留；**新 Lua 引用旧包不存在的能力必须在执行前拒绝**"）。
    ///
    /// **这是能力事实源的一侧**（"当前 Player 具备什么"）。另一侧是候选声明的**要求**
    /// （<see cref="ReleaseCompatibility.BridgeApiVersion"/>）。二者比较后决定接受/拒绝候选。
    ///
    /// **为什么在 LiteGame 而不在 Core**：能力集合是"这个 App 装了什么"的事实，
    /// 由装配点声明；Core 只持校验规则（<c>ReleaseManifestValidator</c>）。
    /// </summary>
    public sealed class BridgeCapabilitySet
    {
        /// <summary>当前 Bridge API 版本（对应 <see cref="ReleaseCompatibility.BridgeApiVersion"/> 的下限比较）。</summary>
        public int ApiVersion { get; }

        private readonly HashSet<string> _capabilities;

        public BridgeCapabilitySet(int apiVersion, IEnumerable<string> capabilities)
        {
            ApiVersion = apiVersion;
            _capabilities = new HashSet<string>(StringComparer.Ordinal);
            if (capabilities != null)
            {
                foreach (string c in capabilities)
                    if (!string.IsNullOrEmpty(c)) _capabilities.Add(c);
            }
        }

        /// <summary>已声明的能力数（诊断）。</summary>
        public int Count => _capabilities.Count;

        /// <summary>是否具备某能力（§10"新 Lua 引用旧包不存在的能力必须在执行前拒绝"）。</summary>
        public bool Has(string capability)
            => !string.IsNullOrEmpty(capability) && _capabilities.Contains(capability);

        /// <summary>
        /// 当前装配的默认能力集。
        ///
        /// **这是显式声明，不是自动探测**：能力集合必须能被审计（哪些 C# 委托对 Lua 开放），
        /// 靠反射扫描自动生成会让"新增一个公开类型 = 自动对 Lua 开放"，与白名单纪律相悖。
        ///
        /// 当前包含 <c>Bridge.BindGlobals</c> 与产品装订面（<c>ProductBridges.BindGlobals</c>）实际绑定的
        /// **七个门面**（data/ui/content/text/meta/settings/room）。**新增 Bridge 绑定时必须同步在此登记**——
        /// 漏登记会让 Lua 在运行时才发现能力缺失（正是 §10 要避免的）。
        /// ApiVersion 2（批C-⑥）：ui 门面改按 lua_path 寻址并增 Replace；新增输入控件族（G15～G17）、
        /// 列表（G6）与 settings 门面——Lua 侧可见面变更，候选清单据此下限比较。
        /// ApiVersion 3（房主开局批）：新增 room 门面（IsOwner/OnOwner/StartMatch——委托形全在
        /// GenConfig 白名单在册，零新增生成形）。
        /// ApiVersion 4（席位实时面批）：room 门面增 OnMembers（席位名单全量快照行表订阅——进房/断线/
        /// 恢复完成重发；委托形同为 Action&lt;LuaFunction&gt;，零新增生成形）。
        /// ApiVersion 5（房间等待人数批）：room 门面增 ExpectedPlayers（本房房规模拉取——房间页
        /// "等待玩家 N/M" 的分母；委托形 Func&lt;int&gt; 与 settings.Quality 同形，零新增生成形）。
        /// 版本值自 M3-4 起由 <see cref="LiteFramework.UI.UiApiManifest.ApiVersion"/> 生成（UiApiSpec 单源）；
        /// 上面的历史条目记录的是该值到达 5 的由来，不改写。
        /// </summary>
        public static BridgeCapabilitySet Default() => new BridgeCapabilitySet(
            apiVersion: LiteFramework.UI.UiApiManifest.ApiVersion,
            capabilities: new[]
            {
                "bridge.data",      // Bridge.data 门面（配置/数值读取）
                "bridge.ui",        // Bridge.ui 门面（页面打开/替换/关闭，按 lua_path）
                "bridge.content",   // Bridge.content 门面（注册表读取）
                "bridge.text",      // Bridge.text 门面（LText 取文/切语言——批C-②）
                "bridge.meta",      // Bridge.meta 门面（联机入口链路——批C-③）
                "bridge.settings",  // Bridge.settings 门面（音量/音效/画质——批C-⑥）
                "bridge.room",      // Bridge.room 门面（房间会话：房主事实/提前开局/席位名单/房规模）
            });
    }
}
