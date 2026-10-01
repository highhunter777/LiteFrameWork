using System;
using System.Collections.Generic;
using LiteFramework;

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
        /// 当前包含 LuaComponent 实际绑定的三个门面（`Bridge.data`/`Bridge.ui`/`Bridge.content`）
        /// 对应的能力名。**新增 Bridge 绑定时必须同步在此登记**——漏登记会让 Lua 在运行时
        /// 才发现能力缺失（正是 §10 要避免的）。
        /// </summary>
        public static BridgeCapabilitySet Default() => new BridgeCapabilitySet(
            apiVersion: 1,
            capabilities: new[]
            {
                "bridge.data",      // Bridge.data 门面（配置/数值读取）
                "bridge.ui",        // Bridge.ui 门面（页面打开/关闭）
                "bridge.content",   // Bridge.content 门面（注册表读取）
            });
    }

    /// <summary>
    /// Bridge 能力健康探针（§10"新 Lua 引用旧包不存在的能力必须在执行前拒绝"）。
    ///
    /// **检查方式**：候选脚本集合声明的**要求能力**（由脚本映射给出）必须被当前
    /// <see cref="BridgeCapabilitySet"/> 覆盖；缺一即不健康——**在执行前拒绝**，
    /// 而不是等 Lua 跑到那一行才报 "attempt to call a nil value"。
    ///
    /// 与 <c>CandidateScriptSet</c> 的分工：后者校验脚本集合自身的完整性（依赖闭包/环），
    /// 本探针校验"脚本集合要求的能力 vs 本包具备的能力"。
    /// </summary>
    public sealed class BridgeCapabilityHealthProbe : IHealthProbe
    {
        private readonly BridgeCapabilitySet _capabilities;
        private readonly Func<ReleaseManifest, IReadOnlyList<string>> _requiredCapabilities;

        public string Name => "bridge-capabilities";

        /// <param name="capabilities">本包具备的能力。</param>
        /// <param name="requiredCapabilities">清单 → 候选要求的能力集合（装配点/发布描述注入）。</param>
        public BridgeCapabilityHealthProbe(BridgeCapabilitySet capabilities,
            Func<ReleaseManifest, IReadOnlyList<string>> requiredCapabilities)
        {
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _requiredCapabilities = requiredCapabilities ?? throw new ArgumentNullException(nameof(requiredCapabilities));
        }

        public System.Threading.Tasks.Task<string> CheckAsyncCore(ReleaseManifest candidate)
            => System.Threading.Tasks.Task.FromResult(Check(candidate));

        /// <summary>纯判定（L1 可测；与 UniTask 无关）。返回 null = 全部覆盖。</summary>
        public string Check(ReleaseManifest candidate)
        {
            if (candidate == null) return "候选为空";

            IReadOnlyList<string> required = _requiredCapabilities(candidate);
            if (required == null || required.Count == 0) return null;

            var missing = new List<string>();
            foreach (string c in required)
                if (!_capabilities.Has(c)) missing.Add(c);

            return missing.Count == 0
                ? null
                : $"候选要求的能力本包不具备（§10 须在执行前拒绝）：{string.Join(", ", missing)}";
        }

        public Cysharp.Threading.Tasks.UniTask<string> CheckAsync(
            ReleaseManifest candidate, System.Threading.CancellationToken ct = default)
            => Cysharp.Threading.Tasks.UniTask.FromResult(Check(candidate));
    }
}
