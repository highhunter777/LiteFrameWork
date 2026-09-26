using System;
using System.Collections.Generic;
using System.IO;

namespace Tools.DisciplineScan
{
    /// <summary>一个扫描目标：源根（相对项目根）+ 该根启用的规则子集 + 可选排除子根。</summary>
    public struct ScanTarget
    {
        public string Root;
        public LintRule[] Rules;

        /// <summary>从本目标排除的子根（相对项目根，前缀匹配；null/空 = 不排除）。
        /// 用途：同一大根下混有不同领域的代码（如 LiteSim 的确定性 Core 与表现层 View）。</summary>
        public string[] ExcludeRoots;

        public ScanTarget(string root, LintRule[] rules) : this(root, rules, null) { }

        public ScanTarget(string root, LintRule[] rules, string[] excludeRoots)
        {
            Root = root;
            Rules = rules;
            ExcludeRoots = excludeRoots;
        }
    }

    /// <summary>
    /// 默认多根配置（《测试开发方案》§7.3 ②「一个引擎 + 多根规则集」）。
    /// 规则定义只此一份；各根按各自领域启用子集。
    /// </summary>
    public static class ScanTargets
    {
        // 规则子集（必须在 Default 之前初始化——静态字段按声明顺序初始化）
        /// <summary>LiteSim：确定性数值层，R1~R5 全适用（R4 为 M8 起）+ R9 模组红线（判定必须在权威内）。</summary>
        public static readonly LintRule[] SimRules =
        {
            LintRule.R1Transcendental,
            LintRule.R2Fma,
            LintRule.R3FloatEquality,
            LintRule.R4DeterminismContainer,
            LintRule.R5BareUnityEditor,
            LintRule.R9ModInSim,
        };

        /// <summary>LiteFramework.Core：只守 R5（宏并集）；Core 合法使用 Dictionary，故不启 R4。</summary>
        public static readonly LintRule[] CoreRules =
        {
            LintRule.R5BareUnityEditor,
        };

        /// <summary>LiteNet：客户端与 .NET 服务器共用 → R5 必守（禁裸 UNITY_EDITOR——双端编译是红线）；
        /// R1–R4 属 Sim 确定性纪律，不适用于传输/协议层（广播遍历序非逻辑序）；Vendor/ 已全局排除。</summary>
        public static readonly LintRule[] NetRules =
        {
            LintRule.R5BareUnityEditor,
        };

        /// <summary>业务 Unity 层：只守 R6（原生协程）。</summary>
        public static readonly LintRule[] UnityRules =
        {
            LintRule.R6NativeCoroutine,
        };

        /// <summary>LiteGame 全域：R8 资源唯一入口 + R12 适配器边界（Editor 目录由 IsExcluded 排除）。</summary>
        public static readonly LintRule[] GameRules =
        {
            LintRule.R8ResourcesLoad,
            // R12 适配器边界（《客户端总设计》§5.1"形成逻辑边界和依赖测试"）：
            // YooAsset/xLua/DOTween 的实现只许在各自边界目录内 import。
            // 用途不只是"抓现有违规"——更是**阻止新耦合扩散**：
            // 在一体 asmdef 里，没有它，任何人都能再 import 一次而无人察觉。
            LintRule.R12AdapterBoundary,
        };

        /// <summary>薄壳/UI：R10 不得直发业务包（禁 INetworkService 契约）。</summary>
        public static readonly LintRule[] ShellUiRules =
        {
            LintRule.R10ShellSendsBusinessPacket,
        };

        /// <summary>RoomServer/Runtime：R11 纯化（《商业级通用服务端框架总设计》§8.1 禁止项——
        /// Transport/墙钟/Console/文件/proto 引用一概不得进入纯运行时层）。</summary>
        public static readonly LintRule[] RuntimePurityRules =
        {
            LintRule.R11RuntimePurity,
        };

        /// <summary>MetaServer：R11 同类把守（《Meta 服务专项设计》§3.2/§4.2）——
        /// Meta 的模块层与契约层不得引用 IO/墙钟/Console，也不得引用 LiteNet/proto/Kcp：
        /// 局外契约不混进战斗协议，且 Meta 类型永不进入 RoomServer/Runtime。
        /// 宿主装配层（Host/Infrastructure）合法使用 Web/IO，故排除在外。</summary>
        public static readonly LintRule[] MetaPurityRules =
        {
            LintRule.R11RuntimePurity,
        };

        /// <summary>LiteSim 下不适用确定性规则的表现层子根（View 跑引擎、有 GameObject，
        /// 与 Sim 的定点/纯 C# 约束是两回事；只守 R6 原生协程）。</summary>
        public static readonly string[] SimLayerExcludes =
        {
            "Assets/LiteSim/View",
        };

        /// <summary>MetaServer 的宿主装配层：合法使用 Kestrel/Web/IO/Console（服务端总设计 §12 Generic Host），
        /// 不属于模块或契约层，故不进 R11 扫描。目标根内其余子目录（Contracts/、Modules/、Infrastructure/）
        /// 随 §4.2 目录建起后**自动**纳入扫描，无需再改本文件。</summary>
        public static readonly string[] MetaHostExcludes =
        {
            "MetaServer/Host",
        };

        /// <summary>
        /// `Assets/LiteGame/` 下**允许存在的顶层目录**（《客户端总设计》§5 顶层分层）。
        ///
        /// 2026-09-26 建 `Adapters/` 层时补：此前顶层目录是历次拆分**自然长出来的**，
        /// 没有人检查过它们是否落在设计的层里——本次盘查发现 `Content/Net/Scripting/
        /// Serialization` 四个适配器平铺在根上、没有 `Adapters/` 层，只能靠人工阅读发现。
        ///
        /// 新增顶层目录必须在此登记，并说明它属于 §5 的哪一层（或为何是例外）。
        /// 未登记的目录会让本规则报红——**目录一变就红**，与 R12 边界表同一纪律。
        /// 非代码目录（`Lua`/`RawFile`/`link.xml` 等）也在列：它们不是"层"，但要有名有姓。
        /// </summary>
        public static readonly string[] LiteGameTopLevelDirs =
        {
            "Abstractions",          // §5 / §5.1：LiteClient.Abstractions
            "Adapters",              // §5 顶层第四层：Content.YooAsset / Network.Kcp / Scripting.XLua / Serialization.Luban
            "App",                   // §5 顶层第一层 Game.App
            "Runtime",               // §5 顶层第二层 Client.Runtime（目录名 = 层标签）
            "UI",                    // §5 框图 "UI Runtime"（游戏侧，故 LiteGame.UI 而非 LiteClient.UI）
            "DevHUD",                // 开发面工具（不在 §5 层内，登记为例外）
            "Editor",                // 编辑器工具程序集（同上）
            "Lua",                   // 脚本资产（非程序集目录）
            "RawFile",               // 配置字节资产（非程序集目录）
        };

        /// <summary>
        /// 校验 `Assets/LiteGame` 顶层目录全部已登记（未登记 = 逃出设计分层，见上）。
        /// 返回未登记目录名（无则空）。
        /// </summary>
        public static List<string> ValidateLiteGameTopLevel(string projectRoot)
        {
            var unregistered = new List<string>();
            string root = Path.Combine(projectRoot, "Assets", "LiteGame");
            if (!Directory.Exists(root)) return unregistered;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (Array.IndexOf(LiteGameTopLevelDirs, name) < 0) unregistered.Add(name);
            }
            unregistered.Sort(StringComparer.Ordinal);
            return unregistered;
        }

        /// <summary>默认扫描目标集合。</summary>
        public static readonly ScanTarget[] Default =
        {
            new ScanTarget("Assets/LiteSim", SimRules, SimLayerExcludes),
            new ScanTarget("Assets/LiteSim/View", UnityRules),                                 // 表现层：只守 R6
            new ScanTarget("Assets/LiteNet", NetRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Core", CoreRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Unity", UnityRules),
            // 注（2026-09-26）：原有一条 `ScanTarget("Assets/LiteGame/Scripts/Runtime", UnityRules)`
            // ——**该目录不存在**（实际为 `Assets/LiteGame/Runtime`，无 `Scripts/`），
            // 即 R6（禁原生协程）自加入起就在**空扫**。
            // 未按原意修正路径：`Assets/LiteGame` 全域挂在 GameRules（非 UnityRules），
            // 而 R6 的正则 `\byield\s+return\b` **不区分 C# 迭代器与 Unity 协程**——
            // 实测 `ContentTrustAnchors.cs:46` 的 `yield return new Anchor(...)`（IEnumerable 迭代器）
            // 会被误报。**空扫与误报都不是我们想要的**，故删掉该目标并把 R6 的矫正登记为待办
            // （需先把正则收紧到 `IEnumerator`/`StartCoroutine` 语境，或引入更精确的判定）。
            new ScanTarget("Assets/LiteGame", GameRules),                                      // R8 资源唯一入口 + R12 适配器边界
            new ScanTarget("Assets/LiteGame/UI", ShellUiRules),                                // R10 薄壳/UI 不发业务包
            // 注（2026-09-26）：此处原写作 `Assets/LiteGame/Scripts/Runtime/Shell/UI`——**不存在**，
            // R10 亦一直空扫。先按真实路径修正为 `Runtime/Shell/UI`；同日 §5.1 第五刀拆出
            // `LiteGame.UI` 程序集，随目录改为 `Assets/LiteGame/UI`（同 R12 边界表：目录一变规则就红）。
            // 该目标的存在性由 `纪律_R12_已接入真实扫描目标_且目标路径存在` 钉住。
            new ScanTarget("RoomServer/Runtime", RuntimePurityRules),                          // R1 纯运行时层（R1《服务端总设计》§8.1）
            new ScanTarget("MetaServer", MetaPurityRules, MetaHostExcludes),                    // Meta 模块/契约层（《Meta 服务专项设计》§4.2）
        };

        /// <summary>
        /// .meta 扫描根（R7 非法 GUID，2026-09-15 事故：64 位 base64 guid 被 Unity 拒收）。
        /// 任何 .meta 的 guid 都必须是 32 位 hex；Editor 目录**不豁免**（meta 不是 C#，
        /// <see cref="DisciplineScanner.IsExcluded"/> 的 Editor 排除不适用于 meta 扫描）。
        /// </summary>
        public static readonly string[] MetaRoots =
        {
            "Assets",
            "Packages",
        };
    }
}
